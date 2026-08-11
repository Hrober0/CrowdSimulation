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
        /// Where to wait instead of pressing on, and whether to. Written by <see cref="ArrivalQueueSystem"/>
        /// when somebody else has the destination first (§8).
        ///
        /// A point, and deliberately not <see cref="WaypointCell"/>: a waypoint is a flow field request, and a
        /// field per waiting agent per busy door would be a cache full of fields whose whole purpose is to
        /// move one agent a cell backwards. A hold point is always on the line the agent is already standing
        /// on, so steering straight at it needs no field at all.
        /// </summary>
        public float2 HoldPoint;

        public bool Holding;
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
