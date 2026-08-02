using System;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// Everything the movement and avoidance tiers need about an agent (design §9). The simulation is 2D
    /// <c>float2</c> throughout; the view maps it to world space and nothing here knows how.
    ///
    /// <see cref="PrefVelocity"/> is where the agent *wants* to go, written by path following from the flow
    /// field gradient. <see cref="Velocity"/> is what RVO allowed it to do. Keeping the two apart is what
    /// lets avoidance override intent without losing it.
    /// </summary>
    public struct AgentMove : IComponentData, IEquatable<AgentMove>
    {
        public Entity Entity;

        public float2 Position;
        public float2 Velocity;
        public float2 PrefVelocity;

        public float MaxSpeed;

        /// <summary>Must stay below ~0.45 of a cell, or agents clip the corners of blocked cells (§3).</summary>
        public float Radius;

        public bool Equals(AgentMove other) => Entity.Equals(other.Entity);

        public override bool Equals(object obj) => obj is AgentMove other && Equals(other);

        public override int GetHashCode() => Entity.GetHashCode();
    }
}
