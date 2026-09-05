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

        [SerializeField, Min(0), Tooltip("Ore seams. Free to walk over, so they route past nothing.")]
        private int _oreSeams = 14;

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

            // Sited against the wood and the ore field below, because a gatherer's whole behaviour is a walk
            // and a building out of range of anything just stands there.
            (BuildingKind.LumberCamp, new int2(6, 14)),
            (BuildingKind.WoodYard, new int2(0, 14)),
            (BuildingKind.Mine, new int2(6, -16)),
            (BuildingKind.OreYard, new int2(0, -16)),

            // Put on bare ground rather than against the wood, because what it does is only visible where
            // there is nothing: a grove appears beside it while you watch.
            (BuildingKind.Planter, new int2(-16, 16)),
        };

        /// <summary>Where the wood stands, and where the seams are. Both near the building that works them.</summary>
        private static readonly int2 ForestCentre = new(14, 16);

        private static readonly int2 OreFieldCentre = new(14, -18);

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
            ScatterOre();
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
        /// **The whole map, not the radius that was asked for.** A grid is rounded up to whole 32-cell chunks,
        /// so asking for 48 cells gets 64 - and painting only the 48 leaves a ring of cost-zero cells right
        /// round the edge. That ring is a road nobody laid and nothing draws: cost zero is 10 to step onto
        /// against the ground's 16, so it is a permanent free lane, and the flow field will happily send an
        /// agent out to the border and along it on the way to somewhere that has nothing to do with the edge
        /// of the map.
        ///
        /// Once only. The patch is a cost delta like every other contribution, so painting it again on every
        /// rebuild would make the world steadily more expensive to walk across until nothing was passable.
        /// </summary>
        private void PaintGround()
        {
            if (_groundPainted || _groundCost <= 0 || !TryGetMap(out GridMap map))
            {
                return;
            }

            _groundPainted = true;

            Entity patch = _entities.CreateEntity(typeof(GridCostPatch));
            _entities.SetComponentData(patch, new GridCostPatch
            {
                MinCell = map.MinCell,
                SizeInCells = map.SizeInCells,
                Cost = _groundCost,
                Flags = CellFlags.None,
                Exits = DirectionUtils.ALL_EXITS,
            });
        }

        private bool TryGetMap(out GridMap map)
        {
            using EntityQuery query = _entities.CreateEntityQuery(ComponentType.ReadOnly<GridWorld>());

            map = query.TryGetSingleton(out GridWorld grid) ? grid.Map : default;
            return map.IsCreated;
        }

        /// <summary>
        /// One wood rather than trees strewn over the whole map.
        ///
        /// Scattering read as noise: single trees everywhere are individually easy to walk round, so nothing
        /// ever routes through one and a lumber camp has no reason to be anywhere in particular. A stand deep
        /// enough to be worth cutting through is what makes both interesting - and it is also the shape that
        /// would have sealed its own middle in, back when a tree was a wall (§14 step 9).
        /// </summary>
        private void ScatterTrees()
        {
            // Fully qualified: UnityEngine.Random is also in scope here, and the one that takes a seed and
            // stays deterministic is the mathematics one.
            var random = new Unity.Mathematics.Random(12345);

            int radius = (int)math.ceil(math.sqrt(_trees));

            for (int i = 0; i < _trees; i++)
            {
                int2 cell = ForestCentre + new int2(
                    random.NextInt(-radius, radius + 1),
                    random.NextInt(-radius, radius + 1)
                );

                if (IsReservedForBuilding(cell) || !InWorld(cell))
                {
                    continue;
                }

                RtsResources.PlaceTree(_entities, cell);
            }
        }

        /// <summary>
        /// Ore, in small clusters rather than scattered evenly, because a seam somebody walks past on the way
        /// to another is what makes a mine's range worth siting well (§14 step 10).
        /// </summary>
        private void ScatterOre()
        {
            var random = new Unity.Mathematics.Random(4242);

            for (int i = 0; i < _oreSeams; i++)
            {
                int2 seed = OreFieldCentre + new int2(random.NextInt(-6, 7), random.NextInt(-5, 6));

                int cluster = random.NextInt(2, 5);
                for (int j = 0; j < cluster; j++)
                {
                    int2 cell = seed + new int2(random.NextInt(-1, 2), random.NextInt(-1, 2));

                    // Ore under a building would be mined out from under it, and a doorstep full of it is a
                    // doorstep agents queue on for two different reasons.
                    if (IsReservedForBuilding(cell) || !InWorld(cell))
                    {
                        continue;
                    }

                    RtsResources.PlaceOre(_entities, cell);
                }
            }
        }

        private bool InWorld(int2 cell) =>
            math.all(cell > -_worldRadius) && math.all(cell < _worldRadius);

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
                Radius = 0.42f,
                CarryCapacity = 10,
                Idle = true,
                Seed = 7,
            });
        }
    }
}
