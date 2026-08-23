using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// A bridge as it is authored: the two mouths, and nothing else (design §5).
    ///
    /// Everything else - which cells are piers, what is left open under the deck, how much the crossing costs -
    /// is *derived* from these two by <see cref="Bridge.TryShape"/>, and that is the same argument entrances
    /// won in §14 step 5. An author who has to keep a list of structure cells in step with a pair of mouths
    /// will eventually not, and the failure is a bridge with a hole in it or a mouth an agent cannot stand on -
    /// neither of which anything downstream could recognise as a mistake rather than a map. Two cells cannot
    /// disagree with themselves.
    ///
    /// Both offsets are unrotated and relative to <c>BuildingPlacement.OriginCell</c>, like every other
    /// authored offset, so rotating a bridge is the ordinary case and not a second code path.
    /// </summary>
    public struct BridgeSpan : IComponentData
    {
        /// <summary>
        /// The cell agents step on from. Must be walkable: it is outside the structure, not part of it.
        /// </summary>
        public int2 EntryOffset;

        /// <summary>
        /// The cell they are put down on. Must share an axis with <see cref="EntryOffset"/> and be at least
        /// three cells away, or there is no room for a pier at each end with the mouths outside them.
        /// </summary>
        public int2 ExitOffset;
    }
}
