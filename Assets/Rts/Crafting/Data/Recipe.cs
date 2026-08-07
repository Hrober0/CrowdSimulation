using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// What a building makes, and how long a batch takes (design §14 step 7).
    ///
    /// Inputs and outputs are both ordinary <see cref="StorageSlot"/>s on the same building, which is what
    /// makes a crafter need no special handling anywhere else: haulers fill its input slots because those
    /// slots ask (§7), and drain its output slots because those give everything away. The recipe only says
    /// what turns into what.
    /// </summary>
    public struct Recipe : IComponentData
    {
        /// <summary>Seconds of work per batch.</summary>
        public float CraftSeconds;

        /// <summary>Priority of the Work order this building posts. Unrelated to its slots' priorities.</summary>
        public byte Priority;
    }

    /// <summary>Consumed per batch. The slot it names must exist on the building.</summary>
    [InternalBufferCapacity(3)]
    public struct RecipeInput : IBufferElementData
    {
        public ItemId Item;

        public int Amount;
    }

    /// <summary>Produced per batch.</summary>
    [InternalBufferCapacity(2)]
    public struct RecipeOutput : IBufferElementData
    {
        public ItemId Item;

        public int Amount;
    }
}
