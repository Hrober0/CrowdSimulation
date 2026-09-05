using Rts;
using Unity.Entities;
using Unity.Mathematics;

namespace Examples.Rts
{
    /// <summary>
    /// Putting harvestable objects on the map, and the yields they hold (design §14 step 9).
    ///
    /// The same shape as <see cref="RtsConstruction"/> and for the same reason: "place a seam" is "create an
    /// entity", and the grid, the cell map and the economy pick it up without any of them being told. There
    /// is no resource system to add - a node is a <see cref="CellObject"/> that happens to carry stock.
    /// </summary>
    public static class RtsResources
    {
        /// <summary>How much a tree is worth felling, and a seam worth mining. Content, not design.</summary>
        public const int WOOD_PER_TREE = 12;

        public const int ORE_PER_SEAM = 40;

        /// <summary>
        /// A tree: an obstacle worth walking round, holding the wood it yields when somebody fells it.
        ///
        /// It is a node from the moment it is planted rather than becoming one when a lumber camp appears,
        /// because "how much wood is in this tree" is a property of the tree. A wood with no camp near it is
        /// simply a wood nobody is cutting.
        /// </summary>
        public static Entity PlaceTree(EntityManager entities, int2 cell) =>
            PlaceNode(entities, cell, ObjectKind.Tree, YieldOf(ObjectKind.Tree), YieldAmountOf(ObjectKind.Tree));

        public static Entity PlaceOre(EntityManager entities, int2 cell) =>
            PlaceNode(entities, cell, ObjectKind.Ore, YieldOf(ObjectKind.Ore), YieldAmountOf(ObjectKind.Ore));

        /// <summary>
        /// What harvesting one of these gives. Asked by the planter too, so that a tree somebody grew and a
        /// tree the map started with are worth the same - which is what makes a grove indistinguishable from
        /// a wood as far as the rest of the game is concerned.
        /// </summary>
        public static ItemId YieldOf(ObjectKind kind) => kind switch
        {
            ObjectKind.Tree => ItemCatalog.Wood,
            ObjectKind.Ore => ItemCatalog.Ore,
            _ => ItemId.None,
        };

        public static int YieldAmountOf(ObjectKind kind) => kind switch
        {
            ObjectKind.Tree => WOOD_PER_TREE,
            ObjectKind.Ore => ORE_PER_SEAM,
            _ => 0,
        };

        /// <summary>
        /// A world object holding a deposit. The slot is a **pure source** in the terms of §7 - priority 0,
        /// requests nothing, gives everything away - which is what makes it a valid supplier to any building
        /// that asks and an impossible one to compete with. Nothing else about it differs from a warehouse
        /// shelf, and that is what lets the existing haul machinery empty it.
        /// </summary>
        public static Entity PlaceNode(
            EntityManager entities,
            int2 cell,
            ObjectKind kind,
            ItemId item,
            int amount)
        {
            Entity node = entities.CreateEntity();

            entities.AddComponentData(node, new CellObject
            {
                Cell = cell,
                Cost = WorldObjectCatalog.Cost(kind),
                Kind = kind,
            });

            entities.AddComponent<ResourceNode>(node);

            entities.AddBuffer<StorageSlot>(node).Add(new StorageSlot
            {
                Item = item,
                Amount = amount,
                Capacity = amount,

                // Never asks for anything, and holds nothing back from anyone who does.
                DeliverInUpTo = 0,
                DeliverOutDownTo = 0,
                Priority = 0,
            });

            return node;
        }
    }
}
