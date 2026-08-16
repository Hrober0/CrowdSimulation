using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// Where an agent is walking. Disabled when it has arrived or has nowhere to be, which is what takes an
    /// idle agent out of the movement systems entirely rather than making them skip it (§6).
    /// </summary>
    public struct PathFollow : IComponentData, IEnableableComponent
    {
        /// <summary>The cell the agent is ultimately heading for.</summary>
        public int2 GoalCell;

        /// <summary>How close to the goal's centre counts as arrived, in cells.</summary>
        public float ArriveDistance;

        /// <summary>
        /// What the agent is steering at right now, and the destination whose flow field it follows: the
        /// goal once it is near enough for the goal's field to cover it, and the next gate on the way until
        /// then. Written by <c>PathRouteSystem</c>.
        /// </summary>
        public int2 WaypointCell;

        /// <summary>The goal the gate route was worked out for, so a changed goal re-routes.</summary>
        public int2 RoutedGoal;

        /// <summary>The chunk the agent was in when it routed. Leaving it is what triggers a re-route.</summary>
        public int RoutedChunk;

        /// <summary>
        /// How close to the goal this agent is allowed to get while it waits its turn, in cells. Written by
        /// <see cref="ArrivalQueueSystem"/> when somebody else has the destination first (§8).
        ///
        /// A distance, and deliberately not a point. A point has to be *somewhere*, and every geometric guess
        /// at where an agent should wait is wrong as soon as the way in is not a straight line: a point back
        /// along the line to the goal can land in a wall, on the far side of a one-way road, or off the route
        /// the agent was actually walking - and steering at it is what made a queue at an awkward door pull
        /// its own members off the road they had to be on. A distance says only "no closer than this" and
        /// leaves *how* to the flow field, which is the one thing that knows the way in.
        /// </summary>
        public float HoldDistance;

        public bool Holding;

        /// <summary>
        /// When this agent joined the queue for its goal, in elapsed seconds, or negative when it is not
        /// queueing for anything.
        ///
        /// Waiting has to count for something. Rank is otherwise the cost of the way in and nothing else, so a
        /// steady trickle of agents arriving nearer than whoever is waiting keeps taking the front and the
        /// agent already there is never served - it is not even stuck, it is being politely overtaken forever.
        /// <see cref="ArrivalQueueSystem"/> ages this into the rank, exactly as <c>OrderAgingSystem</c> ages a
        /// waiting order into its priority (§8), and for the same reason.
        ///
        /// Reset by every new walk, so a hauler returning to a warehouse it visited a minute ago queues as a
        /// newcomer rather than cashing in the wait from its last trip.
        /// </summary>
        public float QueuedSince;

        /// <summary>Whether <see cref="QueuedSince"/> holds a time rather than "not queueing".</summary>
        public readonly bool IsQueued => QueuedSince >= 0f;
    }

    /// <summary>One gate on the way to the goal, in the order they are crossed.</summary>
    public struct PathRoute : IBufferElementData
    {
        public int GateIndex;
    }

    /// <summary>
    /// Set on the frame an agent reaches its goal, and consumed at the top of the next one. The one frame of
    /// lag is deliberate: arrival is noticed during integration, and acting on it there would mean structural
    /// changes in the middle of the movement phase (§13.3).
    /// </summary>
    public struct ArrivedTag : IComponentData, IEnableableComponent
    {
    }
}
