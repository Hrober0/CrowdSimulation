using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Examples.Storage
{
    // ─────────────────────────────────────────────────────────────────────────
    // Singleton component — lives on a single entity created by the system
    // ─────────────────────────────────────────────────────────────────────────

    public struct HolderSpatilEntry
    {
        public Entity Entity;
        public float3 Position;
    }

    public struct IdleHolderGridSingleton : IComponentData
    {
        public NativeParallelMultiHashMap<int2, HolderSpatilEntry> Cells;
        public float CellSize;
    }
}