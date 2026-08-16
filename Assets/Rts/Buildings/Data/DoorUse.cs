using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    public enum DoorUseKind : byte
    {
        /// <summary>On the way in: still on the map, being taken off it.</summary>
        Enter,

        /// <summary>On the way out: back on the map, not walking yet.</summary>
        Exit,
    }

    /// <summary>
    /// An agent in a doorway right now - half in, half out, for a known length of time (design §15).
    ///
    /// Going in and coming out used to be instantaneous: three flags flipped, and an agent leaving a building
    /// *materialised* in the middle of whatever was standing outside, handing avoidance an overlap to solve
    /// from a standing start. A duration is what turns that into something the crowd can react to.
    ///
    /// Two things fall out of it rather than needing mechanisms of their own:
    ///
    /// **The doorway becomes a resource held for a known time.** One agent may use a door at a time, so the
    /// last hole in the queue of §8 closes - <see cref="ArrivalQueueSystem"/> counts an agent in a doorway as
    /// standing on that cell, and the next one queues behind it instead of walking in on top of it.
    ///
    /// **Exit outranks enter.** An agent coming out has nowhere else to be, and blocking it stalls everything
    /// the building is doing; an agent going in can wait one turn.
    ///
    /// A transitioning agent costs a view and a spatial hash entry and nothing else: it is the already
    /// supported "on the map, not walking" state, which is also what an agent standing through a pickup is.
    /// </summary>
    public struct DoorUse : IComponentData, IEnableableComponent
    {
        public Entity Building;

        /// <summary>The entrance cell being used, and the cell the transition is animated onto.</summary>
        public int2 Cell;

        public DoorUseKind Kind;

        public float Remaining;

        public float Duration;

        /// <summary>
        /// How far through the doorway the agent is: 0 fully outside, 1 fully inside. The only thing the view
        /// layer needs, and the reason the direction of travel is recorded rather than inferred.
        /// </summary>
        public float Inside
        {
            get
            {
                float done = Duration > 0f ? math.saturate(1f - Remaining / Duration) : 1f;
                return Kind == DoorUseKind.Enter ? done : 1f - done;
            }
        }
    }
}
