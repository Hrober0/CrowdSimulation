using System;
using Unity.Entities;
using Unity.Mathematics;

namespace ComplexNavigation
{
    public struct AgentCoreData : IComponentData, IEquatable<AgentCoreData>
    {
        public Entity Entity;
        
        public float2 Position;
        public float2 Velocity;
        public float2 PrefVelocity;
        public float MaxSpeed;
        public float Radius;

        public bool Equals(AgentCoreData other) => Entity.Equals(other.Entity);

        public override bool Equals(object obj) => obj is AgentCoreData other && Equals(other);

        public override int GetHashCode() => Entity.GetHashCode();
    }
}