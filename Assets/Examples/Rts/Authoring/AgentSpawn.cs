using Unity.Entities;
using Unity.Mathematics;

namespace Examples.Rts
{
    /// <summary>
    /// A crowd waiting to be spawned. Disabled once spawned, so it fires exactly once and re-enabling it in
    /// the inspector spawns another crowd - the same one-shot convention as <see cref="GridCostPatch"/>.
    /// </summary>
    public struct AgentSpawn : IComponentData, IEnableableComponent
    {
        public int Count;

        /// <summary>Agents are scattered uniformly over this box, in simulation units.</summary>
        public float2 Center;

        public float2 Size;

        public int2 GoalCell;

        public float MaxSpeed;

        /// <summary>Must stay below ~0.45 of a cell, or agents clip the corners of blocked cells (§3).</summary>
        public float Radius;

        /// <summary>Units carried per trip. Zero means the agent will never be given a haul.</summary>
        public int CarryCapacity;

        public uint Seed;
    }
}
