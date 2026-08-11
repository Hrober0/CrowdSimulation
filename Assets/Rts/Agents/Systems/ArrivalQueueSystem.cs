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
    /// The queue is recomputed from positions every frame and stores nothing. That is deliberate: a
    /// reservation would need a release path for agents that die, are re-tasked, or are given up on by the
    /// watchdog - the same leak <see cref="InteriorClaim"/> needs a whole pass to guard against - and there is
    /// nothing to leak if nothing is held. Rank comes out stable anyway, because the agent at the front is the
    /// nearest and the ones behind it are being told to stay further back.
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
        /// A hold point is steered at in a straight line, so it is only honest over ground the agent can
        /// practically see. Further out than this the flow field is still in charge and nothing is held.
        /// </summary>
        private const float ENGAGE_DISTANCE = 5f;

        /// <summary>
        /// Where the second agent waits, in cells from the destination. Beyond
        /// <see cref="TaskStep.DOOR_ARRIVE_DISTANCE"/>, so the one at the front has room to finish its
        /// approach without the next one standing in it.
        /// </summary>
        private const float FIRST_HOLD_DISTANCE = 2f;

        private const float HOLD_SPACING = 1f;

        private ComponentLookup<PathFollow> _follows;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridWorld>();

            _follows = state.GetComponentLookup<PathFollow>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            GridMap map = SystemAPI.GetSingleton<GridWorld>().Map;

            NativeList<Waiting> waiting = Collect(ref state);

            // No early out on a single contender: an agent left alone at a destination it was queued for has
            // to be let go of, and that happens by being ranked first here.
            waiting.Sort(new ByDestinationThenDistance());

            _follows.Update(ref state);
            Apply(map, waiting);

            waiting.Dispose();
        }

        private NativeList<Waiting> Collect(ref SystemState state)
        {
            var waiting = new NativeList<Waiting>(64, Allocator.Temp);

            foreach ((RefRO<AgentMove> agent, RefRO<PathFollow> follow, Entity entity)
                     in SystemAPI.Query<RefRO<AgentMove>, RefRO<PathFollow>>().WithEntityAccess())
            {
                PathFollow path = follow.ValueRO;

                // Still in the hands of the long-range tier: the waypoint is a gate rather than the
                // destination, and there is no straight line to hold on yet.
                if (!path.WaypointCell.Equals(path.GoalCell))
                {
                    continue;
                }

                float2 position = agent.ValueRO.Position;
                float distance = math.distance(position, GridCoords.CellCenter(path.GoalCell));
                if (distance > ENGAGE_DISTANCE)
                {
                    continue;
                }

                waiting.Add(new Waiting
                {
                    Agent = entity,
                    Destination = path.GoalCell,
                    Position = position,
                    Distance = distance,
                });
            }

            return waiting;
        }

        /// <summary>
        /// Walks the sorted list handing out places. Sorted by destination first, so one pass covers every
        /// contested cell in the world and a change of destination is where the count starts again.
        /// </summary>
        private void Apply(in GridMap map, in NativeList<Waiting> waiting)
        {
            int rank = 0;

            for (int i = 0; i < waiting.Length; i++)
            {
                Waiting agent = waiting[i];
                rank = i > 0 && waiting[i - 1].Destination.Equals(agent.Destination) ? rank + 1 : 0;

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
                    path.HoldPoint = HoldPointFor(map, agent, rank);
                }

                _follows[agent.Agent] = path;
            }
        }

        /// <summary>
        /// Where an agent of this rank waits: back along its own approach. A line therefore forms on the side
        /// the traffic is coming from, without anything here having to know which wall the door is in.
        /// </summary>
        private static float2 HoldPointFor(in GridMap map, in Waiting agent, int rank)
        {
            float2 destination = GridCoords.CellCenter(agent.Destination);
            float2 outward = agent.Position - destination;

            // Standing on the destination itself: no approach line to back off along, so wait here and let
            // the one in front clear out.
            if (math.lengthsq(outward) < math.EPSILON)
            {
                return agent.Position;
            }

            float distance = math.min(
                FIRST_HOLD_DISTANCE + (rank - 1) * HOLD_SPACING,
                ENGAGE_DISTANCE
            );

            float2 hold = destination + math.normalize(outward) * distance;

            // The line back may cut a corner the agent walked round, so the point can land in a wall. Waiting
            // where it already stands is always allowed, and always somewhere it fits.
            return map.IsPassable(GridCoords.CellOf(hold)) ? hold : agent.Position;
        }

        private struct Waiting
        {
            public Entity Agent;
            public int2 Destination;
            public float2 Position;
            public float Distance;
        }

        /// <summary>
        /// Groups by destination, nearest first within a group. The index tie-break is not cosmetic: two
        /// agents exactly as far out would otherwise swap places on the whims of iteration order, and both
        /// would keep being told to give way to the other.
        /// </summary>
        private struct ByDestinationThenDistance : IComparer<Waiting>
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

                if (a.Distance != b.Distance)
                {
                    return a.Distance < b.Distance ? -1 : 1;
                }

                return a.Agent.Index.CompareTo(b.Agent.Index);
            }
        }
    }
}
