using Rts;
using Unity.Mathematics;
using UnityEngine;

namespace Examples.Rts
{
    public enum BuildingKind
    {
        Farm,
        Mill,
        Bakery,
        Warehouse,
        Hut,
    }

    /// <summary>
    /// One row of the building menu: everything needed to place one, in one struct.
    ///
    /// A crafter is a footprint, a door, some benches, storage slots and a recipe - and the interesting part
    /// is that none of those are crafter-specific. A warehouse is the same thing with no recipe and no
    /// benches; a hut is the same thing with beds and no storage. So the catalog is data, and
    /// <see cref="RtsConstruction"/> reads all of it the same way.
    /// </summary>
    public readonly struct BuildingBlueprint
    {
        public readonly BuildingKind Kind;
        public readonly string Name;
        public readonly int2 Size;
        public readonly Color Tint;

        /// <summary>Beds or benches. Zero means nobody can go inside.</summary>
        public readonly int Interior;

        /// <summary>Consumed per batch. <see cref="ItemId.None"/> for a building that makes nothing.</summary>
        public readonly ItemId Input;

        /// <summary>Produced per batch, or the item a warehouse stores.</summary>
        public readonly ItemId Output;

        public readonly float CraftSeconds;

        public readonly bool IsShelter;

        public BuildingBlueprint(
            BuildingKind kind,
            string name,
            int2 size,
            Color tint,
            int interior = 0,
            ItemId input = default,
            ItemId output = default,
            float craftSeconds = 0f,
            bool isShelter = false)
        {
            Kind = kind;
            Name = name;
            Size = size;
            Tint = tint;
            Interior = interior;
            Input = input;
            Output = output;
            CraftSeconds = craftSeconds;
            IsShelter = isShelter;
        }

        public bool Crafts => CraftSeconds > 0f && !Output.IsNone;

        /// <summary>A warehouse: takes one item in and gives it to anyone who wants it more.</summary>
        public bool Stores => !Crafts && !Output.IsNone;
    }

    /// <summary>
    /// The five things this example can build. Between them they make a two-stage production chain with
    /// hauling at every join - grain to flour to bread to a warehouse - which is enough to watch every part
    /// of §7 and §8 do its job.
    /// </summary>
    public static class BuildingCatalog
    {
        public static BuildingBlueprint[] All => new[]
        {
            new BuildingBlueprint(
                BuildingKind.Farm, "Farm", new int2(2, 2), new Color(0.45f, 0.65f, 0.30f),
                interior: 1, output: ItemCatalog.Grain, craftSeconds: 2.5f),

            new BuildingBlueprint(
                BuildingKind.Mill, "Mill", new int2(2, 2), new Color(0.70f, 0.65f, 0.45f),
                interior: 1, input: ItemCatalog.Grain, output: ItemCatalog.Flour, craftSeconds: 2f),

            new BuildingBlueprint(
                BuildingKind.Bakery, "Bakery", new int2(2, 2), new Color(0.80f, 0.45f, 0.30f),
                interior: 2, input: ItemCatalog.Flour, output: ItemCatalog.Bread, craftSeconds: 2f),

            new BuildingBlueprint(
                BuildingKind.Warehouse, "Warehouse", new int2(3, 2), new Color(0.40f, 0.50f, 0.70f),
                output: ItemCatalog.Bread),

            new BuildingBlueprint(
                BuildingKind.Hut, "Hauler Hut", new int2(2, 2), new Color(0.55f, 0.45f, 0.60f),
                interior: 6, isShelter: true),
        };

        public static BuildingBlueprint Of(BuildingKind kind)
        {
            foreach (BuildingBlueprint blueprint in All)
            {
                if (blueprint.Kind == kind)
                {
                    return blueprint;
                }
            }

            return All[0];
        }
    }
}
