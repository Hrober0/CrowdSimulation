using System;
using Unity.Entities;
using Unity.Mathematics;

namespace Examples.Storage
{
    public struct StorageComponent : IComponentData
    {
        public float3 WorldPosition;
        public int AnalysisTimer;
        public StorageFlags Flags;
    }

    [Flags]
    public enum StorageFlags : byte
    {
        None = 0,
        IsDirty = 1 << 0,
        IsAccepting = 1 << 1,
        IsDispensing = 1 << 2,
    }
}