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

        /// <summary>The other way round: plants them, and grows a wood that was not there.</summary>
        Planter,

        /// <summary>Somewhere for the wood and ore to end up.</summary>
        WoodYard,
        OreYard,

        /// <summary>Shoots what it does not like the look of. No door, no shelf, nobody inside.</summary>
        Turret,

        /// <summary>A crafter whose output walks out of the door.</summary>
        TrainingCamp,

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

        /// <summary>
        /// What this building plants, or <see cref="ObjectKind.None"/> for one that only takes. What it costs
        /// and what felling it gives are read from <see cref="WorldObjectCatalog"/> and
        /// <see cref="RtsResources"/>, so a planted tree and a tree the map started with are the same thing.
        /// </summary>
        public readonly ObjectKind Plants;

        /// <summary>
        /// How much of a beating it takes, or zero for a building nothing can hurt.
        ///
        /// Only a turret has one today, and that is not an assertion that a bakery is indestructible - it is
        /// that nothing yet attacks one. Step 13 fills this column in, and the cost model there reads the
        /// fraction remaining, which is why the number lives on the blueprint rather than being a constant
        /// in the combat code.
        /// </summary>
        public readonly int Health;

        /// <summary>What it shoots with, or an unarmed <see cref="Rts.Weapon"/> for a building that does not.</summary>
        public readonly Weapon Weapon;

        /// <summary>
        /// What walks out of its door when a batch is finished, or a zero
        /// <see cref="AgentSpec.MaxSpeed"/> for a building that makes things instead of people.
        /// </summary>
        public readonly Trains Trains;

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
            int harvestRange = 0,
            ObjectKind plants = ObjectKind.None,
            int health = 0,
            Weapon weapon = default,
            Trains trains = default)
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
            Plants = plants;
            Health = health;
            Weapon = weapon;
            Trains = trains;
        }

        public bool IsBridge => BridgeCells > 0;

        /// <summary>Sends workers out to the map rather than keeping them at a bench.</summary>
        public bool IsGatherer => Harvests != ObjectKind.None;

        public bool IsPlanter => Plants != ObjectKind.None;

        public bool IsTurret => Weapon.IsArmed;

        /// <summary>Arms its worker rather than filling a shelf. A crafter in every other respect.</summary>
        public bool IsCamp => Trains.Gives.IsArmed;

        /// <summary>
        /// Runs a recipe. A camp counts, and that is the whole of what makes one work: it has inputs, a
        /// craft time and a work order like any other crafter, and what it is short of is an output *item*.
        /// </summary>
        public bool Crafts => CraftSeconds > 0f && (!Output.IsNone || IsCamp);

        /// <summary>
        /// Whether anyone ever goes in. A door is cut for a building agents visit - to work in it, to fetch
        /// from it or to deliver to it - and a turret is the first building that is none of those.
        ///
        /// Worth deriving rather than authoring: a door is not free. It flags a cell as an entrance, which
        /// makes it a cell the arrival queue ranks and the one-way brush must leave alone, and it forces
        /// placement to refuse any spot whose southern neighbour cannot be stood on. A turret in a corner is
        /// a reasonable turret.
        /// </summary>
        public bool HasDoor => Interior > 0 || !Input.IsNone || !Output.IsNone;

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
    /// Everything this example can build. At its centre is a two-stage production chain with hauling at
    /// every join - grain to flour to bread to a warehouse - which is enough to watch every part of §7 and
    /// §8 do its job.
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
                BuildingKind.Planter, "Planter", new int2(2, 2), new Color(0.34f, 0.58f, 0.38f),
                interior: 2, harvestRange: 12, plants: ObjectKind.Tree),

            new BuildingBlueprint(
                BuildingKind.WoodYard, "Wood Yard", new int2(3, 2), new Color(0.48f, 0.40f, 0.28f),
                output: ItemCatalog.Wood),

            new BuildingBlueprint(
                BuildingKind.OreYard, "Ore Yard", new int2(3, 2), new Color(0.52f, 0.42f, 0.46f),
                output: ItemCatalog.Ore),

            // The two of §14 step 12. Between them they are the check that the rows above are a *catalog*
            // and not a list of special cases: a turret has none of the five things every other building
            // here has, and a camp has all of them but one.
            new BuildingBlueprint(
                BuildingKind.Turret, "Turret", new int2(1, 1), new Color(0.70f, 0.30f, 0.35f),
                health: 250,
                weapon: new Weapon { Range = 9f, Damage = 20, ReloadSeconds = 0.8f }),

            // A soldier outranges a turret by nothing and hits for less: a turret is a fixed thing that
            // has to be worth its cell, and a soldier's advantage is that it can be somewhere else tomorrow.
            new BuildingBlueprint(
                BuildingKind.TrainingCamp, "Training Camp", new int2(2, 2), new Color(0.45f, 0.40f, 0.55f),
                interior: 2, input: ItemCatalog.Bread, craftSeconds: 4f,
                health: 300,
                trains: new Trains
                {
                    Gives = new Weapon { Range = 6f, Damage = 12, ReloadSeconds = 0.7f },

                    // Far enough to cover the approach to the buildings around it, short enough that a
                    // raider walking past cannot pull the garrison off the camp (see Post).
                    Leash = 14f,
                }),

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
