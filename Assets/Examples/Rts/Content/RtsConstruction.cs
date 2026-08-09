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
        /// <summary>
        /// Whether a building of this shape may stand here: every cell on the map, passable, and not already
        /// part of another building. The doorway has to be walkable too, or the building would be sealed.
        /// </summary>
        public static bool CanPlace(in GridMap map, in BuildingBlueprint blueprint, int2 origin)
        {
            for (int y = 0; y < blueprint.Size.y; y++)
            {
                for (int x = 0; x < blueprint.Size.x; x++)
                {
                    var cell = new int2(origin.x + x, origin.y + y);
                    if (!map.IsPassable(cell) || map.GetFlags(cell) != CellFlags.None)
                    {
                        return false;
                    }
                }
            }

            return map.IsPassable(DoorstepOf(blueprint, origin));
        }

        /// <summary>Where agents will stand to use it: below the bottom-left cell, outside the footprint.</summary>
        public static int2 DoorstepOf(in BuildingBlueprint blueprint, int2 origin) =>
            new(origin.x, origin.y - 1);

        public static Entity Place(EntityManager entities, in BuildingBlueprint blueprint, int2 origin)
        {
            Entity building = entities.CreateEntity();

            entities.AddComponentData(building, new BuildingPlacement
            {
                OriginCell = origin,
                Rotation = GridRotation.None,
            });

            DynamicBuffer<BuildingFootprintOffset> footprint =
                entities.AddBuffer<BuildingFootprintOffset>(building);

            for (int y = 0; y < blueprint.Size.y; y++)
            {
                for (int x = 0; x < blueprint.Size.x; x++)
                {
                    footprint.Add(new BuildingFootprintOffset { Offset = new int2(x, y) });
                }
            }

            // One door, in the bottom-left cell's south wall. A single entrance is enough to show the
            // mechanism, and it keeps the blueprint to a size rather than a shape.
            entities.AddBuffer<BuildingEntranceOffset>(building)
                    .Add(new BuildingEntranceOffset { Offset = int2.zero, Side = Direction.South });

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
    }

    /// <summary>Which blueprint a building was made from, so the inspector can name it.</summary>
    public struct BuildingLabel : IComponentData
    {
        public BuildingKind Kind;
    }
}
