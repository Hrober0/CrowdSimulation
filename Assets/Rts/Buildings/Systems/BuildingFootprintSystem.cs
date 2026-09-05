using GridNav;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// Takes and gives back the cells a building stands on (design §13.3 #2).
    ///
    /// Every footprint cell is blocked and flagged <see cref="CellFlags.Building"/>. Blocked cells are what
    /// keep *paths* out of a building; RVO obstacles - which keep a shoved agent out of one - come with the
    /// avoidance integration, not here (§3).
    ///
    /// Entrances are resolved in the same pass and for the same reason: a doorway's cell is derived from the
    /// placement exactly as a footprint cell is, and one system that both takes and gives back everything a
    /// building touches cannot leak half a building's cells on demolition.
    ///
    /// A bridge is resolved here too, and the argument is now a third instance of the same one. Its deck cells
    /// *are* footprint cells and go in the same buffer; its <see cref="NavLink"/> is one more thing the
    /// building took out of the grid and has to give back. Splitting any of it out would be a second place that
    /// has to remember a bridge exists, and the thing it would eventually forget is the link - which is
    /// invisible on the map and would go on carrying agents over a bridge that had been torn down.
    /// </summary>
    [UpdateInGroup(typeof(GridUpdateGroup))]
    [UpdateAfter(typeof(CellObjectRegistrationSystem))]
    public partial struct BuildingFootprintSystem : ISystem
    {
        /// <summary>A footprint cell always blocks, so the refund is a constant and needs no recording.</summary>
        private const int FOOTPRINT_COST = CellData.BLOCKED;

        /// <summary>
        /// What an entrance cell is marked with. <see cref="CellFlags.NoIdle"/> is the half that matters at
        /// runtime: a doorway that agents are allowed to loiter in is a doorway that gets blocked (§6).
        /// </summary>
        private const CellFlags ENTRANCE_FLAGS = CellFlags.Entrance | CellFlags.NoIdle;

        /// <summary>
        /// What a crossing costs per cell of span.
        ///
        /// <see cref="NavCost.STEP"/> alone, which is what a cell of road costs, because that is what a bridge
        /// is: built ground that is no slower than the best ground there is. Crucially it is not *free* - a
        /// crossing priced at nothing would be a hole in the cost model that every route in range fell into,
        /// and half the map would detour over a footbridge to save a corner.
        ///
        /// It also has to agree with how long the crossing actually takes, or the routing layer and the bridge
        /// tell the agent two different stories about the same walk.
        /// </summary>
        private const int BRIDGE_COST_PER_CELL = NavCost.STEP;

        private EntityQuery _placed;
        private EntityQuery _demolished;

        public void OnCreate(ref SystemState state)
        {
            _placed = SystemAPI.QueryBuilder()
                               .WithAll<BuildingPlacement, BuildingFootprintOffset>()
                               .WithNone<BuildingFootprintCell>()
                               .Build();

            _demolished = SystemAPI.QueryBuilder()
                                   .WithAll<BuildingFootprintCell>()
                                   .WithNone<BuildingPlacement>()
                                   .Build();

            state.RequireForUpdate<GridWorld>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (_placed.IsEmpty && _demolished.IsEmpty)
            {
                return;
            }

            GridEditQueue edits = SystemAPI.GetSingletonRW<GridWorld>().ValueRW.Edits;
            var commands = new EntityCommandBuffer(Allocator.Temp);

            foreach ((RefRO<BuildingPlacement> placement, DynamicBuffer<BuildingFootprintOffset> footprint, Entity entity)
                     in SystemAPI.Query<RefRO<BuildingPlacement>, DynamicBuffer<BuildingFootprintOffset>>()
                                 .WithNone<BuildingFootprintCell>()
                                 .WithEntityAccess())
            {
                DynamicBuffer<BuildingFootprintCell> occupied =
                    commands.AddBuffer<BuildingFootprintCell>(entity);

                DynamicBuffer<BuildingEntranceCell> doorways =
                    commands.AddBuffer<BuildingEntranceCell>(entity);

                BuildingPlacement placed = placement.ValueRO;
                foreach (BuildingFootprintOffset offset in footprint)
                {
                    int2 cell = BuildingGeometry.CellOf(placed.OriginCell, offset.Offset, placed.Rotation);

                    edits.Enqueue(GridEdit.CostDelta(cell, FOOTPRINT_COST));
                    edits.Enqueue(GridEdit.AddFlags(cell, CellFlags.Building));

                    occupied.Add(new BuildingFootprintCell { Cell = cell });
                }

                if (SystemAPI.HasBuffer<BuildingEntranceOffset>(entity))
                {
                    foreach (BuildingEntranceOffset entrance
                             in SystemAPI.GetBuffer<BuildingEntranceOffset>(entity))
                    {
                        Direction side = BuildingGeometry.SideOf(entrance.Side, placed.Rotation);
                        int2 doorstep = BuildingGeometry.DoorstepOf(
                            placed.OriginCell, entrance.Offset, entrance.Side, placed.Rotation);

                        edits.Enqueue(GridEdit.AddFlags(doorstep, ENTRANCE_FLAGS));

                        doorways.Add(new BuildingEntranceCell
                        {
                            Cell = doorstep,
                            Facing = DirectionUtils.Opposite(side),
                        });
                    }
                }

                if (SystemAPI.HasComponent<BridgeSpan>(entity))
                {
                    LayBridge(edits, ref commands, occupied, entity,
                              SystemAPI.GetComponent<BridgeSpan>(entity), placed);
                }
            }

            foreach ((DynamicBuffer<BuildingFootprintCell> occupied, DynamicBuffer<BuildingEntranceCell> doorways, Entity entity)
                     in SystemAPI.Query<DynamicBuffer<BuildingFootprintCell>, DynamicBuffer<BuildingEntranceCell>>()
                                 .WithNone<BuildingPlacement>()
                                 .WithEntityAccess())
            {
                foreach (BuildingFootprintCell cell in occupied)
                {
                    edits.Enqueue(GridEdit.CostDelta(cell.Cell, -FOOTPRINT_COST));
                    edits.Enqueue(GridEdit.RemoveFlags(cell.Cell, CellFlags.Building));
                }

                // Flags are bits, not counts, so two buildings whose doorsteps land on the same cell would
                // have that cell cleared by whichever is demolished first. Left as is: it needs two doors
                // facing each other across one cell, and a refcount per flag would cost every cell four
                // bytes to fix an authoring mistake.
                foreach (BuildingEntranceCell doorway in doorways)
                {
                    edits.Enqueue(GridEdit.RemoveFlags(doorway.Cell, ENTRANCE_FLAGS));
                }

                if (SystemAPI.HasComponent<Bridge>(entity))
                {
                    ClearBridge(edits, ref commands, entity, SystemAPI.GetComponent<Bridge>(entity));
                }

                commands.RemoveComponent<BuildingFootprintCell>(entity);
                commands.RemoveComponent<BuildingEntranceCell>(entity);
            }

            commands.Playback(state.EntityManager);
            commands.Dispose();
        }

        /// <summary>
        /// Stands the two piers up, opens the two mouths and hangs the link between them.
        ///
        /// **The cells between the piers are not touched**, and that is the whole difference between a bridge
        /// and a wall with a gate in it. The deck is overhead, so the ground beneath it is still ground and
        /// traffic crossing the line of the bridge passes under it. Only the piers are structure, and only they
        /// go into the <see cref="BuildingFootprintCell"/> buffer - the same buffer as any other footprint,
        /// which is what makes demolition give them back through the one path that already exists.
        ///
        /// The mouths are not in it either: they are cells agents stand on, and blocking either would wall the
        /// bridge off from the map it exists to join.
        ///
        /// A span that is not a straight line, or too short to have two piers with the mouths outside them, is
        /// refused and said so. The building still stands; it is simply a building rather than a bridge, which
        /// is a great deal easier to notice than a crossing that quietly goes nowhere.
        /// </summary>
        private static void LayBridge(GridEditQueue edits, ref EntityCommandBuffer commands,
                                      DynamicBuffer<BuildingFootprintCell> occupied, Entity entity,
                                      in BridgeSpan span, in BuildingPlacement placed)
        {
            int2 entry = placed.OriginCell + RotationUtils.Rotate(span.EntryOffset, placed.Rotation);
            int2 exit = placed.OriginCell + RotationUtils.Rotate(span.ExitOffset, placed.Rotation);

            if (!Bridge.TryShape(entry, exit, out BridgeShape shape))
            {
                UnityEngine.Debug.LogWarning("[Rts] Dropped a bridge: its mouths must be in line and at least three cells apart.");
                return;
            }

            Pier(edits, occupied, shape.NearPier);

            // Guarded, because on the shortest bridge the two piers are one cell and taking it twice would
            // leave it blocked after the bridge came down.
            if (!shape.FarPier.Equals(shape.NearPier))
            {
                Pier(edits, occupied, shape.FarPier);
            }

            // NoIdle is the half that matters, exactly as at a doorstep: a mouth that agents may loiter on is
            // a mouth that gets blocked, and this one has a queue behind it.
            edits.Enqueue(GridEdit.AddFlags(entry, ENTRANCE_FLAGS));
            edits.Enqueue(GridEdit.AddFlags(exit, ENTRANCE_FLAGS));

            edits.Enqueue(GridEdit.AddLink(entry, exit, (ushort)(shape.Span * BRIDGE_COST_PER_CELL)));

            commands.AddComponent(entity, new Bridge { Entry = entry, Exit = exit, Span = shape.Span });
            commands.AddBuffer<BridgeOccupant>(entity);
        }

        private static void Pier(GridEditQueue edits, DynamicBuffer<BuildingFootprintCell> occupied, int2 cell)
        {
            edits.Enqueue(GridEdit.CostDelta(cell, FOOTPRINT_COST));
            edits.Enqueue(GridEdit.AddFlags(cell, CellFlags.Building));

            occupied.Add(new BuildingFootprintCell { Cell = cell });
        }

        /// <summary>
        /// Takes the link and the mouth flags back. The deck goes with the footprint, above.
        ///
        /// Occupants are not touched here, and could not be: this runs in the grid phase, and putting an agent
        /// back on the map is the agent phase's business. <see cref="BridgeTransitSystem"/> finds them next
        /// frame with no bridge left to be on and puts them down - one rule, wherever a bridge went.
        /// </summary>
        private static void ClearBridge(GridEditQueue edits, ref EntityCommandBuffer commands, Entity entity,
                                        in Bridge bridge)
        {
            edits.Enqueue(GridEdit.RemoveLink(bridge.Entry, bridge.Exit));
            edits.Enqueue(GridEdit.RemoveFlags(bridge.Entry, ENTRANCE_FLAGS));
            edits.Enqueue(GridEdit.RemoveFlags(bridge.Exit, ENTRANCE_FLAGS));

            commands.RemoveComponent<Bridge>(entity);
            commands.RemoveComponent<BridgeOccupant>(entity);
        }
    }
}
