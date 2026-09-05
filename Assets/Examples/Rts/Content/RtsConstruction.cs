using GridNav;
using Rts;
using Unity.Entities;
using Unity.Mathematics;

namespace Examples.Rts
{
    /// <summary>
    /// Putting a building on the map at runtime, and taking it off again.
    ///
    /// There is no runtime-only machinery here: the entity this creates is exactly what
    /// <see cref="BuildingAuthoring"/> bakes, and <c>BuildingFootprintSystem</c> picks it up on the next grid
    /// phase without knowing or caring which of the two made it. That is the payoff of §13.2's single-writer
    /// rule - "place a building" is "create an entity", and the grid sorts itself out.
    /// </summary>
    public static class RtsConstruction
    {
        /// <summary>What a bridge mouth may not already be. A road under one is fine; another door is not.</summary>
        private const CellFlags MOUTH_CONFLICTS =
            CellFlags.Building | CellFlags.Entrance | CellFlags.LinkEntry | CellFlags.LinkExit;

        /// <summary>
        /// One door, in the bottom-left cell's south wall. A single entrance is enough to show the mechanism,
        /// and it keeps the blueprint to a size rather than a shape. Named here because the placement check,
        /// the preview and <see cref="Place"/> all have to mean the same door.
        /// </summary>
        private static readonly int2 DoorWallOffset = int2.zero;

        private const Direction DOOR_SIDE = Direction.South;

        /// <summary>
        /// The <c>OriginCell</c> to store for a building the player has put the cursor on.
        ///
        /// Rotation happens *about* the origin cell (`RotationUtils`), so a rotated footprint runs off in a
        /// different direction from an unrotated one - a quarter turn sends a 3x2 building down and to the left
        /// of the cell it was authored to occupy. Left alone, that makes the cursor mean a different corner of
        /// the building at every rotation: the preview and the placement disagree, and turning a building walks
        /// it away from the mouse.
        ///
        /// So the origin is shifted to put the *rotated* shape where the cursor is. Rotating then spins the
        /// building inside a box anchored under the mouse, which is what every RTS does and what the preview can
        /// honestly draw. With no rotation the shift is zero, so nothing that placed buildings before behaves
        /// differently.
        /// </summary>
        public static int2 OriginFor(in BuildingBlueprint blueprint, int2 cursor,
                                     GridRotation rotation = GridRotation.None)
        {
            // A bridge is anchored on the mouth agents step on from rather than on a bounding box. It is a
            // directed line, so "here is where you get on, and it runs away from you" is the one description
            // that stays meaningful through a rotation - a corner of its box does not.
            if (blueprint.IsBridge)
            {
                return cursor - RotationUtils.Rotate(new int2(-1, 0), rotation);
            }

            var low = new int2(int.MaxValue, int.MaxValue);

            for (int y = 0; y < blueprint.Size.y; y++)
            {
                for (int x = 0; x < blueprint.Size.x; x++)
                {
                    low = math.min(low, RotationUtils.Rotate(new int2(x, y), rotation));
                }
            }

            return cursor - low;
        }

        /// <summary>
        /// Whether a building of this shape may stand under the cursor, turned this way: every cell on the map,
        /// passable, and not already part of another building. The doorway has to be walkable too, or the
        /// building would be sealed.
        /// </summary>
        public static bool CanPlace(in GridMap map, in BuildingBlueprint blueprint, int2 cursor,
                                    GridRotation rotation = GridRotation.None)
        {
            int2 origin = OriginFor(blueprint, cursor, rotation);

            if (blueprint.IsBridge)
            {
                return CanPlaceBridge(map, blueprint, origin, rotation);
            }

            for (int y = 0; y < blueprint.Size.y; y++)
            {
                for (int x = 0; x < blueprint.Size.x; x++)
                {
                    int2 cell = BuildingGeometry.CellOf(origin, new int2(x, y), rotation);
                    if (!map.IsPassable(cell) || map.GetFlags(cell) != CellFlags.None)
                    {
                        return false;
                    }
                }
            }

            return map.IsPassable(DoorstepOf(origin, rotation));
        }

        /// <summary>
        /// Where agents will stand to use it: outside the wall the door is cut into.
        ///
        /// Derived from the rotation rather than assumed to be below the origin, which is the same argument
        /// <c>BuildingEntranceOffset</c> makes - the door is authored as a wall and a side, so where an agent
        /// stands follows from the placement instead of being a second fact that has to be kept true by hand.
        /// It was hardcoded to "one below the origin", which was quietly wrong for every rotated building.
        ///
        /// Answered by <see cref="BuildingGeometry"/>, which is the same function <c>BuildingFootprintSystem</c>
        /// asks when the building actually goes down - the check and the placement cannot drift apart while
        /// they share it.
        /// </summary>
        public static int2 DoorstepOf(int2 origin, GridRotation rotation = GridRotation.None) =>
            BuildingGeometry.DoorstepOf(origin, DoorWallOffset, DOOR_SIDE, rotation);

        /// <summary>Which way the door faces once the building has been turned.</summary>
        public static Direction DoorSideOf(GridRotation rotation = GridRotation.None) =>
            BuildingGeometry.SideOf(DOOR_SIDE, rotation);

        /// <summary>The wall cell the door is cut into - the inside end of the doorway, for drawing it.</summary>
        public static int2 DoorWallOf(int2 origin, GridRotation rotation = GridRotation.None) =>
            BuildingGeometry.CellOf(origin, DoorWallOffset, rotation);

        public static Entity Place(EntityManager entities, in BuildingBlueprint blueprint, int2 cursor,
                                   GridRotation rotation = GridRotation.None)
        {
            Entity building = entities.CreateEntity();

            entities.AddComponentData(building, new BuildingPlacement
            {
                OriginCell = OriginFor(blueprint, cursor, rotation),
                Rotation = rotation,
            });

            if (blueprint.IsBridge)
            {
                return FinishBridge(entities, building, blueprint);
            }

            DynamicBuffer<BuildingFootprintOffset> footprint =
                entities.AddBuffer<BuildingFootprintOffset>(building);

            for (int y = 0; y < blueprint.Size.y; y++)
            {
                for (int x = 0; x < blueprint.Size.x; x++)
                {
                    footprint.Add(new BuildingFootprintOffset { Offset = new int2(x, y) });
                }
            }

            // The offsets go in unrotated: BuildingFootprintSystem turns them by the placement's rotation,
            // so writing them turned here would apply the quarter turn twice.
            entities.AddBuffer<BuildingEntranceOffset>(building)
                    .Add(new BuildingEntranceOffset { Offset = DoorWallOffset, Side = DOOR_SIDE });

            if (blueprint.Interior > 0)
            {
                entities.AddComponentData(building, new Interior { Capacity = blueprint.Interior });
            }

            if (blueprint.IsShelter)
            {
                entities.AddComponent<IdleShelter>(building);
            }

            AddStorage(entities, building, blueprint);
            AddRecipe(entities, building, blueprint);

            entities.AddComponentData(building, new BuildingVisual
            {
                Tint = new float4(blueprint.Tint.r, blueprint.Tint.g, blueprint.Tint.b, blueprint.Tint.a),
            });

            entities.AddComponentData(building, new BuildingLabel { Kind = blueprint.Kind });

            return building;
        }

        /// <summary>
        /// The two mouths of a bridge of this blueprint placed here: the cell before the near pier and the cell
        /// after the far one.
        ///
        /// A bridge is authored as its structure, running east before rotation, and the mouths are the cells
        /// either side of it. Deriving them rather than listing them is what keeps "the mouth is outside the
        /// structure and therefore walkable" true after a rotation without anybody having to check.
        /// </summary>
        public static void MouthsOf(in BuildingBlueprint blueprint, int2 origin, GridRotation rotation,
                                    out int2 entry, out int2 exit)
        {
            entry = origin + RotationUtils.Rotate(new int2(-1, 0), rotation);
            exit = origin + RotationUtils.Rotate(new int2(blueprint.BridgeCells, 0), rotation);
        }

        /// <summary>
        /// Whether a bridge of this blueprint may stand here.
        ///
        /// Three different questions, and they are different on purpose:
        ///
        /// **The mouths must be stood on.** A crossing that lands where nobody can stand is a crossing nobody
        /// can use, and an agent put down inside a blocked cell cannot get out of it.
        ///
        /// **The piers are ordinary footprint**, so they want buildable ground like any other building.
        ///
        /// **What is under the deck is not asked about at all.** That is the point of the shape: the ground
        /// below is untouched, so a bridge may pass over a road, a river, a tree or a crowd. Only another
        /// building is refused there, and only because two views on one cell reads as a mistake.
        /// </summary>
        public static bool CanPlaceBridge(in GridMap map, in BuildingBlueprint blueprint, int2 origin,
                                          GridRotation rotation)
        {
            MouthsOf(blueprint, origin, rotation, out int2 entry, out int2 exit);

            if (!Bridge.TryShape(entry, exit, out BridgeShape shape))
            {
                return false;
            }

            if (!map.IsPassable(entry) || !map.IsPassable(exit))
            {
                return false;
            }

            // A mouth that is already the mouth of something else cannot be reused: one link per cell (§3),
            // and a doorstep shared with a bridge would have two things deciding what happens on it.
            if (HasAnyFlag(map, entry, MOUTH_CONFLICTS) || HasAnyFlag(map, exit, MOUTH_CONFLICTS))
            {
                return false;
            }

            if (!IsBuildable(map, shape.NearPier) || !IsBuildable(map, shape.FarPier))
            {
                return false;
            }

            for (int i = 0; i < shape.GapCells; i++)
            {
                if (map.GetFlags(shape.GapCell(i)).HasFlag(CellFlags.Building))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Finishes a bridge entity: the two mouths, and nothing else. The piers, the link and the mouth flags
        /// are all worked out by <c>BuildingFootprintSystem</c>, which is also what gives them back (§5).
        /// </summary>
        private static Entity FinishBridge(EntityManager entities, Entity bridge,
                                           in BuildingBlueprint blueprint)
        {
            // Empty, but present: the placement query is keyed on this buffer, and a bridge's footprint is
            // derived from its span rather than authored.
            entities.AddBuffer<BuildingFootprintOffset>(bridge);

            entities.AddComponentData(bridge, new BridgeSpan
            {
                EntryOffset = new int2(-1, 0),
                ExitOffset = new int2(blueprint.BridgeCells, 0),
            });

            entities.AddComponentData(bridge, new BuildingVisual
            {
                Tint = new float4(blueprint.Tint.r, blueprint.Tint.g, blueprint.Tint.b, blueprint.Tint.a),
            });

            entities.AddComponentData(bridge, new BuildingLabel { Kind = blueprint.Kind });

            return bridge;
        }

        private static bool IsBuildable(in GridMap map, int2 cell) =>
            map.IsPassable(cell) && map.GetFlags(cell) == CellFlags.None;

        /// <summary>
        /// Takes a building away. The cells come back on the next grid phase, from the cleanup buffers - so
        /// this really is just "destroy the entity", exactly as demolition is in the tests.
        /// </summary>
        public static void Demolish(EntityManager entities, Entity building)
        {
            if (entities.Exists(building))
            {
                entities.DestroyEntity(building);
            }
        }

        /// <summary>
        /// The four rows of §7's table, chosen by what the building is for. These numbers are the whole of
        /// its economic behaviour - there is nothing else to configure and nothing to connect up.
        /// </summary>
        private static void AddStorage(EntityManager entities, Entity building, in BuildingBlueprint blueprint)
        {
            if (blueprint.Input.IsNone && blueprint.Output.IsNone)
            {
                return;
            }

            DynamicBuffer<StorageSlot> slots = entities.AddBuffer<StorageSlot>(building);

            if (!blueprint.Input.IsNone)
            {
                // Crafter input: asks hard, and never gives back what it has been given.
                slots.Add(new StorageSlot
                {
                    Item = blueprint.Input,
                    Capacity = 20,
                    DeliverInUpTo = 20,
                    DeliverOutDownTo = 20,
                    Priority = 6,
                });
            }

            if (blueprint.Crafts)
            {
                // Crafter output: never asks, gives everything away.
                slots.Add(new StorageSlot
                {
                    Item = blueprint.Output,
                    Capacity = 20,
                    DeliverInUpTo = 0,
                    DeliverOutDownTo = 0,
                    Priority = 0,
                });
                return;
            }

            if (blueprint.Stores)
            {
                // Warehouse: always wants more, at the lowest priority that still counts as wanting.
                slots.Add(new StorageSlot
                {
                    Item = blueprint.Output,
                    Capacity = 200,
                    DeliverInUpTo = 200,
                    DeliverOutDownTo = 0,
                    Priority = 1,
                });
            }
        }

        private static void AddRecipe(EntityManager entities, Entity building, in BuildingBlueprint blueprint)
        {
            if (!blueprint.Crafts)
            {
                return;
            }

            entities.AddComponentData(building, new Recipe
            {
                CraftSeconds = blueprint.CraftSeconds,
                Priority = 5,
            });

            DynamicBuffer<RecipeInput> inputs = entities.AddBuffer<RecipeInput>(building);
            if (!blueprint.Input.IsNone)
            {
                inputs.Add(new RecipeInput { Item = blueprint.Input, Amount = 1 });
            }

            entities.AddBuffer<RecipeOutput>(building)
                    .Add(new RecipeOutput { Item = blueprint.Output, Amount = 1 });
        }

        private static bool HasAnyFlag(in GridMap map, int2 cell, CellFlags flags) =>
            (map.GetFlags(cell) & flags) != CellFlags.None;
    }

    /// <summary>Which blueprint a building was made from, so the inspector can name it.</summary>
    public struct BuildingLabel : IComponentData
    {
        public BuildingKind Kind;
    }
}
