using FluentAssertions;
using GridNav;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace Tests.EditorTests.GridNavTests
{
    /// <summary>
    /// The long-range half of breach routing (design §14.4).
    ///
    /// A gate is an opening between two chunks, so whether one exists is a passability question - and a
    /// seeker able to knock a wall down answers it differently. That is why there is a gate graph per
    /// traversal rather than one graph.
    ///
    /// The map here is bigger than a flow field window on purpose. Below that size the field covers every
    /// walk and the gate tier never runs, which is exactly why this gap could sit unnoticed while every other
    /// test passed.
    /// </summary>
    public class BreachGateGraphTests
    {
        /// <summary>Ten chunks square: comfortably wider than the 128-cell field window.</summary>
        private const int MAP_CELLS = 320;

        /// <summary>On a chunk boundary, so the wall is exactly what decides whether a gate exists.</summary>
        private const int WallX = 0;

        private static readonly int2 Gap = new(WallX, 0);

        private static readonly int2 ThisSide = new(-100, 0);

        private static readonly int2 FarSide = new(100, 0);

        private static readonly Traversal Breacher = new(BreachClass.High, 1);

        private GridNavTestWorld _world;

        [SetUp]
        public void Setup()
        {
            _world = new GridNavTestWorld(MAP_CELLS);

            int half = MAP_CELLS / 2;
            for (int y = -half; y < half; y++)
            {
                if (y != Gap.y)
                {
                    _world.Enqueue(GridEdit.CostDelta(new int2(WallX, y), CellData.BLOCKED));
                }
            }

            _world.Tick();
        }

        [TearDown]
        public void Teardown() => _world.Dispose();

        private void PlugTheGap(byte owner = 0, ushort health = 100)
        {
            _world.Enqueue(GridEdit.CostDelta(Gap, GridMap.STRUCTURE_BLOCK));
            _world.Enqueue(GridEdit.SetStructure(Gap, owner, health));
            _world.Tick();
        }

        private bool HasGatePath(Traversal traversal)
        {
            _world.RequestGraph(traversal);
            _world.Tick();
            _world.Tick();

            ChunkGateGraph graph = _world.GraphFor(traversal);
            graph.IsCreated.Should().BeTrue("the graph was asked for two ticks ago");

            var gates = new NativeList<int>(16, Allocator.Temp);
            bool found = GatePathFinder.TryFindGatePath(_world.Map, graph, ThisSide, FarSide, gates);
            gates.Dispose();

            return found;
        }

        [Test]
        public void TheCivilianGraphIsBuiltWithoutBeingAskedFor()
        {
            _world.Graph.IsCreated.Should().BeTrue("nearly everything walks on it");
            _world.Graph.Traversal.CanBreach.Should().BeFalse();
        }

        [Test]
        public void AnUnpluggedGapIsAGateForEveryone()
        {
            HasGatePath(Traversal.Civilian).Should().BeTrue();
            HasGatePath(Breacher).Should().BeTrue();
        }

        /// <summary>
        /// The whole point. With the only gap plugged, the wall closes the chunk border for a civilian and
        /// stays open for something that can knock the plug down - so the two find different long-range
        /// routes across the same map.
        /// </summary>
        [Test]
        public void APluggedGapClosesTheBorderOnlyForThoseWhoCannotBreachIt()
        {
            PlugTheGap();

            HasGatePath(Traversal.Civilian).Should().BeFalse("there is no way through and none round");
            HasGatePath(Breacher).Should().BeTrue("the plug is a gate to anything that can break it");
        }

        [Test]
        public void NobodyGatesThroughTheirOwnWall()
        {
            PlugTheGap(owner: Breacher.Faction);

            HasGatePath(Breacher).Should().BeFalse("it is ours to walk round, not through");
        }

        /// <summary>
        /// A wall coming down has to open the gate. It is the structure counter that carries that news, and
        /// only to the graphs that can act on it - which is the same counter that keeps a siege from
        /// rebuilding every civilian graph on the map (§3.1).
        /// </summary>
        [Test]
        public void KnockingThePlugDownOpensTheGate()
        {
            PlugTheGap(health: 3000);

            HasGatePath(Breacher).Should().BeFalse("three thousand health is further than a breach prices");

            _world.Enqueue(GridEdit.SetStructure(Gap, 0, 100));
            _world.Tick();

            HasGatePath(Breacher).Should().BeTrue("battered down to a hundred, it is worth two shots");
        }
    }
}
