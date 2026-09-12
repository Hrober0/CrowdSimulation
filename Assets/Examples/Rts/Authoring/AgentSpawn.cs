using Rts;
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

        public int MaxHealth;

        /// <summary>
        /// Spawn with nothing to do rather than walking to <see cref="GoalCell"/>. Idle agents are picked up
        /// by the economy on the next tick - which is what you want when dropping a crowd into a working
        /// world, as opposed to a demo that wants everyone marching at one spot.
        /// </summary>
        public bool Idle;

        /// <summary>Which side the crowd is on. See <see cref="RtsFactions"/>.</summary>
        public byte Faction;

        /// <summary>
        /// What they carry, if anything. An unarmed spawn makes workers; an armed one makes soldiers, which
        /// is how the example gets an enemy onto the map at all - nothing in the game produces one until a
        /// faction has a camp of its own.
        /// </summary>
        public Weapon Weapon;

        /// <summary>How far an armed spawn will chase. Zero takes the default. See <see cref="Post"/>.</summary>
        public float Leash;

        public uint Seed;
    }
}
