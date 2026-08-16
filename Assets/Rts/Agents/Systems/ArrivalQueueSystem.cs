using System.Collections.Generic;
using GridNav;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// Makes agents heading for the same cell take turns at it instead of fighting over it (design §8).
    ///
    /// Doorways are what this is for, but it is not written in terms of doors, because the door is not the
    /// problem - one destination that several agents have to *occupy* is. A hauler stands on a doorstep for
    /// the length of a pickup without ever going inside, a resident walks onto it to disappear, and a worker
    /// arrives on it to stay: all three are agents converging on a single cell, and ORCA is symmetric, so each
    /// yields to the others and none of them gets there. <c>Interior.Capacity</c> caps how many may be *in* a
    /// building; nothing capped how many may crowd its step.
    ///
    /// The queue is recomputed from the world every frame and stores nothing. That is deliberate: a
    /// reservation would need a release path for agents that die, are re-tasked, or are given up on by the
    /// watchdog - the same leak <see cref="InteriorClaim"/> needs a whole pass to guard against - and there is
    /// nothing to leak if nothing is held.
    ///
    /// Four things about *how* it ranks matter more than the ranking:
    ///
    /// **Order comes from the flow field, not from a straight line.** The field for the destination is already
    /// built and shared by everyone walking to it, so "how far is this agent from the door" is one array read
    /// of the exact remaining path cost. A straight line is a different question, and at an awkward door it
    /// gives the wrong answer: the agent nearest as the crow flies can be the one on the wrong side of a
    /// one-way road, and making it the front of the queue stops everyone who *can* get in from trying.
    ///
    /// **An agent with no route is not a contender.** It reads as unreachable, is left out, and is then the
    /// watchdog's business - rather than standing at the head of a line it cannot lead.
    ///
    /// **Agents already standing on the destination count.** They hold the cell without walking to it, which is
    /// what an <c>Interact</c> on a doorstep and a <see cref="DoorUse"/> both are, so they take the front place
    /// and the queue forms behind them instead of on top of them (§15).
    ///
    /// **Waiting counts too.** Cost alone is a queue that never lets anyone in: a trickle of agents arriving
    /// nearer than whoever is waiting takes the front every time, and the one already there is overtaken
    /// forever. Time spent queueing is worth <see cref="AGE_STEPS_PER_SECOND"/> steps of closeness, so the
    /// wait an agent has already served is exactly what a newcomer has to beat. Everyone in the line ages at
    /// the same rate, so this never reorders the line itself - it only decides the line against new arrivals.
    ///
    /// Only ranks behind the front are ever held, which is what keeps a queue from deadlocking: the agent at
    /// the front is still watched by <see cref="WatchdogSystem"/>, so a head that really is wedged is dropped
    /// after its stall window and the queue moves up.
    /// </summary>
    [UpdateInGroup(typeof(RtsAgentGroup))]
    [UpdateAfter(typeof(PathRequestSystem))]
    [UpdateBefore(typeof(PathFollowSystem))]
    public partial struct ArrivalQueueSystem : ISystem
    {
        /// <summary>
        /// How close to its destination an agent has to be before it is queued at all.
        ///
        /// This is the radius the queue *forms* in, not the length it may reach: an agent given a place behind
        /// the front walks back to it, so a long line extends past this distance. What keeps that honest is
        /// that a held agent is re-ranked wherever it stands (see <see cref="Collect"/>) - a queue member that
        /// dropped out of the ranking would keep holding a place nobody was maintaining, and wait for a turn
        /// that could never come.
        /// </summary>
        private const float ENGAGE_DISTANCE = 5f;

        /// <summary>
        /// Where the second agent waits, in cells from the destination. Beyond
        /// <see cref="TaskStep.DOOR_ARRIVE_DISTANCE"/>, so the one at the front has room to finish its
        /// approach without the next one standing in it.
        /// </summary>
        private const float FIRST_HOLD_DISTANCE = 2f;

        private const float HOLD_SPACING = 1f;

        /// <summary>
        /// Where the line stops growing. Past this the agents are out of the way of the door already, and
        /// spacing them further only sends them on a longer walk back when their turn comes.
        /// </summary>
        private const float MAX_HOLD_DISTANCE = 8f;

        /// <summary>
        /// How near its destination a stopped agent has to be to count as standing on it. A shade over
        /// <see cref="TaskStep.DOOR_ARRIVE_DISTANCE"/>, which is the furthest out an agent is ever allowed to
        /// consider itself arrived.
        /// </summary>
        private const float OCCUPANT_DISTANCE = TaskStep.DOOR_ARRIVE_DISTANCE + 0.2f;

        /// <summary>
        /// What a second of waiting is worth, in steps of the way in.
        ///
        /// It bounds how long anyone can be overtaken: an agent behind by N steps takes at most N divided by
        /// this to reach the front, whatever else arrives meanwhile. Two steps a second empties a queue of the
        /// depth the engage radius can hold in a few seconds, which is the order of one or two door
        /// transitions - fast enough that nobody stands out there wondering, slow enough that an agent two
        /// cells from a free door is still let straight in.
        /// </summary>
        private const float AGE_STEPS_PER_SECOND = 2f;

        private ComponentLookup<PathFollow> _follows;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<FlowFieldCache>();

            _follows = state.GetComponentLookup<PathFollow>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            FlowFieldCache cache = SystemAPI.GetSingleton<FlowFieldCache>();
            var now = (float)SystemAPI.Time.ElapsedTime;

            NativeHashMap<int2, int> occupants = CollectOccupants(ref state);
            NativeList<Waiting> waiting = Collect(ref state, cache, now);

            // No early out on a single contender: an agent left alone at a destination it was queued for has
            // to be let go of, and that happens by being ranked first here.
            waiting.Sort(new ByDestinationThenCost());

            _follows.Update(ref state);
            Apply(waiting, occupants);

            waiting.Dispose();
            occupants.Dispose();
        }

        /// <summary>
        /// Who is standing on a destination rather than walking to it, counted per cell.
        ///
        /// An agent that has arrived has <see cref="PathFollow"/> disabled but still has steps to run - it is
        /// mid-pickup, mid-<see cref="DoorUse"/>, waiting for a door, or a frame away from its next step - and
        /// for as long as that lasts it is what the next agent is queueing for. Agents that have gone inside
        /// are excluded for free, because <see cref="AgentMove"/> is disabled on them and this query wants it
        /// enabled: an occupant has to be on the map to be in the way.
        ///
        /// Standing near the cell is checked rather than assumed. A goal outlives the walk that set it, so an
        /// agent that stopped somewhere else entirely still names one - and counting that would hold back a
        /// queue at a door on the other side of the map.
        /// </summary>
        private NativeHashMap<int2, int> CollectOccupants(ref SystemState state)
        {
            var occupants = new NativeHashMap<int2, int>(16, Allocator.Temp);

            foreach ((RefRO<AgentMove> agent, RefRO<PathFollow> follow, EnabledRefRO<PathFollow> walking,
                      DynamicBuffer<TaskStep> steps)
                     in SystemAPI.Query<RefRO<AgentMove>, RefRO<PathFollow>, EnabledRefRO<PathFollow>,
                                        DynamicBuffer<TaskStep>>()
                                 .WithPresent<PathFollow>())
            {
                if (walking.ValueRO || steps.IsEmpty)
                {
                    continue;
                }

                int2 cell = follow.ValueRO.GoalCell;
                if (math.distance(agent.ValueRO.Position, GridCoords.CellCenter(cell)) > OCCUPANT_DISTANCE)
                {
                    continue;
                }

                occupants.TryGetValue(cell, out int count);
                occupants[cell] = count + 1;
            }

            return occupants;
        }

        /// <summary>
        /// Every agent that is in the running for a destination this frame, ranked by what its way in costs
        /// less what its wait has earned.
        ///
        /// An agent dropped from the ranking has its hold released and its wait forgotten on the spot. Holding
        /// is only ever true because this system said so on an earlier frame, so leaving it set on an agent
        /// that is no longer ranked would strand it: nothing else clears it, and a holding agent is exempt
        /// from the watchdog.
        /// </summary>
        private NativeList<Waiting> Collect(ref SystemState state, in FlowFieldCache cache, float now)
        {
            var waiting = new NativeList<Waiting>(64, Allocator.Temp);

            foreach ((RefRO<AgentMove> agent, RefRW<PathFollow> follow, Entity entity)
                     in SystemAPI.Query<RefRO<AgentMove>, RefRW<PathFollow>>().WithEntityAccess())
            {
                PathFollow path = follow.ValueRO;

                // Still in the hands of the long-range tier: the waypoint is a gate rather than the
                // destination, and there is no last stretch to take turns over yet.
                bool nearGoal = path.WaypointCell.Equals(path.GoalCell);

                float2 position = agent.ValueRO.Position;
                float distance = math.distance(position, GridCoords.CellCenter(path.GoalCell));

                // Already holding, so ranked wherever it stands: its place was handed to it and may be
                // further out than the radius the queue forms in.
                bool engaged = nearGoal && (distance <= ENGAGE_DISTANCE || path.Holding);

                if (!engaged || !TryStepsToGoal(cache, path.GoalCell, GridCoords.CellOf(position), distance,
                                                out float steps))
                {
                    Release(follow);
                    continue;
                }

                if (!path.IsQueued)
                {
                    follow.ValueRW.QueuedSince = now;
                    path.QueuedSince = now;
                }

                waiting.Add(new Waiting
                {
                    Agent = entity,
                    Destination = path.GoalCell,
                    Cost = steps - (now - path.QueuedSince) * AGE_STEPS_PER_SECOND,
                });
            }

            return waiting;
        }

        /// <summary>Out of the queue: no place held, and no wait to cash in when it comes back.</summary>
        private static void Release(RefRW<PathFollow> follow)
        {
            if (!follow.ValueRO.Holding && !follow.ValueRO.IsQueued)
            {
                return;
            }

            follow.ValueRW.Holding = false;
            follow.ValueRW.QueuedSince = -1f;
        }

        /// <summary>
        /// How much of the walk is left, in steps, read straight out of the destination's flow field. False
        /// when there is no way in from here at all.
        ///
        /// Steps rather than raw field cost, because the number is compared against seconds of waiting and one
        /// of the two has to be in units the other can be expressed in. Dividing by <see cref="NavCost.STEP"/>
        /// gives "cheap steps' worth of walking left", which is exact on a road and an over-estimate on rough
        /// ground - which is the right way round, since rough ground really is slower to arrive over.
        ///
        /// The straight-line distance is the fallback for the one frame after a field has been asked for and
        /// before it has been built (§13.2 invariant 3). Every agent heading for one destination falls back
        /// together, so a group is always ranked by one measure rather than a mixture of two.
        /// </summary>
        private static bool TryStepsToGoal(
            in FlowFieldCache cache,
            int2 destination,
            int2 cell,
            float distance,
            out float steps)
        {
            if (!cache.TryGetSlot(destination, out int slot) || !cache.Covers(slot, cell))
            {
                steps = distance;
                return true;
            }

            ushort integration = cache.IntegrationAt(slot, cell);
            steps = integration / (float)NavCost.STEP;
            return integration != FlowField.UNREACHABLE;
        }

        /// <summary>
        /// Walks the sorted list handing out places. Sorted by destination first, so one pass covers every
        /// contested cell in the world and a change of destination is where the count starts again.
        /// </summary>
        private void Apply(in NativeList<Waiting> waiting, NativeHashMap<int2, int> occupants)
        {
            int rank = 0;

            for (int i = 0; i < waiting.Length; i++)
            {
                Waiting agent = waiting[i];

                if (i > 0 && waiting[i - 1].Destination.Equals(agent.Destination))
                {
                    rank++;
                }
                else
                {
                    // The first walker at a destination is only the front if nobody is standing on it.
                    occupants.TryGetValue(agent.Destination, out rank);
                }

                PathFollow path = _follows[agent.Agent];

                if (rank == 0)
                {
                    if (!path.Holding)
                    {
                        continue;
                    }

                    path.Holding = false;
                }
                else
                {
                    path.Holding = true;
                    path.HoldDistance = math.min(
                        FIRST_HOLD_DISTANCE + (rank - 1) * HOLD_SPACING,
                        MAX_HOLD_DISTANCE
                    );
                }

                _follows[agent.Agent] = path;
            }
        }

        private struct Waiting
        {
            public Entity Agent;
            public int2 Destination;

            /// <summary>Steps of walking left, less what waiting has earned. Lowest is the front.</summary>
            public float Cost;
        }

        /// <summary>
        /// Groups by destination, cheapest way in first within a group. The index tie-break is not cosmetic:
        /// two agents exactly as far out would otherwise swap places on the whims of iteration order, and both
        /// would keep being told to give way to the other.
        /// </summary>
        private struct ByDestinationThenCost : IComparer<Waiting>
        {
            public int Compare(Waiting a, Waiting b)
            {
                if (a.Destination.x != b.Destination.x)
                {
                    return a.Destination.x < b.Destination.x ? -1 : 1;
                }

                if (a.Destination.y != b.Destination.y)
                {
                    return a.Destination.y < b.Destination.y ? -1 : 1;
                }

                if (a.Cost != b.Cost)
                {
                    return a.Cost < b.Cost ? -1 : 1;
                }

                return a.Agent.Index.CompareTo(b.Agent.Index);
            }
        }
    }
}
