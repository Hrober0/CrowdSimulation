using FluentAssertions;
using GridNav;
using NUnit.Framework;
using Unity.Mathematics;

namespace Tests.EditorTests.GridNavTests
{
    /// <summary>
    /// What a wall costs the thing looking at it (design §14 step 13, amended).
    ///
    /// The model is one function and three rules: cost is health over damage, priced as that many cells of
    /// walking; a seeker that does no damage cannot pass at all; and neither can one looking at its own
    /// side's wall. The civilian case is not a special case - it is the damage-zero instance of the same
    /// arithmetic, which is why there is one grid here and not two.
    /// </summary>
    public class BreachCostTests
    {
        private static readonly int2 Wall = new(4, 4);

        private GridNavTestWorld _world;

        private GridMap Map => _world.Map;

        [SetUp]
        public void Setup() => _world = new GridNavTestWorld(32);

        [TearDown]
        public void Teardown() => _world.Dispose();

        /// <summary>Puts a wall down exactly as <c>BuildingFootprintSystem</c> does: blocked, and recorded.</summary>
        private void Build(int2 cell, byte owner, ushort health)
        {
            _world.Enqueue(GridEdit.CostDelta(cell, GridMap.STRUCTURE_BLOCK));
            _world.Enqueue(GridEdit.AddFlags(cell, CellFlags.Building));
            _world.Enqueue(GridEdit.SetStructure(cell, owner, health));
            _world.Tick();
        }

        private void Edit(GridEdit edit)
        {
            _world.Enqueue(edit);
            _world.Tick();
        }

        private static Traversal Attacker(BreachClass breach = BreachClass.High, byte faction = 1) =>
            new(breach, faction);

        [Test]
        public void AWallIsAWallToAnyoneWhoCannotBreakIt()
        {
            Build(Wall, owner: 0, health: 100);

            Map.IsPassable(Wall, Traversal.Civilian).Should().BeFalse();
            Map.IsPassable(Wall, new Traversal(BreachClass.None, 1)).Should()
                .BeFalse("a faction that does no damage is a civilian, whoever it is");
        }

        [Test]
        public void AnAttackerMayPriceItAsABreach()
        {
            Build(Wall, owner: 0, health: 100);

            Map.IsPassable(Wall, Attacker()).Should().BeTrue();
        }

        /// <summary>
        /// The rule that kills the two-field design: an army never routes through its own bakery, and it
        /// needs no second field to know that.
        /// </summary>
        [Test]
        public void NobodyRoutesThroughTheirOwnWall()
        {
            Build(Wall, owner: 1, health: 100);

            Map.IsPassable(Wall, Attacker(faction: 1)).Should().BeFalse("it is ours");
            Map.IsPassable(Wall, Attacker(faction: 2)).Should().BeTrue("and theirs to break");
        }

        /// <summary>Health over damage, in the same currency as walking: ten shots is ten cells of detour.</summary>
        [Test]
        public void TheBreachPriceIsShotsTimesAStep()
        {
            Build(Wall, owner: 0, health: 100);

            Map.GetCost(Wall, Attacker(BreachClass.Low)).Should()
                .Be((ushort)(10 * NavCost.STEP), "ten shots at ten damage");

            Map.GetCost(Wall, Attacker(BreachClass.High)).Should()
                .Be((ushort)(2 * NavCost.STEP), "two shots at fifty");
        }

        [Test]
        public void ABatteredWallIsCheaperToComeThrough()
        {
            Build(Wall, owner: 0, health: 200);
            ushort fresh = Map.GetCost(Wall, Attacker());

            Edit(GridEdit.SetStructure(Wall, 0, 50));

            Map.GetCost(Wall, Attacker()).Should()
                .BeLessThan(fresh, "a route through it gets cheaper as it burns");
        }

        /// <summary>
        /// Costs live under <see cref="CellData.BLOCKED"/>, so a wall past about twenty-five cells of detour
        /// cannot be expressed at all. Saturating to "go round" is the honest reading of that.
        /// </summary>
        [Test]
        public void AWallTooThickToBeWorthItStaysAWall()
        {
            Build(Wall, owner: 0, health: 5000);

            Map.IsPassable(Wall, Attacker(BreachClass.Low)).Should().BeFalse();
        }

        /// <summary>What is underneath is still paid for: only the wall comes out of the sum.</summary>
        [Test]
        public void TheGroundUnderTheWallIsStillPaidFor()
        {
            Edit(GridEdit.CostDelta(Wall, 16));
            Build(Wall, owner: 0, health: 100);

            Map.GetCost(Wall, Attacker(BreachClass.High)).Should().Be((ushort)(16 + 2 * NavCost.STEP));
        }

        [Test]
        public void ClearingAStructureLeavesTheCellAsItWas()
        {
            Build(Wall, owner: 0, health: 100);

            Edit(GridEdit.CostDelta(Wall, -GridMap.STRUCTURE_BLOCK));
            Edit(GridEdit.ClearStructure(Wall));

            Map.IsPassable(Wall, Traversal.Civilian).Should().BeTrue();
            Map.GetCost(Wall, Attacker()).Should().Be((ushort)0);
        }

        /// <summary>
        /// The counter that makes a siege affordable: a structure changing hands or health must not tell the
        /// bread economy's fields to rebuild.
        /// </summary>
        [Test]
        public void DamagingAWallDoesNotDirtyTheCivilianCost()
        {
            Build(Wall, owner: 0, health: 200);

            ChunkVersions before = Map.GetChunkVersionsAtCell(Wall);

            Edit(GridEdit.SetStructure(Wall, 0, 50));

            ChunkVersions after = Map.GetChunkVersionsAtCell(Wall);

            after.StructureVersion.Should().BeGreaterThan(before.StructureVersion);
            after.CostVersion.Should().Be(before.CostVersion, "a wall is a wall to a hauler at any health");
            after.PassabilityVersion.Should().Be(before.PassabilityVersion);
        }
    }
}
