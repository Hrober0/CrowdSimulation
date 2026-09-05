using FluentAssertions;
using GridNav;
using NUnit.Framework;
using Rts;
using Unity.Entities;
using Unity.Mathematics;

namespace Tests.EditorTests.RtsTests
{
    /// <summary>
    /// Harvestable objects, and what pricing an obstacle instead of forbidding it buys (design §14 step 9).
    ///
    /// The two claims worth holding on to are that a wood can be walked through at all - which is what stops
    /// a planting zone sealing its own planter out - and that felling one does not reach the navigation
    /// graph, which is what makes clearing a forest affordable.
    /// </summary>
    public class ResourceNodeTests
    {
        private const ushort TREE_COST = 60;

        private static readonly int2 Cell = new(4, 4);

        private RtsTestWorld _world;

        [SetUp]
        public void Setup() => _world = new RtsTestWorld();

        [TearDown]
        public void Teardown() => _world.Dispose();

        [Test]
        public void ATreeIsCrossedRatherThanBlocked()
        {
            _world.CreateCellObject(Cell, TREE_COST);
            _world.Tick();

            _world.Map.IsPassable(Cell).Should()
                  .BeTrue("a tree that cannot be walked through can seal a cell behind it");

            NavCost.OfCell(_world.Map.GetCost(Cell)).Should()
                   .BeGreaterThan(NavCost.OfCell(0) * 3,
                                  "and it still has to be dear enough that a short detour wins");
        }

        /// <summary>
        /// A stand of trees around a cell leaves the cell reachable. This is the case that decided the cost:
        /// at <see cref="CellData.BLOCKED"/> the middle of a grown planting zone is walled in, the planter
        /// can neither plant in it nor fell its way back to it, and nothing anywhere reports why.
        /// </summary>
        [Test]
        public void AStandOfTreesDoesNotSealWhatIsInsideIt()
        {
            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    if (x != 0 || y != 0)
                    {
                        _world.CreateCellObject(Cell + new int2(x, y), TREE_COST);
                    }
                }
            }

            _world.Tick();

            for (int d = 0; d < DirectionUtils.DIRECTION_COUNT; d++)
            {
                _world.Map.CanTraverse(Cell, (Direction)d).Should()
                      .BeTrue("a ring of trees is thick going, not a wall");
            }
        }

        /// <summary>
        /// The payoff of the cost being under the threshold, and the reason it must stay there: passability
        /// never changes, so the chunk-gate graph is never re-scanned. Only the cost version moves, and only
        /// flow fields whose window covers the wood rebuild.
        /// </summary>
        [Test]
        public void FellingATreeLeavesTheNavigationGraphAlone()
        {
            Entity tree = _world.CreateCellObject(Cell, TREE_COST);
            _world.Tick();

            int2 chunk = _world.Map.ChunkCoordOf(Cell);
            ChunkVersions before = _world.Map.GetChunkVersions(chunk);

            _world.Entities.DestroyEntity(tree);
            _world.Tick();

            ChunkVersions after = _world.Map.GetChunkVersions(chunk);

            after.PassabilityVersion.Should()
                 .Be(before.PassabilityVersion,
                     "the cell was walkable before and after, so no gate can have changed");

            after.CostVersion.Should()
                 .BeGreaterThan(before.CostVersion, "but the route through it did get cheaper");

            _world.Map.GetCost(Cell).Should().Be(0);
        }

        /// <summary>The contrast, and the reason a rock is still a rock: taking away a wall does move gates.</summary>
        [Test]
        public void ClearingARockDoesMoveTheNavigationGraph()
        {
            Entity rock = _world.CreateCellObject(Cell, CellData.BLOCKED, ObjectKind.Rock);
            _world.Tick();

            int2 chunk = _world.Map.ChunkCoordOf(Cell);
            uint before = _world.Map.GetChunkVersions(chunk).PassabilityVersion;

            _world.Entities.DestroyEntity(rock);
            _world.Tick();

            _world.Map.GetChunkVersions(chunk).PassabilityVersion.Should().BeGreaterThan(before);
        }

        /// <summary>
        /// A zero-cost object is free in the strictest sense - nothing downstream is ever told about it.
        ///
        /// Ore was zero for exactly this reason and is no longer: free also meant agents walked straight
        /// through the seams, and a little reluctance was worth two version bumps in a seam's whole life
        /// (<c>WorldObjectCatalog.ORE_COST</c>). The property is still worth pinning down, because it is what
        /// makes the price of that trade knowable - and anything meant to be walked over freely can still
        /// have it for nothing.
        /// </summary>
        [Test]
        public void AZeroCostObjectNeverDisturbsTheGrid()
        {
            int2 chunk = _world.Map.ChunkCoordOf(Cell);
            ChunkVersions before = _world.Map.GetChunkVersions(chunk);

            Entity seam = _world.CreateResourceNode(Cell, ObjectKind.Ore, new ItemId(6), 40);
            _world.Tick();

            _world.Map.IsPassable(Cell).Should().BeTrue();

            _world.Entities.DestroyEntity(seam);
            _world.Tick();

            ChunkVersions after = _world.Map.GetChunkVersions(chunk);
            after.CostVersion.Should()
                 .Be(before.CostVersion,
                     "a zero cost delta changes no cell, so nothing downstream needs telling - not the gates, "
                     + "and not one flow field");
            after.PassabilityVersion.Should().Be(before.PassabilityVersion);
        }

        /// <summary>
        /// A spent node goes, and its cost goes with it.
        ///
        /// It takes one more grid phase to disappear completely, and that is the cleanup component doing its
        /// job rather than a delay: destroying an entity carrying <c>CellObjectRegistered</c> strips it to
        /// that component and leaves it, which is precisely how the grid learns there is a cost to refund.
        /// </summary>
        [Test]
        public void ASpentNodeIsTakenAwayAndGivesItsCellBack()
        {
            Entity tree = _world.CreateResourceNode(Cell, ObjectKind.Tree, new ItemId(4), 0, TREE_COST);
            _world.TickFrame(0.1f);

            _world.Entities.HasComponent<CellObject>(tree).Should()
                  .BeFalse("there is nothing left in it, so it is no longer a thing standing on the map");

            _world.Tick();

            _world.Entities.Exists(tree).Should().BeFalse();
            _world.Map.GetCost(Cell).Should().Be(0, "and its cost went back the way a demolished building's does");
        }

        /// <summary>
        /// The half that stops a miner walking to a seam that vanishes under him. An empty node with stock
        /// still promised is not spent; it is waiting for whoever was promised it.
        /// </summary>
        [Test]
        public void ANodeStillPromisedToSomebodyStays()
        {
            Entity tree = _world.CreateResourceNode(Cell, ObjectKind.Tree, new ItemId(4), 5, TREE_COST);

            DynamicBuffer<StorageSlot> slots = _world.Entities.GetBuffer<StorageSlot>(tree);
            StorageSlot slot = slots[0];
            slot.Amount = 0;
            slot.ReservedOut = 5;
            slots[0] = slot;

            _world.TickFrame(0.1f);

            _world.Entities.HasComponent<CellObject>(tree).Should()
                  .BeTrue("somebody is still walking here for the last of it");

            slots = _world.Entities.GetBuffer<StorageSlot>(tree);
            slot = slots[0];
            slot.ReservedOut = 0;
            slots[0] = slot;

            _world.TickFrame(0.1f);
            _world.Tick();

            _world.Entities.Exists(tree).Should().BeFalse("once nobody is coming for it, it goes");
        }

        /// <summary>
        /// Passability used to answer "is this ground clear", and stopped being able to the moment a tree
        /// could be walked through: a wood is now passable, unflagged ground that a building would happily be
        /// dropped on top of, burying an object nothing can reach again. <see cref="CellFlags.Object"/> is
        /// what the placement rule reads instead.
        /// </summary>
        [Test]
        public void GroundWithSomethingStandingOnItIsNotClear()
        {
            _world.CreateCellObject(Cell, TREE_COST);
            _world.Tick();

            _world.Map.IsPassable(Cell).Should().BeTrue("it is still walkable");
            _world.Map.GetFlags(Cell).Should()
                  .Be(CellFlags.Object, "but it is not clear, and building on it would bury the tree");
        }

        /// <summary>
        /// The flag is a count, not a sum. Two trees on one cell and the cell is clear only when both have
        /// gone - if the first to be felled lowered the flag, a building would be dropped on the second.
        /// </summary>
        [Test]
        public void GroundIsClearOnlyOnceTheLastThingOnItHasGone()
        {
            Entity first = _world.CreateCellObject(Cell, TREE_COST);
            Entity second = _world.CreateCellObject(Cell, TREE_COST);
            _world.Tick();

            _world.Entities.DestroyEntity(first);
            _world.Tick();

            _world.Map.GetFlags(Cell).Should().Be(CellFlags.Object, "one is still standing there");

            _world.Entities.DestroyEntity(second);
            _world.Tick();

            _world.Map.GetFlags(Cell).Should().Be(CellFlags.None);
            _world.Map.GetCost(Cell).Should().Be(0);
        }

        /// <summary>
        /// Nodes are not part of the general market. A warehouse that wants wood is not served by walking to
        /// the nearest tree - that is a forestry building's job, within the range it works (§14 step 10) - and
        /// without the exclusion every hauler in the game becomes a lumberjack.
        /// </summary>
        [Test]
        public void AWarehouseIsNotSuppliedStraightFromTheForest()
        {
            var wood = new ItemId(4);

            _world.CreateResourceNode(Cell, ObjectKind.Tree, wood, 20, TREE_COST);
            Entity warehouse = _world.CreateWarehouse(new int2(12, 12), wood);

            // A hauler standing by, so the test cannot pass merely because there was nobody to send.
            _world.CreateIdleAgent(new float2(6f, 6f));

            _world.TickFrames(5);

            _world.Entities.GetBuffer<StorageSlot>(warehouse)[0].ReservedIn.Should()
                  .Be(0, "no hauler can have been sent to a tree");
        }
    }
}
