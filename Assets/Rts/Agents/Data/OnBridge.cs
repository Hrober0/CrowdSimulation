using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// Set on an agent while it is being carried across a bridge (design §6).
    ///
    /// The bridge's <see cref="BridgeOccupant"/> buffer is what actually drives the crossing, so this looks
    /// like the same fact written twice - and it is the same shape as <see cref="InsideBuilding"/> next to
    /// <c>Interior.Occupied</c>, for the same reason. A crossing agent has had <see cref="PathFollow"/> turned
    /// off by something outside itself, and if the thing that turned it off ceases to exist there has to be
    /// something on the agent that says so. Otherwise a bridge destroyed with agents on it leaves them
    /// standing on a blocked deck with no steering and nothing anywhere that knows to give it back - the
    /// stranded-claim bug of §14 step 8, one layer down.
    ///
    /// So the buffer is the authority on *where along the deck*, and this is the authority on *whether at all*.
    /// </summary>
    public struct OnBridge : IComponentData, IEnableableComponent
    {
        public Entity Bridge;

        /// <summary>
        /// The far bank. Kept here rather than read back off the bridge, because it is wanted precisely when
        /// the bridge is the thing that has gone: an agent whose bridge is demolished under it is put down on
        /// the near bank if it can be, and this is what says which end it was heading for.
        /// </summary>
        public int2 Exit;

        /// <summary>The near bank, and the safe place to put an agent back if the crossing cannot finish.</summary>
        public int2 Entry;
    }
}
