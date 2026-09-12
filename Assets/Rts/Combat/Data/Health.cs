using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// What is left of something that can be destroyed (design §14 step 12).
    ///
    /// One component for agents and buildings alike, because everything that hurts one will hurt the other
    /// and a second mechanism would only mean two answers to "is it dead yet". Written by exactly one system,
    /// <see cref="DamageApplySystem"/>, in the same spirit as slot amounts (§13.2 invariant 2) - producers
    /// enqueue damage, one consumer subtracts it, and the race cannot occur rather than being unlikely.
    ///
    /// <see cref="Max"/> is not decoration. Step 13 prices a battered building lower than a fresh one, and
    /// what it needs is the *fraction* remaining quantised into a few bands - so the number a cost is derived
    /// from has to be here beside the one that is counted down.
    /// </summary>
    public struct Health : IComponentData
    {
        public int Current;

        public int Max;

        public bool IsAlive => Current > 0;

        public static Health Full(int max) => new() { Current = max, Max = max };
    }
}
