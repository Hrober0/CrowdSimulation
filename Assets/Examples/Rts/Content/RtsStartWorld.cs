using GridNav;
using Rts;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Examples.Rts
{
    /// <summary>
    /// Lays out a working economy on Play, so the scene is something to watch rather than something to set
    /// up (design §14.1). F5 lays it out again.
    ///
    /// This is also the answer to "save and load". A saved world would mean serialising chunked native grid
    /// memory and every component of every entity, for a sandbox whose whole point is that any situation can
    /// be rebuilt in seconds. Rebuilding *is* the load, and it is the same code path the first frame uses.
    ///
    /// The chain is Farm -> Mill -> Bakery -> Warehouse with a hut for the haulers: two crafting stages and
    /// three hauls, which between them exercise strict priority, reservations, claim-before-approach and the
    /// concurrency cap.
    /// </summary>
    public class RtsStartWorld : MonoBehaviour
    {
        /// <summary>
        /// Frames to wait between clearing and re-laying. The grid is written once per frame by its single
        /// writer (§13.2), so the demolished cells have to be given back *before* the replacements are
        /// queued - otherwise a building placed where one just stood cancels its own refund out and leaves
        /// the cell walkable and unflagged.
        /// </summary>
        private const int FRAMES_BETWEEN_CLEAR_AND_LAY = 1;

        [SerializeField, Tooltip("Lay the world out when the scene starts.")]
        private bool _buildOnStart = true;

        [SerializeField, Min(0)] private int _haulers = 12;

        [SerializeField, Tooltip("Ground cost, so roads have something to be cheaper than.")]
        private int _groundCost = RoadBrush.ROAD_DISCOUNT;

        [SerializeField, Min(0), Tooltip("Trees, to give the pathfinder something to route past.")]
        private int _trees = 60;

        [SerializeField, Min(4)] private int _worldRadius = 24;

        /// <summary>A property rather than a static field, so there is no shared mutable state anywhere.</summary>
        private static (BuildingKind Kind, int2 Origin)[] Layout => new[]
        {
            (BuildingKind.Farm, new int2(-16, 6)),
            (BuildingKind.Farm, new int2(-16, -8)),
            (BuildingKind.Mill, new int2(-4, 6)),
            (BuildingKind.Bakery, new int2(8, 6)),
            (BuildingKind.Warehouse, new int2(16, -6)),
            (BuildingKind.Hut, new int2(0, -10)),
        };

        private EntityManager _entities;
        private bool _ready;
        private bool _groundPainted;
        private int _layCountdown = -1;

        /// <summary>
        /// Brings the grid into existence. The subscene path bakes <see cref="GridSettings"/> from
        /// <see cref="GridAuthoring"/>; a plain scene has to say so itself, and doing it in Awake means the
        /// singleton is there before the grid systems first run.
        /// </summary>
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

            if (_buildOnStart)
            {
                Rebuild();
            }
        }

        /// <summary>Clears whatever is there and lays the world out again, a frame later.</summary>
        public void Rebuild()
        {
            if (!_ready)
            {
                return;
            }

            Clear();
            _layCountdown = FRAMES_BETWEEN_CLEAR_AND_LAY;
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
            PaintGround();

            foreach ((BuildingKind kind, int2 origin) in Layout)
            {
                RtsConstruction.Place(_entities, BuildingCatalog.Of(kind), origin);
            }

            ScatterTrees();
            SpawnHaulers();
        }

        private void Clear()
        {
            // Buildings and world objects give their cells back through their cleanup buffers on the next
            // grid phase, so destroying the entities is all that is needed - the grid repairs itself.
            DestroyAll(ComponentType.ReadOnly<BuildingPlacement>());
            DestroyAll(ComponentType.ReadOnly<CellObject>());
            DestroyAll(ComponentType.ReadOnly<AgentMove>());
            DestroyAll(ComponentType.ReadOnly<AgentSpawn>());
        }

        private void DestroyAll(ComponentType type)
        {
            using EntityQuery query = _entities.CreateEntityQuery(type);
            _entities.DestroyEntity(query);
        }

        /// <summary>
        /// Ground is laid down deliberately expensive, because a road is a *discount* (§3) and a discount off
        /// zero changes no routing at all.
        ///
        /// Once only. The patch is a cost delta like every other contribution, so painting it again on every
        /// rebuild would make the world steadily more expensive to walk across until nothing was passable.
        /// </summary>
        private void PaintGround()
        {
            if (_groundPainted || _groundCost <= 0)
            {
                return;
            }

            _groundPainted = true;

            Entity patch = _entities.CreateEntity(typeof(GridCostPatch));
            _entities.SetComponentData(patch, new GridCostPatch
            {
                MinCell = new int2(-_worldRadius, -_worldRadius),
                SizeInCells = new int2(_worldRadius * 2, _worldRadius * 2),
                Cost = _groundCost,
                Flags = CellFlags.None,
                Exits = DirectionUtils.ALL_EXITS,
            });
        }

        private void ScatterTrees()
        {
            // Fully qualified: UnityEngine.Random is also in scope here, and the one that takes a seed and
            // stays deterministic is the mathematics one.
            var random = new Unity.Mathematics.Random(12345);

            for (int i = 0; i < _trees; i++)
            {
                var cell = new int2(
                    random.NextInt(-_worldRadius, _worldRadius),
                    random.NextInt(-_worldRadius, _worldRadius)
                );

                if (IsReservedForBuilding(cell))
                {
                    continue;
                }

                Entity tree = _entities.CreateEntity(typeof(CellObject));
                _entities.SetComponentData(tree, new CellObject
                {
                    Cell = cell,
                    Cost = CellData.BLOCKED,
                    Kind = ObjectKind.Tree,
                });
            }
        }

        /// <summary>
        /// Keeps trees off the buildings and their doorsteps. Checked against the layout rather than against
        /// the grid, because the buildings queued a moment ago are not written until the next frame.
        /// </summary>
        private static bool IsReservedForBuilding(int2 cell)
        {
            foreach ((BuildingKind kind, int2 origin) in Layout)
            {
                int2 size = BuildingCatalog.Of(kind).Size;
                if (math.all(cell >= origin - 2) && math.all(cell <= origin + size + 1))
                {
                    return true;
                }
            }

            return false;
        }

        private void SpawnHaulers()
        {
            if (_haulers <= 0)
            {
                return;
            }

            Entity request = _entities.CreateEntity(typeof(AgentSpawn));
            _entities.SetComponentData(request, new AgentSpawn
            {
                Count = _haulers,
                Center = new float2(0f, -4f),
                Size = new float2(12f, 6f),
                MaxSpeed = 3f,
                Radius = 0.35f,
                CarryCapacity = 10,
                Idle = true,
                Seed = 7,
            });
        }
    }
}
