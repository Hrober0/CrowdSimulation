using GridNav;
using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// Something that shoots (design §14 step 13).
    ///
    /// One component for a turret and for a soldier, which is the payoff of step 12 having built the turret
    /// first: a turret turned out to be "a thing with a position that hurts what it can reach", and so is a
    /// soldier. The two differ in where the position comes from - a footprint or an <see cref="AgentMove"/> -
    /// and in nothing else, so <c>AttackSystem</c> has two queries and one targeting rule rather than two of
    /// each.
    ///
    /// Enableable, and carried by every agent rather than added to the armed ones. §9 says there is one agent
    /// archetype; making a soldier by adding a component would split it, and it would make conversion a
    /// structural change. Disabled, it costs a bit. Enabled, the agent is a soldier - there is no separate
    /// <c>Soldier</c> tag, because "is armed" and "is a soldier" would then be two facts that could disagree.
    /// </summary>
    public struct Weapon : IComponentData, IEnableableComponent
    {
        /// <summary>How far it reaches, in cells, from the middle of whatever carries it.</summary>
        public float Range;

        public int Damage;

        public float ReloadSeconds;

        /// <summary>
        /// Earliest time it may fire again. A time rather than a countdown, because the economy runs at 10 Hz
        /// and a countdown decremented by a tick's delta would make the rate of fire a property of the tick
        /// rate rather than of the weapon.
        /// </summary>
        public double NextShotTime;

        /// <summary>
        /// What it last fired at, so the panel can say. Not a claim on the target: two soldiers shooting the
        /// same raider is correct, and reserving one would be a mechanism with no question behind it.
        /// </summary>
        public Entity LastTarget;

        /// <summary>
        /// How hard it hits *structures*, for routing (design §14.4). Separate from <see cref="Damage"/> on
        /// purpose: hurting people and knocking down walls are different jobs, and a weapon good at one is
        /// often useless at the other. It is authored rather than derived from the damage figure, so a
        /// battering ram and a rifle can differ without either being renumbered.
        ///
        /// <see cref="BreachClass.None"/> - the default - routes exactly like a civilian: round the wall.
        /// </summary>
        public BreachClass Breach;

        public bool IsArmed => Range > 0f && Damage > 0;
    }
}
