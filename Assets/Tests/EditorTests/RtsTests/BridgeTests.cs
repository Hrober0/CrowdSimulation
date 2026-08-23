using GridNav;
using NUnit.Framework;
using Rts;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Tests.EditorTests.RtsTests
{
    /// <summary>
    /// A bridge is the first thing on the map that agents go *through* rather than *to*, so most of what these
    /// tests are checking is that the routing layer knows it exists. A crossing nobody routes over is a
    /// building with an animation.
    ///
    /// The map is split by a wall of blocked cells with the bridge as the only way across, which makes every
    /// assertion about routing unambiguous: if an agent reaches the far bank, it used the bridge, because there
    /// is nothing else it could have used.
    /// </summary>
    internal sealed class BridgeTests
    {
        /// <summary>Where the wall runs. Everything else is placed relative to it.</summary>
        private const int WALL_X = 8;

        private const int LANE_Y = 4;

        [Test]
        public void ABridgeIsTwoPiersAndAnOpenGap()
        {
            using var world = new RtsTestWorld();

            // Mouths four apart: piers at 3 and 5, and the cell between them left alone.
            world.CreateBridge(new int2(2, 2), new int2(6, 2));
            world.Tick();

            Assert.IsTrue(world.Map.IsPassable(new int2(2, 2)), "the near mouth is where an agent stands");
            Assert.IsTrue(world.Map.IsPassable(new int2(6, 2)), "the far mouth is where it is put down");

            Assert.IsFalse(world.Map.IsPassable(new int2(3, 2)), "the near pier is solid");
            Assert.IsFalse(world.Map.IsPassable(new int2(5, 2)), "so is the far one");
            Assert.IsTrue(world.Map.GetFlags(new int2(3, 2)).HasFlag(CellFlags.Building));
            Assert.IsTrue(world.Map.GetFlags(new int2(5, 2)).HasFlag(CellFlags.Building));

            // The whole point of the shape: the deck is overhead, so the ground under it is untouched.
            Assert.IsTrue(world.Map.IsPassable(new int2(4, 2)), "the ground under the deck stays walkable");
            Assert.AreEqual(CellFlags.None, world.Map.GetFlags(new int2(4, 2)),
                "and the bridge does not even mark it");

            Assert.IsTrue(world.Map.GetFlags(new int2(2, 2)).HasFlag(CellFlags.LinkEntry));
            Assert.IsTrue(world.Map.GetFlags(new int2(6, 2)).HasFlag(CellFlags.LinkExit));

            // NoIdle on both mouths, for the reason a doorstep carries it: a mouth agents may loiter on is a
            // mouth that gets blocked, and this one has a queue behind it.
            Assert.IsTrue(world.Map.GetFlags(new int2(2, 2)).HasFlag(CellFlags.NoIdle));
            Assert.IsTrue(world.Map.GetFlags(new int2(6, 2)).HasFlag(CellFlags.NoIdle));
        }

        [Test]
        public void AShortOrCrookedSpanIsNotABridge()
        {
            // Two apart leaves no room for a pier at each end with the mouths outside them, and a diagonal is
            // not a line. Both have to be refused here rather than half-built and noticed later.
            Assert.IsFalse(Bridge.TryShape(new int2(0, 0), new int2(2, 0), out _), "two apart is too short");
            Assert.IsFalse(Bridge.TryShape(new int2(0, 0), new int2(3, 3), out _), "and a diagonal is not a line");

            Assert.IsTrue(Bridge.TryShape(new int2(0, 0), new int2(3, 0), out BridgeShape shortest));
            Assert.AreEqual(new int2(1, 0), shortest.NearPier);
            Assert.AreEqual(new int2(2, 0), shortest.FarPier);
            Assert.AreEqual(0, shortest.GapCells, "the shortest bridge is two piers and nothing between them");

            Assert.IsTrue(Bridge.TryShape(new int2(0, 0), new int2(5, 0), out BridgeShape longer));
            Assert.AreEqual(2, longer.GapCells, "each extra cell is one more cell of ground under the deck");
            Assert.AreEqual(new int2(2, 0), longer.GapCell(0));
        }

        [Test]
        public void ABridgeTooShortToStandUpIsRefusedAndLeavesTheMapAlone()
        {
            using var world = new RtsTestWorld();

            // The refusal also logs a warning, which is deliberately not asserted on: it is emitted from a
            // Burst-compiled system, and whether it reaches the managed log depends on how Burst is configured
            // for the run. What must hold either way is that nothing was taken.
            Entity bridge = world.CreateBridge(new int2(2, 2), new int2(4, 2));
            world.Tick();

            Assert.IsFalse(world.Entities.HasComponent<Bridge>(bridge), "it should not have become a bridge");
            Assert.IsTrue(world.Map.IsPassable(new int2(3, 2)), "and it should not have taken any cells");
            Assert.AreEqual(CellFlags.None, world.Map.GetFlags(new int2(2, 2)), "or flagged a mouth");
        }

        [Test]
        public void RotatingABridgeTurnsTheWholeThing()
        {
            using var world = new RtsTestWorld();

            // Authored running east; a quarter turn clockwise sends it south.
            world.CreateBridgeBlueprint(new int2(4, 8), structureCells: 3, GridRotation.Clockwise90);
            world.Tick();

            Assert.IsTrue(world.Map.GetFlags(new int2(4, 9)).HasFlag(CellFlags.LinkEntry),
                "the near mouth should be north of the origin");
            Assert.IsTrue(world.Map.GetFlags(new int2(4, 5)).HasFlag(CellFlags.LinkExit),
                "and the far mouth three cells south of it");

            Assert.IsFalse(world.Map.IsPassable(new int2(4, 8)), "near pier");
            Assert.IsFalse(world.Map.IsPassable(new int2(4, 6)), "far pier");
            Assert.IsTrue(world.Map.IsPassable(new int2(4, 7)), "and the ground under the deck is still open");
        }

        [Test]
        public void AnAgentWalksUnderABridgeRatherThanRoundIt()
        {
            using var world = new RtsTestWorld();

            // A bridge running east, and an agent crossing its line from south to north underneath.
            world.CreateBridge(new int2(2, 4), new int2(6, 4));
            world.Tick();

            int2 goal = new(4, 7);
            Entity agent = world.CreateAgent(GridCoords.CellCenter(new int2(4, 1)), goal);
            world.Entities.GetBuffer<TaskStep>(agent).Add(TaskStep.GoTo(goal));

            Assert.IsTrue(
                TickUntil(world, 300,
                    () => math.distance(PositionOf(world, agent), GridCoords.CellCenter(goal)) < 1f),
                "the way under the deck is open, so the agent should just walk through it");

            Assert.IsFalse(world.Entities.IsComponentEnabled<OnBridge>(agent),
                "walking under a bridge is not using it");
        }

        [Test]
        public void APierOverWaterGivesTheWaterBackWhenItIsDemolished()
        {
            using var world = new RtsTestWorld();

            // A pier standing in water: already blocked, and blocked again by the structure. The cost sum has to
            // stay exact through both halves or demolishing the bridge would drain the river.
            world.BlockCells(new int2(3, 2), new int2(1, 0), 3);
            world.Tick();

            ushort water = world.Map.GetCost(new int2(3, 2));

            Entity bridge = world.CreateBridge(new int2(2, 2), new int2(6, 2));
            world.Tick();

            Assert.Greater(world.Map.GetCost(new int2(3, 2)), water, "the pier adds its own contribution");

            world.Entities.DestroyEntity(bridge);
            world.Tick();

            Assert.AreEqual(water, world.Map.GetCost(new int2(3, 2)), "the water is exactly as it was");
            Assert.IsFalse(world.Map.IsPassable(new int2(3, 2)), "and it is still water");
            Assert.IsFalse(world.Map.GetFlags(new int2(2, 2)).HasFlag(CellFlags.LinkEntry), "link taken back");
        }

        [Test]
        public void ABridgeCannotOpenAWallItCrosses()
        {
            using var world = new RtsTestWorld();
            SplitTheMap(world);

            // Placed so the open cell under the deck lands on the wall. Nothing about a bridge ever *clears* a
            // cell, so the wall has to survive being bridged - otherwise a bridge would be a gate.
            world.CreateBridge(new int2(WALL_X - 2, LANE_Y), new int2(WALL_X + 2, LANE_Y));
            world.Tick();

            Assert.IsFalse(world.Map.IsPassable(new int2(WALL_X, LANE_Y)),
                "the wall under the deck is still a wall");
        }

        [Test]
        public void TheFlowFieldPricesTheCrossingRatherThanIgnoringIt()
        {
            using var world = new RtsTestWorld();
            SplitTheMap(world);

            int2 entry = new(WALL_X - 1, LANE_Y);
            int2 exit = new(WALL_X + 3, LANE_Y);
            const int span = 4;

            world.CreateBridge(entry, exit);
            world.Tick();

            int2 goal = new(WALL_X + 5, LANE_Y);

            // Two frames: a field is asked for on one and built on the next (§13.2 invariant 3).
            world.Fields.Request(goal);
            world.TickFrames(3);

            Assert.IsTrue(world.Fields.TryGetSlot(goal, out int slot), "the goal's field should be built");

            ushort atExit = world.Fields.IntegrationAt(slot, exit);
            ushort atEntry = world.Fields.IntegrationAt(slot, entry);

            Assert.AreNotEqual(FlowField.UNREACHABLE, atEntry,
                "the near bank has a route to the far side, and only the bridge can be it");

            // The exact price: the crossing, plus stepping onto the far bank, on top of what is left from there.
            int expected = atExit + NavCost.OfCell(world.Map.GetCost(exit)) + span * NavCost.STEP;
            Assert.AreEqual(expected, atEntry, "the crossing is priced as a road of the same length");

            Assert.IsTrue(world.Fields.IsLinkStep(slot, entry), "the way on from the mouth is the bridge");
            Assert.IsFalse(world.Fields.TryGetDirection(slot, entry, out _),
                "and it is not a direction anyone can walk");
        }

        [Test]
        public void WithoutABridgeTheFarBankIsUnreachable()
        {
            using var world = new RtsTestWorld();
            SplitTheMap(world);
            world.Tick();

            int2 goal = new(WALL_X + 5, LANE_Y);
            world.Fields.Request(goal);
            world.TickFrames(3);

            Assert.IsTrue(world.Fields.TryGetSlot(goal, out int slot));
            Assert.AreEqual(FlowField.UNREACHABLE,
                world.Fields.IntegrationAt(slot, new int2(WALL_X - 1, LANE_Y)),
                "the wall is a wall until something crosses it");
        }

        [Test]
        public void AnAgentCrossesToTheFarBankAndCarriesOnToItsGoal()
        {
            using var world = new RtsTestWorld();
            SplitTheMap(world);

            world.CreateBridge(new int2(WALL_X - 1, LANE_Y), new int2(WALL_X + 3, LANE_Y));
            world.Tick();

            int2 goal = new(WALL_X + 6, LANE_Y);
            Entity agent = world.CreateAgent(GridCoords.CellCenter(new int2(WALL_X - 4, LANE_Y)), goal);
            world.Entities.GetBuffer<TaskStep>(agent).Add(TaskStep.GoTo(goal));

            Assert.IsTrue(
                TickUntil(world, 300, () => PositionOf(world, agent).x > WALL_X),
                "the agent should have got to the far bank, and the bridge is the only way there");

            Assert.IsTrue(
                TickUntil(world, 200,
                    () => math.distance(PositionOf(world, agent), GridCoords.CellCenter(goal)) < 1f),
                "and then carried on to its goal under its own steam");

            Assert.IsFalse(world.Entities.IsComponentEnabled<OnBridge>(agent), "and it is off the bridge");
        }

        [Test]
        public void TheCrossingOnlyRunsOneWay()
        {
            using var world = new RtsTestWorld();
            SplitTheMap(world);

            // Laid west to east, so an agent on the east bank has no way back.
            world.CreateBridge(new int2(WALL_X - 1, LANE_Y), new int2(WALL_X + 3, LANE_Y));
            world.Tick();

            int2 backwards = new(WALL_X - 4, LANE_Y);
            world.Fields.Request(backwards);
            world.TickFrames(3);

            Assert.IsTrue(world.Fields.TryGetSlot(backwards, out int slot));
            Assert.AreEqual(FlowField.UNREACHABLE,
                world.Fields.IntegrationAt(slot, new int2(WALL_X + 3, LANE_Y)),
                "the far bank cannot get home over a one-way bridge");
        }

        [Test]
        public void WhileBeingCarriedTheAgentIsOnTheMapButNotWalking()
        {
            using var world = new RtsTestWorld();
            SplitTheMap(world);

            world.CreateBridge(new int2(WALL_X - 1, LANE_Y), new int2(WALL_X + 5, LANE_Y));
            world.Tick();

            int2 goal = new(WALL_X + 8, LANE_Y);
            Entity agent = world.CreateAgent(GridCoords.CellCenter(new int2(WALL_X - 3, LANE_Y)), goal);
            world.Entities.GetBuffer<TaskStep>(agent).Add(TaskStep.GoTo(goal));

            Assert.IsTrue(TickUntilOnBridge(world, agent, 200), "the agent never got on the bridge");

            // Steering off, and every consequence of that: nothing integrates it, nothing avoids-solves it and
            // the watchdog does not watch it.
            Assert.IsFalse(world.Entities.IsComponentEnabled<PathFollow>(agent), "not walking");

            // But still on the map: visible, hashed, and something the crowd at either mouth has to avoid.
            Assert.IsTrue(world.Entities.IsComponentEnabled<AgentMove>(agent), "still on the map");
            Assert.IsTrue(world.Entities.IsComponentEnabled<ViewVisible>(agent), "still drawn");

            // And moving at its own pace rather than teleporting.
            float2 before = world.Entities.GetComponentData<AgentMove>(agent).Position;
            world.TickFrames(2, 0.05f);
            float2 after = world.Entities.GetComponentData<AgentMove>(agent).Position;

            Assert.Greater(after.x, before.x, "it should be walking the deck, not jumping it");
            Assert.Less(after.x - before.x, 1f, "and no faster than it walks anywhere else");
        }

        [Test]
        public void GettingOntoABridgeIsAStepRatherThanAJump()
        {
            using var world = new RtsTestWorld();
            SplitTheMap(world);

            world.CreateBridge(new int2(WALL_X - 1, LANE_Y), new int2(WALL_X + 5, LANE_Y));
            world.Tick();

            int2 goal = new(WALL_X + 8, LANE_Y);
            Entity agent = world.CreateAgent(GridCoords.CellCenter(new int2(WALL_X - 4, LANE_Y)), goal);
            world.Entities.GetBuffer<TaskStep>(agent).Add(TaskStep.GoTo(goal));

            // An agent is admitted from wherever on the mouth cell it stopped, which is almost never the centre
            // line the deck runs along. Snapping it there would be a visible jump onto the bridge, so the
            // position it had the frame before boarding has to be within one step of the position it has after.
            float2 before = PositionOf(world, agent);
            float step = 0f;
            bool boarded = false;

            for (int i = 0; i < 300 && !boarded; i++)
            {
                float2 previous = PositionOf(world, agent);
                world.TickFrame(0.05f);

                if (world.Entities.IsComponentEnabled<OnBridge>(agent))
                {
                    boarded = true;
                    before = previous;
                    step = math.distance(previous, PositionOf(world, agent));
                }
            }

            Assert.IsTrue(boarded, "the agent never got onto the bridge");

            // One frame at 4 cells a second is 0.2 of a cell; a snap to the mouth centre would be several times
            // that. Generous enough not to be a re-statement of the arithmetic, tight enough to catch a jump.
            Assert.Less(step, 0.35f,
                $"boarding moved the agent {step:0.00} cells in one frame, from {before.x:0.00},{before.y:0.00}");
        }

        [Test]
        public void AnAgentBlockingTheFarBankHoldsUpTheDeckBehindIt()
        {
            using var world = new RtsTestWorld();
            SplitTheMap(world);

            int2 entry = new(WALL_X - 1, LANE_Y);
            int2 exit = new(WALL_X + 3, LANE_Y);
            Entity bridge = world.CreateBridge(entry, exit);
            world.Tick();

            // Standing on the far bank with nowhere to go: this is "an agent that was not able to get off".
            Loiter(world, exit);

            int2 goal = new(WALL_X + 6, LANE_Y);
            for (int i = 0; i < 3; i++)
            {
                Entity walker = world.CreateAgent(
                    GridCoords.CellCenter(new int2(WALL_X - 2 - i, LANE_Y)), goal);

                world.Entities.GetBuffer<TaskStep>(walker).Add(TaskStep.GoTo(goal));
            }

            // Two seconds: long enough for all three to be on the deck and bunched up, and comfortably short
            // of the point where the head gives up on a clear bank and steps off anyway.
            world.TickFrames(40, 0.05f);

            DynamicBuffer<BridgeOccupant> occupants = world.Entities.GetBuffer<BridgeOccupant>(bridge);

            Assert.Greater(occupants.Length, 0, "somebody should have got on");

            Assert.LessOrEqual(occupants.Length, world.Entities.GetComponentData<Bridge>(bridge).Capacity,
                "the deck never holds more than its span");

            // The head is at the far end and cannot get off, which is the whole of the blocking rule.
            Assert.AreEqual(world.Entities.GetComponentData<Bridge>(bridge).Span, occupants[0].Distance,
                0.01f, "the head should be waiting at the far end");

            // Nobody is past anybody: single file is what makes a jam at the exit close the entrance.
            for (int i = 1; i < occupants.Length; i++)
            {
                Assert.LessOrEqual(occupants[i].Distance, occupants[i - 1].Distance,
                    "occupants must stay in order");
            }
        }

        [Test]
        public void ClearingTheFarBankLetsTheQueueDrain()
        {
            using var world = new RtsTestWorld();
            SplitTheMap(world);

            int2 entry = new(WALL_X - 1, LANE_Y);
            int2 exit = new(WALL_X + 3, LANE_Y);
            Entity bridge = world.CreateBridge(entry, exit);
            world.Tick();

            Entity blocker = Loiter(world, exit);

            int2 goal = new(WALL_X + 6, LANE_Y);
            Entity walker = world.CreateAgent(GridCoords.CellCenter(new int2(WALL_X - 3, LANE_Y)), goal);
            world.Entities.GetBuffer<TaskStep>(walker).Add(TaskStep.GoTo(goal));

            Assert.IsTrue(TickUntil(world, 150, () => world.Entities.IsComponentEnabled<OnBridge>(walker)),
                "the walker should have got onto the deck");

            world.TickFrames(40, 0.05f);
            Assert.IsTrue(world.Entities.IsComponentEnabled<OnBridge>(walker),
                "and be stuck on it while the far bank is taken");

            world.Entities.DestroyEntity(blocker);

            Assert.IsTrue(TickUntil(world, 150, () => !world.Entities.IsComponentEnabled<OnBridge>(walker)),
                "clearing the bank should let it off");

            Assert.IsTrue(world.Entities.GetBuffer<BridgeOccupant>(bridge).IsEmpty, "the deck should have cleared");
            Assert.Greater(PositionOf(world, walker).x, WALL_X + 2f, "and the walker should be past the far end");
        }

        [Test]
        public void ABankThatNeverClearsDoesNotStopTheBridgeForGood()
        {
            using var world = new RtsTestWorld();
            SplitTheMap(world);

            int2 exit = new(WALL_X + 3, LANE_Y);
            world.CreateBridge(new int2(WALL_X - 1, LANE_Y), exit);
            world.Tick();

            // Standing on the far bank and never moving. The head of the deck is the one agent with no
            // watchdog behind it, so if this could hold the bridge for ever nothing would ever report it.
            Loiter(world, exit);

            int2 goal = new(WALL_X + 6, LANE_Y);
            Entity walker = world.CreateAgent(GridCoords.CellCenter(new int2(WALL_X - 3, LANE_Y)), goal);
            world.Entities.GetBuffer<TaskStep>(walker).Add(TaskStep.GoTo(goal));

            Assert.IsTrue(TickUntil(world, 150, () => world.Entities.IsComponentEnabled<OnBridge>(walker)),
                "the walker should have got onto the deck");

            Assert.IsTrue(TickUntil(world, 200, () => !world.Entities.IsComponentEnabled<OnBridge>(walker)),
                "and been let off in the end rather than held for ever");
        }

        [Test]
        public void DemolishingABridgeUnderAnAgentPutsItBackOnTheMap()
        {
            using var world = new RtsTestWorld();
            SplitTheMap(world);

            int2 entry = new(WALL_X - 1, LANE_Y);
            Entity bridge = world.CreateBridge(entry, new int2(WALL_X + 5, LANE_Y));
            world.Tick();

            int2 goal = new(WALL_X + 8, LANE_Y);
            Entity agent = world.CreateAgent(GridCoords.CellCenter(new int2(WALL_X - 3, LANE_Y)), goal);
            world.Entities.GetBuffer<TaskStep>(agent).Add(TaskStep.GoTo(goal));

            Assert.IsTrue(TickUntilOnBridge(world, agent, 200), "the agent never got on the bridge");

            world.Entities.DestroyEntity(bridge);
            world.TickFrames(3, 0.05f);

            Assert.IsFalse(world.Entities.IsComponentEnabled<OnBridge>(agent),
                "an agent cannot stay on a bridge that has gone");

            float2 position = world.Entities.GetComponentData<AgentMove>(agent).Position;
            Assert.IsTrue(world.Map.IsPassable(GridCoords.CellOf(position)),
                "and it must not be left standing where nothing can walk");
        }

        [Test]
        public void ABridgeAcrossAChunkBorderIsAGateTheLongRangeSearchCanUse()
        {
            // Big enough that the goal's own field cannot reach the agent, which is what forces the coarse
            // graph to answer instead - and the coarse graph is the half a flow field cannot cover.
            using var world = new RtsTestWorld(256);

            const int wall = 0;
            world.BlockCells(new int2(wall, -128), new int2(0, 1), 256);

            int2 entry = new(wall - 2, 0);
            int2 exit = new(wall + 2, 0);
            world.CreateBridge(entry, exit);
            world.Tick();
            world.TickFrame(0.05f);

            Assert.AreNotEqual(world.Map.ChunkCoordOf(entry).x, world.Map.ChunkCoordOf(exit).x,
                "this test is only meaningful if the two banks are in different chunks");

            using EntityQuery query = world.Entities.CreateEntityQuery(
                ComponentType.ReadOnly<ChunkGateGraph>());

            ChunkGateGraph graph = query.GetSingleton<ChunkGateGraph>();

            var gates = new NativeList<int>(16, Allocator.Temp);
            bool found = GatePathFinder.TryFindGatePath(
                world.Map, graph, new int2(wall - 100, 0), new int2(wall + 100, 0), gates);

            Assert.IsTrue(found, "the far side is reachable, and the bridge is the only way");

            bool usesTheBridge = false;
            foreach (int gate in gates)
            {
                usesTheBridge |= graph.GateBorderOf(gate) == GateBorder.Link;
            }

            Assert.IsTrue(usesTheBridge, "the route has to cross the bridge, so a link gate must be in it");

            gates.Dispose();
        }

        [Test]
        public void ABridgeInsideOneChunkNeedsNoGateOfItsOwn()
        {
            using var world = new RtsTestWorld(256);

            // Both banks in one chunk, so the intra-chunk edge costs carry the crossing and no gate is made.
            const int wall = 8;
            world.BlockCells(new int2(wall, -128), new int2(0, 1), 256);

            world.CreateBridge(new int2(wall - 2, 0), new int2(wall + 2, 0));
            world.Tick();
            world.TickFrame(0.05f);

            using EntityQuery query = world.Entities.CreateEntityQuery(
                ComponentType.ReadOnly<ChunkGateGraph>());

            ChunkGateGraph graph = query.GetSingleton<ChunkGateGraph>();

            var gates = new NativeList<int>(16, Allocator.Temp);
            bool found = GatePathFinder.TryFindGatePath(
                world.Map, graph, new int2(wall - 100, 0), new int2(wall + 100, 0), gates);

            Assert.IsTrue(found, "the crossing still has to be found, just not as a gate");
            gates.Dispose();
        }

        // ---- helpers ---------------------------------------------------------------------------------------

        /// <summary>
        /// A wall of blocked cells right across the map, so the only way from one side to the other is whatever
        /// the test puts there.
        /// </summary>
        private static void SplitTheMap(RtsTestWorld world)
        {
            world.BlockCells(new int2(WALL_X, -32), new int2(0, 1), 64);
        }

        private static bool TickUntilOnBridge(RtsTestWorld world, Entity agent, int frames) =>
            TickUntil(world, frames, () => world.Entities.IsComponentEnabled<OnBridge>(agent));

        private static bool TickUntil(RtsTestWorld world, int frames, System.Func<bool> done)
        {
            for (int i = 0; i < frames; i++)
            {
                world.TickFrame(0.05f);

                if (done())
                {
                    return true;
                }
            }

            return false;
        }

        private static float2 PositionOf(RtsTestWorld world, Entity agent) =>
            world.Entities.GetComponentData<AgentMove>(agent).Position;

        /// <summary>
        /// An agent that stays put where it is told.
        ///
        /// An idle agent will not do: a bridge mouth and a bank both carry <see cref="CellFlags.NoIdle"/>, so
        /// <c>IdleAssignSystem</c> would quite correctly walk it away from the one cell the test needs it to be
        /// standing on. A long <c>Interact</c> is how the game itself makes an agent stand somewhere.
        /// </summary>
        private static Entity Loiter(RtsTestWorld world, int2 cell)
        {
            Entity agent = world.CreateIdleAgent(GridCoords.CellCenter(cell));
            world.Entities.GetBuffer<TaskStep>(agent).Add(TaskStep.Interact(Entity.Null, 10_000f));
            return agent;
        }
    }
}
