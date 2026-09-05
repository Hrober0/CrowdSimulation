using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// A building that puts things back on the ground near it (design §14 step 11) - the other half of
    /// <see cref="Reaps"/>, and absent rather than disabled on a building that only takes.
    ///
    /// It carries everything needed to make the thing it plants, because `Rts` has no catalog to ask. What a
    /// sapling costs to walk through and what felling one gives are content decisions, filled in by the game
    /// layer when the building is placed - exactly as <see cref="Reaps.Yields"/> is.
    /// </summary>
    public struct Sows : IComponentData
    {
        /// <summary>What it plants.</summary>
        public ObjectKind Plants;

        /// <summary>What the grown thing adds to its cell's cost.</summary>
        public ushort PlantCost;

        /// <summary>What felling one gives, and how much - so a planted tree is worth cutting down.</summary>
        public ItemId Yields;

        public int YieldAmount;

        /// <summary>How far out it will plant. See <see cref="Reaps.Range"/> for why a range is safe here.</summary>
        public int Range;
    }
}
