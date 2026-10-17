using GridNav;
using Rts;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Examples.Rts
{
    /// <summary>
    /// Two armies on an open field, for watching a fight and nothing else. F5 lines them up again.
    ///
    /// The sibling of <see cref="RtsStartWorld"/> with the economy left out: no buildings, no haulers, just a
    /// line of swords in front of a line of guns on each side. Both are ordinary armed agents - melee is a
    /// <see cref="Weapon"/> whose range is about a body's width, and there is nothing else to a sword (§14 step
    /// 13). The leash is long enough to reach across the field, so every soldier can see every enemy from its
    /// post and the threat market sends them at each other on the first economy tick.
    /// </summary>
    public class RtsBattleWorld : MonoBehaviour
    {
        [SerializeField, Min(4)] private int _worldRadius = 24;

        [SerializeField, Min(0)] private int _swordsPerSide = 10;

        [SerializeField, Min(0)] private int _gunsPerSide = 8;

        [SerializeField, Tooltip("Cells between the two front lines.")]
        private float _gap = 14f;

        [SerializeField, Tooltip("Cells from each side's swords back to its guns.")]
        private float _depth = 6f;

        [SerializeField, Min(1f), Tooltip("How many cells deep each rank stands. Its height follows from the count.")]
        private float _rankDepth = 2f;

        [SerializeField, Min(0.5f)]
        [Tooltip("Ground each soldier gets in its rank, in cells. Below about one, they start out shoulder to "
                 + "shoulder and avoidance has to push them apart before anything else happens.")]
        private float _cellsPerSoldier = 1.8f;

        [SerializeField, Min(0), Tooltip("Two small woods flanking the middle, so a charge has something to route round.")]
        private int _treesPerWood = 6;

        [SerializeField, Tooltip("Reach about a body's width: it has to walk up to what it hits.")]
        private SoldierType _sword = new()
        {
            Range = 1.4f,
            Damage = 14,
            ReloadSeconds = 0.8f,
            MaxHealth = 220,
            MaxSpeed = 3.2f,
        };

        [SerializeField, Tooltip("Outranges a sword by a long way and dies to one quickly.")]
        private SoldierType _gun = new()
        {
            Range = 8f,
            Damage = 8,
            ReloadSeconds = 1.1f,
            MaxHealth = 90,
            MaxSpeed = 2.6f,
        };

        private EntityManager _entities;
        private bool _ready;
        private int _layCountdown = -1;

        /// <summary>The grid has to exist before its systems first run. See <see cref="RtsStartWorld"/>.</summary>
        private void Awake()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world is not { IsCreated: true })
            {
                return;
            }

            using EntityQuery existing = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<GridSettings>());

            if (existing.IsEmpty)
            {
                world.EntityManager.CreateSingleton(
                    GridSettings.FromCells(new int2(_worldRadius * 2, _worldRadius * 2), centerOnOrigin: true)
                );
            }
        }

        private void Start()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            _ready = world is { IsCreated: true };
            if (!_ready)
            {
                return;
            }

            _entities = world.EntityManager;
            Rebuild();
        }

        /// <summary>
        /// Clears the field and lines both armies up again a frame later, for the reason
        /// <see cref="RtsStartWorld"/> gives: the woods' cells have to be handed back before new ones are laid.
        /// </summary>
        public void Rebuild()
        {
            if (!_ready)
            {
                return;
            }

            DestroyAll(ComponentType.ReadOnly<CellObject>());
            DestroyAll(ComponentType.ReadOnly<AgentMove>());
            DestroyAll(ComponentType.ReadOnly<AgentSpawn>());
            _layCountdown = 1;
        }

        private void Update()
        {
            if (!_ready)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.F5))
            {
                Rebuild();
                return;
            }

            if (_layCountdown < 0)
            {
                return;
            }

            if (_layCountdown > 0)
            {
                _layCountdown--;
                return;
            }

            _layCountdown = -1;
            Lay();
        }

        private void Lay()
        {
            PlantWood(new int2(0, 8));
            PlantWood(new int2(0, -9));

            float front = _gap * 0.5f;
            float back = front + _depth;

            // Leash from a post on one side's back line to the other side's back line, with room to spare.
            float leash = back * 2f + 4f;

            // The same seed for both sides' rank of a kind, so the two armies stand in the same shape. Between
            // evenly matched armies a slightly better formation is an early lead, and an early lead snowballs:
            // with a seed each, one side won the big battle by a hundred and thirty to nothing every time.
            Spawn(RtsFactions.PLAYER, _sword, _swordsPerSide, -front, leash, seed: 11);
            Spawn(RtsFactions.PLAYER, _gun, _gunsPerSide, -back, leash, seed: 12);
            Spawn(RtsFactions.RAIDERS, _sword, _swordsPerSide, front, leash, seed: 11);
            Spawn(RtsFactions.RAIDERS, _gun, _gunsPerSide, back, leash, seed: 12);
        }

        /// <summary>One rank: a block standing at <paramref name="x"/>, spread up and down the field.</summary>
        private void Spawn(byte faction, in SoldierType type, int count, float x, float leash, uint seed)
        {
            if (count <= 0)
            {
                return;
            }

            Entity request = _entities.CreateEntity(typeof(AgentSpawn));
            _entities.SetComponentData(request, new AgentSpawn
            {
                Count = count,
                Center = new float2(x, 0f),
                Size = new float2(_rankDepth, math.max(count * _cellsPerSoldier / _rankDepth, 1f)),
                MaxSpeed = type.MaxSpeed,
                Radius = 0.42f,
                MaxHealth = type.MaxHealth,
                Faction = faction,
                Weapon = new Weapon
                {
                    Range = type.Range,
                    Damage = type.Damage,
                    ReloadSeconds = type.ReloadSeconds,
                },
                Leash = leash,
                Idle = true,
                Seed = seed,
            });
        }

        private void PlantWood(int2 centre)
        {
            var random = new Unity.Mathematics.Random((uint)(1000 + centre.y * 7 + 3));

            for (int i = 0; i < _treesPerWood; i++)
            {
                int2 cell = centre + new int2(random.NextInt(-2, 3), random.NextInt(-1, 2));
                if (math.all(cell > -_worldRadius) && math.all(cell < _worldRadius))
                {
                    RtsResources.PlaceTree(_entities, cell);
                }
            }
        }

        private void DestroyAll(ComponentType type)
        {
            using EntityQuery query = _entities.CreateEntityQuery(type);
            _entities.DestroyEntity(query);
        }

        /// <summary>
        /// One kind of soldier: its weapon and its body. The weapon's numbers are copied out rather than the
        /// <see cref="Weapon"/> itself held here, because that is a component with runtime state on it and is
        /// not something the inspector can serialise.
        /// </summary>
        [System.Serializable]
        private struct SoldierType
        {
            [Min(0f)] public float Range;
            [Min(0)] public int Damage;
            [Min(0f)] public float ReloadSeconds;
            [Min(1)] public int MaxHealth;
            [Min(0.1f)] public float MaxSpeed;
        }
    }
}
