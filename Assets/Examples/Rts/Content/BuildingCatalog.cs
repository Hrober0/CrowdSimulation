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

        /// <summary>Sends its miners out to the ore seams around it.</summary>
        Mine,

        /// <summary>The same building, felling trees.</summary>
        LumberCamp,

        /// <summary>Somewhere for the wood and ore to end up.</summary>
        WoodYard,
        OreYard,

        /// <summary>A one-way crossing: two piers with one cell of open ground under it.</summary>
        Bridge,

        /// <summary>The same, two cells longer under the deck.</summary>
        LongBridge,
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

        /// <summary>
        /// Structure cells of a bridge, or zero for anything that is not one.
        ///
        /// A fixed length rather than something the player drags out, so a bridge is chosen from the menu and
        /// placed with one click like every other building. Three is a pier, a cell of open ground and a pier;
        /// each extra cell is one more cell of ground the deck passes over.
        /// </summary>
        public readonly int BridgeCells;

        /// <summary>
        /// What this building sends its workers out to harvest, or <see cref="ObjectKind.None"/> for one that
        /// stays at home. The yield is <see cref="Output"/> - a mine's ore is its output in exactly the sense
        /// a bakery's bread is, which is why nothing downstream has to know where it came from.
        /// </summary>
        public readonly ObjectKind Harvests;

        /// <summary>How far out, in cells. See <see cref="Reaps.Range"/>.</summary>
        public readonly int HarvestRange;

        public BuildingBlueprint(
            BuildingKind kind,
            string name,
            int2 size,
            Color tint,
            int interior = 0,
            ItemId input = default,
            ItemId output = default,
            float craftSeconds = 0f,
            bool isShelter = false,
            int bridgeCells = 0,
            ObjectKind harvests = ObjectKind.None,
            int harvestRange = 0)
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
            BridgeCells = bridgeCells;
            Harvests = harvests;
            HarvestRange = harvestRange;
        }

        public bool IsBridge => BridgeCells > 0;

        /// <summary>Sends workers out to the map rather than keeping them at a bench.</summary>
        public bool IsGatherer => Harvests != ObjectKind.None;

        public bool Crafts => CraftSeconds > 0f && !Output.IsNone;

        /// <summary>
        /// A warehouse: takes one item in and gives it to anyone who wants it more.
        ///
        /// A gatherer is explicitly not one, even though it also has an output and no recipe. Its shelf is a
        /// pure source like a crafter's - things arrive on it because its own workers brought them - and a
        /// warehouse's standing request would have a mine asking the map to deliver the ore it is standing
        /// on top of.
        /// </summary>
        public bool Stores => !Crafts && !IsGatherer && !Output.IsNone;
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

            new BuildingBlueprint(
                BuildingKind.Mine, "Mine", new int2(2, 2), new Color(0.62f, 0.42f, 0.32f),
                interior: 3, output: ItemCatalog.Ore,
                harvests: ObjectKind.Ore, harvestRange: 14),

            new BuildingBlueprint(
                BuildingKind.LumberCamp, "Lumber Camp", new int2(2, 2), new Color(0.42f, 0.52f, 0.32f),
                interior: 3, output: ItemCatalog.Wood,
                harvests: ObjectKind.Tree, harvestRange: 16),

            new BuildingBlueprint(
                BuildingKind.WoodYard, "Wood Yard", new int2(3, 2), new Color(0.48f, 0.40f, 0.28f),
                output: ItemCatalog.Wood),

            new BuildingBlueprint(
                BuildingKind.OreYard, "Ore Yard", new int2(3, 2), new Color(0.52f, 0.42f, 0.46f),
                output: ItemCatalog.Ore),

            new BuildingBlueprint(
                BuildingKind.Bridge, "Bridge", new int2(3, 1), new Color(0.62f, 0.52f, 0.38f),
                bridgeCells: 3),

            new BuildingBlueprint(
                BuildingKind.LongBridge, "Long Bridge", new int2(4, 1), new Color(0.55f, 0.45f, 0.32f),
                bridgeCells: 4),
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
