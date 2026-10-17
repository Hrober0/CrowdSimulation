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
    ///
    /// **Soldiers search in parallel** (design §13.5, "search parallel, commit serial"). Each one reads the
    /// hash, writes only its own <see cref="Weapon"/>, and hands its shot to the damage queue's parallel
    /// writer; the one applier settles the queue afterwards. Searched one after another on the main thread,
    /// sixteen hundred soldiers made every tick a 15 ms hitch.
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

            // TempJob rather than Temp, because the soldiers' job reads it from worker threads.
            NativeList<StructureTarget> structures = CollectStructures(ref state);

            var targeting = new Targeting
            {
                Crowd = crowd,
                Factions = _faction,
                Health = _health,
                Structures = structures.AsArray(),
            };

            // Turrets: a weapon bolted to a footprint. Enabled-ness is not asked about, because a building
            // that carries one is armed by construction - nothing ever disarms a turret. Dozens at most, so
            // the main thread does them before the soldiers' job takes the Weapon component.
            foreach ((RefRW<Weapon> weapon, RefRO<Faction> faction, DynamicBuffer<BuildingFootprintCell> footprint)
                     in SystemAPI.Query<RefRW<Weapon>, RefRO<Faction>, DynamicBuffer<BuildingFootprintCell>>())
            {
                if (footprint.IsEmpty)
                {
                    continue;
                }

                if (TryFire(ref weapon.ValueRW, faction.ValueRO, CentreOf(footprint), targeting, now,
                            out DamageEvent shot))
                {
                    damage.Enqueue(shot);
                }
            }

            // Soldiers: a weapon carried by a body. The job takes only enabled weapons and enabled bodies, so
            // a worker is skipped by the same bit that says it is not a soldier - and an agent that has stepped
            // inside a building has AgentMove disabled, so it stops shooting and stops being shot at together.
            state.Dependency = new SoldierFireJob
            {
                Crowd = crowd,
                Factions = _faction,
                Health = _health,
                Structures = structures.AsArray(),
                Damage = damage.AsParallelWriter(),
                Now = now,
            }.ScheduleParallel(state.Dependency);

            // Completed here: the queue lives inside a singleton, so nothing would order DamageApplySystem
            // after the job otherwise. The work inside it is still spread over every worker.
            state.Dependency.Complete();
            structures.Dispose();
        }

        /// <summary>One shot, if it is loaded and there is anything to shoot at.</summary>
        private static bool TryFire(ref Weapon weapon, Faction mine, float2 muzzle, in Targeting targeting,
                                    double now, out DamageEvent shot)
        {
            shot = default;

            if (!weapon.IsArmed || now < weapon.NextShotTime)
            {
                return false;
            }

            if (!targeting.TryFindTarget(muzzle, weapon.Range, mine, out Entity target))
            {
                return false;
            }

            shot = new DamageEvent { Target = target, Amount = weapon.Damage };

            weapon.LastTarget = target;
            weapon.NextShotTime = now + weapon.ReloadSeconds;
            return true;
        }

        /// <summary>
        /// Every building that can be shot at: it has to have a side to be on and something to lose. A
        /// bridge has neither and is not in here, which is why a crossing cannot be demolished by gunfire.
        /// </summary>
        private NativeList<StructureTarget> CollectStructures(ref SystemState state)
        {
            var structures = new NativeList<StructureTarget>(16, Allocator.TempJob);

            foreach ((RefRO<Faction> faction, RefRO<Health> health,
                      DynamicBuffer<BuildingFootprintCell> footprint, Entity entity)
                     in SystemAPI.Query<RefRO<Faction>, RefRO<Health>,
                                        DynamicBuffer<BuildingFootprintCell>>().WithEntityAccess())
            {
                if (!health.ValueRO.IsAlive || !FootprintBounds.TryOf(footprint, out FootprintBounds bounds))
                {
                    continue;
                }

                structures.Add(new StructureTarget
                {
                    Entity = entity,
                    Bounds = bounds,
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

        /// <summary>Every soldier at once, each writing only its own weapon and a shot into the queue.</summary>
        [BurstCompile]
        private partial struct SoldierFireJob : IJobEntity
        {
            [ReadOnly] public NativeSpatialHash<AgentMove> Crowd;
            [ReadOnly] public ComponentLookup<Faction> Factions;
            [ReadOnly] public ComponentLookup<Health> Health;
            [ReadOnly] public NativeArray<StructureTarget> Structures;

            public NativeQueue<DamageEvent>.ParallelWriter Damage;
            public double Now;

            private void Execute(ref Weapon weapon, in Faction faction, in AgentMove move)
            {
                var targeting = new Targeting
                {
                    Crowd = Crowd,
                    Factions = Factions,
                    Health = Health,
                    Structures = Structures,
                };

                if (TryFire(ref weapon, faction, move.Position, targeting, Now, out DamageEvent shot))
                {
                    Damage.Enqueue(shot);
                }
            }
        }

        /// <summary>The read-only half of a shot: what is there to hit, shared by turrets and soldiers.</summary>
        private struct Targeting
        {
            public NativeSpatialHash<AgentMove> Crowd;
            public ComponentLookup<Faction> Factions;
            public ComponentLookup<Health> Health;
            public NativeArray<StructureTarget> Structures;

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
            public readonly bool TryFindTarget(float2 muzzle, float range, Faction mine, out Entity target)
            {
                var nearest = new NearestEnemy
                {
                    Muzzle = muzzle,
                    BestSq = range * range,
                    Mine = mine,
                    Factions = Factions,
                    Health = Health,
                    Target = Entity.Null,
                };

                Crowd.ForEachInAABB(muzzle - range, muzzle + range, ref nearest);

                // Buildings second, and on the same yardstick, so a soldier standing between a raider and its
                // camp shoots the raider. A structure only wins the tie-break by being strictly nearer.
                //
                // Measured to the nearest edge, not the middle (see FootprintBounds): to the middle, a sword
                // could never reach anything bigger than a turret.
                float best = nearest.BestSq;
                target = nearest.Target;

                foreach (StructureTarget structure in Structures)
                {
                    if (!Faction.AreEnemies(structure.Faction, mine))
                    {
                        continue;
                    }

                    float distanceSq = structure.Bounds.DistanceSq(muzzle);
                    if (distanceSq > best)
                    {
                        continue;
                    }

                    best = distanceSq;
                    target = structure.Entity;
                }

                return target != Entity.Null;
            }
        }

        /// <summary>
        /// Keeps the nearest enemy agent while the hash is walked, rather than collecting every candidate into
        /// a list first and sorting through it afterwards.
        /// </summary>
        private struct NearestEnemy : ISpatialQueryProcessor<AgentMove>
        {
            public float2 Muzzle;
            public float BestSq;
            public Faction Mine;
            public ComponentLookup<Faction> Factions;
            public ComponentLookup<Health> Health;
            public Entity Target;

            public void Process(AgentMove candidate)
            {
                float distanceSq = math.lengthsq(candidate.Position - Muzzle);
                if (distanceSq > BestSq || !IsEnemy(candidate.Entity))
                {
                    return;
                }

                BestSq = distanceSq;
                Target = candidate.Entity;
            }

            /// <summary>
            /// Whether that agent is worth a bullet: on another side, and not already dead this tick. Health is
            /// asked about because the applier writes it immediately and destroys later, so an entity can be at
            /// zero and still be standing in this frame's hash.
            /// </summary>
            private readonly bool IsEnemy(Entity candidate)
            {
                if (!Factions.HasComponent(candidate) || !Faction.AreEnemies(Factions[candidate], Mine))
                {
                    return false;
                }

                return !Health.HasComponent(candidate) || Health[candidate].IsAlive;
            }
        }

        /// <summary>A building flattened to the two facts a shot needs: where it is and whose it is.</summary>
        private struct StructureTarget
        {
            public Entity Entity;
            public FootprintBounds Bounds;
            public Faction Faction;
        }
    }
}
