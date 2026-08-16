using System.Collections.Generic;
using GridNav;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// Takes agents in and out of buildings, one at a time per door and never instantly
    /// (design §6, §15, §13.3 #18).
    ///
    /// Going in ends with <see cref="AgentMove"/>, <see cref="PathFollow"/> and <see cref="ViewVisible"/>
    /// disabled. That is the whole of it, and every consequence §6 claims falls out of those three flags: no
    /// path, no steering, no RVO neighbour query, no spatial hash entry and no GameObject. An agent resting in
    /// a hut costs nothing per frame because there is no per-frame loop left that can see it - not because
    /// anything was tuned.
    ///
    /// What is *not* instant is getting there. A <see cref="DoorUse"/> holds the doorway for a fixed time, so a
    /// door admits one agent at a time and the crowd outside has something with a known duration to queue for
    /// rather than a teleport to be surprised by. An agent coming out is put on the map at the start of its
    /// transition and an agent going in is taken off it at the end, so for the length of either the agent is on
    /// the doorstep: visible, avoidable, and counted by <see cref="ArrivalQueueSystem"/>.
    ///
    /// **The request is the task step, not a queue.** A pending <c>Enter</c> or <c>Exit</c> sits at the head of
    /// the agent's <see cref="TaskStep"/> buffer until this system has finished with it, so "who is waiting for
    /// a door" is re-read from the world every frame. A queue of requests would have to survive a door being
    /// busy for several frames, and then it would need de-duplicating, ageing and a release path for every way
    /// an agent can stop wanting the door - the same leak the queue of §8 avoids by storing nothing.
    ///
    /// Single-threaded and after integration, because each transition writes two entities at once - the
    /// agent's flags and the building's occupancy - which is the shape §13.2 invariant 4 keeps off the job
    /// system.
    /// </summary>
    [UpdateInGroup(typeof(RtsAgentGroup))]
    [UpdateAfter(typeof(AgentIntegrateSystem))]
    public partial struct InteriorTransitionSystem : ISystem
    {
        /// <summary>
        /// How long a door takes to walk through, in seconds. Also the shortest time between two agents using
        /// the same door, so it is a throughput number as much as an animation one (§15: tuning).
        /// </summary>
        public const float DOOR_SECONDS = 0.4f;

        public void OnUpdate(ref SystemState state)
        {
            var busy = new NativeHashSet<int2>(8, Allocator.Temp);

            Advance(ref state, busy, SystemAPI.Time.DeltaTime);

            // Exits first, and both lists in entity order: a door handed out on the whims of chunk iteration
            // would make the same world replay differently.
            NativeList<Pending> waiting = CollectWaiting(ref state);
            waiting.Sort(new ExitsFirstThenIndex());
            Grant(ref state, waiting, busy);

            waiting.Dispose();
            busy.Dispose();
        }

        /// <summary>
        /// Counts the doorways in use down, finishes the ones that are done, and reports the cells still held.
        /// </summary>
        private void Advance(ref SystemState state, NativeHashSet<int2> busy, float deltaTime)
        {
            // Collected first: finishing a transition disables the component this query filters on, and
            // changing that under the iteration is asking the enumerator to skip agents at random.
            var completed = new NativeList<Entity>(8, Allocator.Temp);

            foreach ((RefRW<DoorUse> door, Entity agent) in SystemAPI.Query<RefRW<DoorUse>>().WithEntityAccess())
            {
                door.ValueRW.Remaining -= deltaTime;

                if (door.ValueRO.Remaining <= 0f)
                {
                    completed.Add(agent);
                    continue;
                }

                busy.Add(door.ValueRO.Cell);
            }

            EntityManager entities = state.EntityManager;
            foreach (Entity agent in completed)
            {
                Finish(entities, agent);
            }

            completed.Dispose();
        }

        /// <summary>
        /// Agents whose next step is a door they have not been let through yet. An agent already in a doorway
        /// is excluded - it has what it was waiting for.
        /// </summary>
        private NativeList<Pending> CollectWaiting(ref SystemState state)
        {
            var waiting = new NativeList<Pending>(8, Allocator.Temp);

            // WithPresent, because an agent waiting to come *out* of a building is exactly the one whose
            // DoorUse is disabled and whose AgentMove is too.
            foreach ((DynamicBuffer<TaskStep> steps, EnabledRefRO<DoorUse> inDoorway, Entity agent)
                     in SystemAPI.Query<DynamicBuffer<TaskStep>, EnabledRefRO<DoorUse>>()
                                 .WithPresent<DoorUse>()
                                 .WithEntityAccess())
            {
                if (inDoorway.ValueRO || steps.IsEmpty)
                {
                    continue;
                }

                TaskStep step = steps[0];
                if (step.Kind != TaskStepKind.Enter && step.Kind != TaskStepKind.Exit)
                {
                    continue;
                }

                waiting.Add(new Pending
                {
                    Agent = agent,
                    Building = step.Target,
                    Cell = step.Cell,
                    Kind = step.Kind == TaskStepKind.Enter ? DoorUseKind.Enter : DoorUseKind.Exit,
                });
            }

            return waiting;
        }

        private void Grant(ref SystemState state, in NativeList<Pending> waiting, NativeHashSet<int2> busy)
        {
            EntityManager entities = state.EntityManager;

            foreach (Pending pending in waiting)
            {
                if (!CanUseDoor(entities, pending))
                {
                    // Nothing to go through, or nothing to come out of: drop the step rather than wait on a
                    // door that will never open. A building can be demolished while an agent walks to it.
                    RetireStep(entities, pending.Agent);
                    continue;
                }

                // Add reports false when the cell was already there, which is the whole admission rule: one
                // agent in a doorway at a time.
                if (!busy.Add(pending.Cell))
                {
                    continue;
                }

                Begin(entities, pending);
            }
        }

        private static bool CanUseDoor(in EntityManager entities, in Pending pending)
        {
            if (!entities.Exists(pending.Agent) || !entities.HasComponent<AgentMove>(pending.Agent))
            {
                return false;
            }

            // Coming out needs no building. One demolished around its occupants must not trap them inside it -
            // putting them back on the map is strictly better than the alternative, and there is no state left
            // to give back anyway.
            if (pending.Kind == DoorUseKind.Exit)
            {
                return true;
            }

            return entities.Exists(pending.Building)
                   && entities.HasComponent<Interior>(pending.Building);
        }

        /// <summary>
        /// Opens the door. An <c>Exit</c> puts the agent back on the map here, at the start, so it is visible
        /// and avoidable for the whole of its way out; an <c>Enter</c> only stops it walking, and is taken off
        /// the map in <see cref="Finish"/>.
        /// </summary>
        private static void Begin(in EntityManager entities, in Pending pending)
        {
            Stop(entities, pending.Agent);

            if (pending.Kind == DoorUseKind.Exit)
            {
                StepOut(entities, pending);
            }

            entities.SetComponentData(pending.Agent, new DoorUse
            {
                Building = pending.Building,
                Cell = pending.Cell,
                Kind = pending.Kind,
                Remaining = DOOR_SECONDS,
                Duration = DOOR_SECONDS,
            });

            entities.SetComponentEnabled<DoorUse>(pending.Agent, true);
        }

        private static void Finish(in EntityManager entities, Entity agent)
        {
            if (!entities.Exists(agent))
            {
                return;
            }

            var door = entities.GetComponentData<DoorUse>(agent);
            entities.SetComponentEnabled<DoorUse>(agent, false);

            if (door.Kind == DoorUseKind.Enter)
            {
                StepIn(entities, agent, door.Building);
            }

            RetireStep(entities, agent);
        }

        /// <summary>The far side of an <c>Enter</c>: off the map, and counted as an occupant.</summary>
        private static void StepIn(in EntityManager entities, Entity agent, Entity building)
        {
            // Demolished mid-transition. The agent is still standing on the doorstep - it was never taken off
            // the map - so leaving it there is the whole of the recovery.
            if (!entities.Exists(building) || !entities.HasComponent<Interior>(building))
            {
                return;
            }

            Interior interior = entities.GetComponentData<Interior>(building);
            interior.Occupied++;
            entities.SetComponentData(building, interior);

            entities.SetComponentData(agent, new InsideBuilding { Building = building });
            entities.SetComponentEnabled<InsideBuilding>(agent, true);

            entities.SetComponentEnabled<AgentMove>(agent, false);

            SetVisible(entities, agent, false);
        }

        /// <summary>The near side of an <c>Exit</c>: back on the doorstep, standing, and visible.</summary>
        private static void StepOut(in EntityManager entities, in Pending pending)
        {
            Entity agent = pending.Agent;

            if (entities.Exists(pending.Building) && entities.HasComponent<Interior>(pending.Building))
            {
                Interior interior = entities.GetComponentData<Interior>(pending.Building);
                interior.Occupied = math.max(interior.Occupied - 1, 0);
                interior.Claimed = math.max(interior.Claimed - 1, 0);
                entities.SetComponentData(pending.Building, interior);
            }

            // Put back down on the doorstep. The agent kept its position while inside, but the building may
            // have been re-placed or the claim released from somewhere else in the meantime, and the entrance
            // cell is the one place it is certainly allowed to stand.
            AgentMove move = entities.GetComponentData<AgentMove>(agent);
            move.Position = GridCoords.CellCenter(pending.Cell);
            entities.SetComponentData(agent, move);

            entities.SetComponentEnabled<InsideBuilding>(agent, false);
            ReleaseClaimOn(entities, agent, pending.Building);

            entities.SetComponentEnabled<AgentMove>(agent, true);
            SetVisible(entities, agent, true);

            // PathFollow stays off: the agent is standing in a doorway with nothing to do until its next
            // GoTo step turns it back on.
        }

        /// <summary>
        /// Stops the agent dead for the length of the transition.
        ///
        /// Both velocities, not just the current one: nothing updates <see cref="AgentMove.PrefVelocity"/>
        /// while the agent is not walking, so a leftover value would be honoured the moment it steps out.
        /// </summary>
        private static void Stop(in EntityManager entities, Entity agent)
        {
            AgentMove move = entities.GetComponentData<AgentMove>(agent);
            move.Velocity = float2.zero;
            move.PrefVelocity = float2.zero;
            entities.SetComponentData(agent, move);

            entities.SetComponentEnabled<PathFollow>(agent, false);
        }

        /// <summary>
        /// Drops the <c>Enter</c> or <c>Exit</c> at the head of the task now that it has been dealt with. The
        /// step is what said "still waiting for a door", so it can only be retired here.
        /// </summary>
        private static void RetireStep(in EntityManager entities, Entity agent)
        {
            if (!entities.HasBuffer<TaskStep>(agent))
            {
                return;
            }

            DynamicBuffer<TaskStep> steps = entities.GetBuffer<TaskStep>(agent);
            if (steps.IsEmpty)
            {
                return;
            }

            if (steps[0].Kind == TaskStepKind.Enter || steps[0].Kind == TaskStepKind.Exit)
            {
                steps.RemoveAt(0);
            }
        }

        /// <summary>
        /// Gives back the claim, but only if it is a claim on the building being left.
        ///
        /// The two can differ, and the case is ordinary rather than exotic: a hauler resting in a hut is
        /// handed a job, its claim is moved to the building it has been sent to, and only *then* does it
        /// walk out of the hut. Clearing the claim on the way out would drop the new one on the floor.
        /// </summary>
        private static void ReleaseClaimOn(in EntityManager entities, Entity agent, Entity building)
        {
            if (!entities.HasComponent<InteriorClaim>(agent)
                || !entities.IsComponentEnabled<InteriorClaim>(agent))
            {
                return;
            }

            if (entities.GetComponentData<InteriorClaim>(agent).Building == building)
            {
                entities.SetComponentEnabled<InteriorClaim>(agent, false);
            }
        }

        /// <summary>
        /// Optional, because <see cref="ViewVisible"/> is the seam to a view layer that need not exist - a
        /// headless run has no GameObjects to release and should not have to carry a flag to say so.
        /// </summary>
        private static void SetVisible(in EntityManager entities, Entity agent, bool visible)
        {
            if (entities.HasComponent<ViewVisible>(agent))
            {
                entities.SetComponentEnabled<ViewVisible>(agent, visible);
            }
        }

        private struct Pending
        {
            public Entity Agent;
            public Entity Building;
            public int2 Cell;
            public DoorUseKind Kind;
        }

        /// <summary>
        /// Exits before enters (§15), then by entity index so a contested door is handed out the same way in
        /// every replay of the same world.
        /// </summary>
        private struct ExitsFirstThenIndex : IComparer<Pending>
        {
            public int Compare(Pending a, Pending b) =>
                a.Kind != b.Kind
                    ? (a.Kind == DoorUseKind.Exit ? -1 : 1)
                    : a.Agent.Index.CompareTo(b.Agent.Index);
        }
    }
}
