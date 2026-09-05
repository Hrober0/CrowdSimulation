using GridNav;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// Puts a planted thing on the ground (design §14 step 11).
    ///
    /// What it makes is an ordinary <see cref="CellObject"/> carrying a <see cref="ResourceNode"/> and its
    /// yield - the same entity `RtsResources` builds when the map is laid out, and the same one a lumber camp
    /// fells. There is no such thing as a *planted* tree as far as the rest of the game is concerned, which is
    /// what makes a grove behave exactly like a wood that was always there.
    ///
    /// The grid finds out on the next grid phase, through `CellObjectRegistrationSystem` and the one writer
    /// (§13.2 invariant 1), exactly as a placed building does.
    /// </summary>
    [UpdateInGroup(typeof(RtsAgentGroup))]
    [UpdateAfter(typeof(InteractionSystem))]
    public partial struct PlantingSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.EntityManager.CreateSingleton(new PlantQueue(Allocator.Persistent), "PlantQueue");
            state.RequireForUpdate<GridWorld>();
        }

        public void OnDestroy(ref SystemState state)
        {
            if (SystemAPI.TryGetSingleton(out PlantQueue queue))
            {
                queue.Dispose();
            }
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            PlantQueue queue = SystemAPI.GetSingleton<PlantQueue>();
            if (queue.Count == 0)
            {
                return;
            }

            GridMap map = SystemAPI.GetSingleton<GridWorld>().Map;
            EntityManager entities = state.EntityManager;

            while (queue.TryDequeue(out PlantRequest request))
            {
                // Asked again at the moment it would appear, because a planting takes time and the world
                // moves while a worker walks: a building can have been put down on the spot, or somebody
                // else's sapling can already be standing there. Planting anyway would bury one under the
                // other with no way to tell them apart afterwards.
                if (map.GetFlags(request.Cell) != CellFlags.None || !map.IsPassable(request.Cell))
                {
                    continue;
                }

                Plant(entities, request);
            }
        }

        private static void Plant(EntityManager entities, in PlantRequest request)
        {
            Entity planted = entities.CreateEntity();

            entities.AddComponentData(planted, new CellObject
            {
                Cell = request.Cell,
                Cost = request.What.PlantCost,
                Kind = request.What.Plants,
            });

            entities.AddComponent<ResourceNode>(planted);

            // A pure source, like every other node: it never asks for anything and gives everything away.
            entities.AddBuffer<StorageSlot>(planted).Add(new StorageSlot
            {
                Item = request.What.Yields,
                Amount = request.What.YieldAmount,
                Capacity = request.What.YieldAmount,
                DeliverInUpTo = 0,
                DeliverOutDownTo = 0,
                Priority = 0,
            });
        }
    }
}
