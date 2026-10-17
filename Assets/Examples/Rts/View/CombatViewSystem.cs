using GridNav;
using Rts;
using Unity.Entities;
using Unity.Mathematics;

namespace Examples.Rts
{
    /// <summary>
    /// Feeds <see cref="CombatView"/> from the simulation: every armed agent's position and target, and every
    /// shot fired since the last frame (design §10). Reads only - like <see cref="ViewSyncSystem"/>, nothing
    /// here writes simulation state.
    ///
    /// The view belongs to the scene; <see cref="CombatViewSettings"/> binds one while it is enabled, and with
    /// none bound this does nothing.
    /// </summary>
    [UpdateInGroup(typeof(RtsViewGroup))]
    [UpdateAfter(typeof(ViewSyncSystem))]
    public partial class CombatViewSystem : SystemBase
    {
        private CombatView _view;
        private ComponentLookup<AgentMove> _moves;
        private BufferLookup<BuildingFootprintCell> _footprints;

        /// <summary>
        /// Weapons that reach no further than this, in cells, are drawn as swords; anything longer is a gun.
        /// A presentation rule rather than a simulation one: `Rts` knows only a range.
        /// </summary>
        public float SwordReach { get; set; } = 2f;

        /// <summary>
        /// How far past its range a soldier keeps facing its last target, in cells. Beyond it the target is
        /// somebody else's problem and the soldier faces where it is walking again.
        /// </summary>
        public float AimSlack { get; set; } = 2f;

        public ViewCulling Culling { get; set; }

        public void Bind(CombatView view) => _view = view;

        /// <summary>Guarded on identity, for the same scene-reload ordering <see cref="ViewSyncSystem"/> explains.</summary>
        public void Unbind(CombatView view)
        {
            if (_view == view)
            {
                _view = null;
            }
        }

        protected override void OnCreate()
        {
            _moves = GetComponentLookup<AgentMove>(isReadOnly: true);
            _footprints = GetBufferLookup<BuildingFootprintCell>(isReadOnly: true);
        }

        protected override void OnUpdate()
        {
            if (_view == null)
            {
                return;
            }

            _moves.Update(this);
            _footprints.Update(this);

            double now = SystemAPI.Time.ElapsedTime;
            ViewCulling culling = Culling;

            _view.BeginFrame();

            // The query takes enabled weapons and enabled moves only: a worker is not drawn armed, and a soldier
            // inside a building is not drawn at all - the same two bits the attack system goes by.
            foreach ((RefRO<Weapon> weapon, RefRO<Faction> faction, RefRO<AgentMove> move, Entity entity)
                     in SystemAPI.Query<RefRO<Weapon>, RefRO<Faction>, RefRO<AgentMove>>()
                                 .WithAll<ViewVisible>()
                                 .WithEntityAccess())
            {
                AgentMove body = move.ValueRO;
                bool shot = _view.NoteShot(entity, weapon.ValueRO.NextShotTime);

                if (!culling.IsVisible(body.Position))
                {
                    continue;
                }

                bool hasTarget = TryPositionOf(weapon.ValueRO.LastTarget, out float2 target);
                bool aiming = hasTarget
                              && math.distance(target, body.Position) <= weapon.ValueRO.Range + AimSlack;

                _view.Show(entity, new SoldierFrame
                {
                    Position = body.Position,
                    Velocity = body.Velocity,
                    MaxSpeed = body.MaxSpeed,
                    Faction = faction.ValueRO.Id,
                    Kind = weapon.ValueRO.Range <= SwordReach ? CombatWeaponKind.Sword : CombatWeaponKind.Gun,
                    HasAim = aiming,
                    Aim = target,
                });

                if (shot && hasTarget)
                {
                    _view.Fire(entity, body.Position, target, now);
                }
            }

            // Turrets: no body to draw, but their rounds fly like anyone else's.
            foreach ((RefRO<Weapon> weapon, DynamicBuffer<BuildingFootprintCell> footprint, Entity entity)
                     in SystemAPI.Query<RefRO<Weapon>, DynamicBuffer<BuildingFootprintCell>>().WithEntityAccess())
            {
                if (_view.NoteShot(entity, weapon.ValueRO.NextShotTime)
                    && !footprint.IsEmpty
                    && TryPositionOf(weapon.ValueRO.LastTarget, out float2 target))
                {
                    _view.Fire(entity, CentreOf(footprint), target, now);
                }
            }

            _view.EndFrame(now, SystemAPI.Time.DeltaTime);
        }

        /// <summary>Where a target stands: a body's position, or the middle of a building.</summary>
        private bool TryPositionOf(Entity target, out float2 position)
        {
            if (target != Entity.Null && _moves.HasComponent(target))
            {
                position = _moves[target].Position;
                return true;
            }

            if (target != Entity.Null && _footprints.HasBuffer(target) && !_footprints[target].IsEmpty)
            {
                position = CentreOf(_footprints[target]);
                return true;
            }

            position = default;
            return false;
        }

        private static float2 CentreOf(in DynamicBuffer<BuildingFootprintCell> footprint)
        {
            float2 sum = float2.zero;
            foreach (BuildingFootprintCell cell in footprint)
            {
                sum += GridCoords.CellCenter(cell.Cell);
            }

            return sum / footprint.Length;
        }
    }
}
