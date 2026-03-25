using System;
using Unity.Entities;
using Unity.Mathematics;

namespace Examples.Storage
{
    public struct StorageComponent : IComponentData
    {
        public float3 WorldPosition;
    }
}