using CustomNativeCollections;
using GridNav;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// Everything armed finds the nearest enemy in range and hurts it (design §14 step 13).
    ///
    /// One system for turrets and soldiers, because a <see cref="Weapon"/> is one component: the two queries
    /// below differ only in where a muzzle is, and they share the target search so that a turret and a
    /// soldier can never come to disagree about what counts as an enemy or about how far is too far.
    ///
    /// Agents are found through the spatial hash the avoidance and assignment phases already build. Buildings
    /// are found by walking the list, and that is not laziness - there is no cell-to-building index anywhere
    /// in the project (the demolish tool scans too), buildings are counted in dozens against agents in
    /// thousands, and an index would be a structure to keep in step for no measurable gain.
    ///
    /// Nothing claims its target. Two soldiers shooting one raider is the right answer, and the damage queue
    /// settles what actually happens - including who was already dead when the second shot landed.
    /// </summary>
    [UpdateInGroup(typeof(RtsEconomyGroup))]
    [UpdateBefore(typeof(DamageApplySystem))]
    public partial struct AttackSystem : ISystem
    {
        private ComponentLookup<Faction> _faction;
        private ComponentLookup<Health> _health;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AgentSpatialHash>();
            state.RequireForUpdate<DamageQueue>();

            _faction = state.GetComponentLookup<Faction>(isReadOnly: true);
            _health = state.GetComponentLookup<Health>(isReadOnly: true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            NativeSpatialHash<AgentMove> crowd = SystemAPI.GetSingleton<AgentSpatialHash>().Hash;
            DamageQueue damage = SystemAPI.GetSingleton<DamageQueue>();
            double now = SystemAPI.Time.ElapsedTime;

            _faction.Update(ref state);
            _health.Update(ref state);

            NativeList<StructureTarget> structures = CollectStructures(ref state);
            var candidates = new NativeList<AgentMove>(16, Allocator.Temp);

            // Turrets: a weapon bolted to a footprint. Enabled-ness is not asked about, because a building
            // that carries one is armed by construction - nothing ever disarms a turret.
            foreach ((RefRW<Weapon> weapon, RefRO<Faction> faction, DynamicBuffer<BuildingFootprintCell> footprint)
                     in SystemAPI.Query<RefRW<Weapon>, RefRO<Faction>, DynamicBuffer<BuildingFootprintCell>>())
            {
                if (footprint.IsEmpty)
                {
                    continue;
                }

                Fire(ref weapon.ValueRW, faction.ValueRO, CentreOf(footprint),
                     crowd, structures, candidates, damage, now);
            }

            // Soldiers: a weapon carried by a body. The query takes only enabled weapons, so a worker is
            // skipped by the same bit that says it is not a soldier - and an agent that has stepped inside a
            // building has AgentMove disabled, so it stops shooting and stops being shot at together.
            foreach ((RefRW<Weapon> weapon, RefRO<Faction> faction, RefRO<AgentMove> move)
                     in SystemAPI.Query<RefRW<Weapon>, RefRO<Faction>, RefRO<AgentMove>>())
            {
                Fire(ref weapon.ValueRW, faction.ValueRO, move.ValueRO.Position,
                     crowd, structures, candidates, damage, now);
            }

            candidates.Dispose();
            structures.Dispose();
        }

        /// <summary>One shot, if it is loaded and there is anything to shoot at.</summary>
        private void Fire(ref Weapon weapon, Faction mine, float2 muzzle,
                          in NativeSpatialHash<AgentMove> crowd, in NativeList<StructureTarget> structures,
                          NativeList<AgentMove> candidates, in DamageQueue damage, double now)
        {
            if (!weapon.IsArmed || now < weapon.NextShotTime)
            {
                return;
            }

            if (!TryFindTarget(muzzle, weapon.Range, mine, crowd, structures, candidates, out Entity target))
            {
                return;
            }

            damage.Enqueue(new DamageEvent { Target = target, Amount = weapon.Damage });

            weapon.LastTarget = target;
            weapon.NextShotTime = now + weapon.ReloadSeconds;
        }

        /// <summary>
        /// Nearest enemy in range, agent or building, or nothing.
        ///
        /// Nearest rather than weakest, most dangerous or most valuable: anything cleverer is a targeting
        /// *policy*, and a policy is a design decision about how a fight should read rather than a mechanism.
        /// It belongs above this, in whatever eventually gives a soldier its orders.
        ///
        /// **There is no line of sight.** Range is a plain distance, so a shot passes through walls, woods
        /// and buildings alike. That is an absence rather than a decision, and it is recorded as one in §15.
        /// </summary>
        private bool TryFindTarget(float2 muzzle, float range, Faction mine,
                                   in NativeSpatialHash<AgentMove> crowd,
                                   in NativeList<StructureTarget> structures,
                                   NativeList<AgentMove> candidates,
                                   out Entity target)
        {
            target = Entity.Null;
            float best = range * range;

            candidates.Clear();
            crowd.QueryAABB(muzzle - range, muzzle + range, candidates);

            // The hash spans an agent over every cell its body touches, so a big body comes back more than
            // once. Harmless: the nearest of several copies of one agent is that agent.
            foreach (AgentMove candidate in candidates)
            {
                if (!IsEnemy(candidate.Entity, mine))
                {
                    continue;
                }

                float distanceSq = math.lengthsq(candidate.Position - muzzle);
                if (distanceSq > best)
                {
                    continue;
                }

                best = distanceSq;
                target = candidate.Entity;
            }

            // Buildings second, and on the same yardstick, so a soldier standing between a raider and its
            // camp shoots the raider. A structure only wins the tie-break by being strictly nearer.
            foreach (StructureTarget structure in structures)
            {
                if (!Faction.AreEnemies(structure.Faction, mine))
                {
                    continue;
                }

                float distanceSq = math.lengthsq(structure.Centre - muzzle);
                if (distanceSq > best)
                {
                    continue;
                }

                best = distanceSq;
                target = structure.Entity;
            }

            return target != Entity.Null;
        }

        /// <summary>
        /// Whether that agent is worth a bullet: on another side, and not already dead this tick. Health is
        /// asked about because the applier writes it immediately and destroys later, so an entity can be at
        /// zero and still be standing in this frame's hash.
        /// </summary>
        private bool IsEnemy(Entity candidate, Faction mine)
        {
            if (!_faction.HasComponent(candidate) || !Faction.AreEnemies(_faction[candidate], mine))
            {
                return false;
            }

            return !_health.HasComponent(candidate) || _health[candidate].IsAlive;
        }

        /// <summary>
        /// Every building that can be shot at: it has to have a side to be on and something to lose. A
        /// bridge has neither and is not in here, which is why a crossing cannot be demolished by gunfire.
        /// </summary>
        private NativeList<StructureTarget> CollectStructures(ref SystemState state)
        {
            var structures = new NativeList<StructureTarget>(16, Allocator.Temp);

            foreach ((RefRO<Faction> faction, RefRO<Health> health,
                      DynamicBuffer<BuildingFootprintCell> footprint, Entity entity)
                     in SystemAPI.Query<RefRO<Faction>, RefRO<Health>,
                                        DynamicBuffer<BuildingFootprintCell>>().WithEntityAccess())
            {
                if (footprint.IsEmpty || !health.ValueRO.IsAlive)
                {
                    continue;
                }

                structures.Add(new StructureTarget
                {
                    Entity = entity,
                    Centre = CentreOf(footprint),
                    Faction = faction.ValueRO,
                });
            }

            return structures;
        }

        /// <summary>
        /// The middle of the building, not its origin cell. A turret is one cell today and the two would be
        /// the same number; they stop being the same the moment somebody puts a 2x2 one in the catalog, and a
        /// range measured from a corner is a range that is wrong on three sides.
        /// </summary>
        private static float2 CentreOf(in DynamicBuffer<BuildingFootprintCell> footprint)
        {
            float2 sum = float2.zero;
            foreach (BuildingFootprintCell cell in footprint)
            {
                sum += GridCoords.CellCenter(cell.Cell);
            }

            return sum / footprint.Length;
        }

        /// <summary>A building flattened to the two facts a shot needs: where it is and whose it is.</summary>
        private struct StructureTarget
        {
            public Entity Entity;
            public float2 Centre;
            public Faction Faction;
        }
    }
}
