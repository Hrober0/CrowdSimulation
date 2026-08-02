using Unity.Collections;
using Unity.Entities;

namespace GridNav
{
    /// <summary>
    /// Allocates the grid from the baked <see cref="GridSettings"/> and owns its lifetime. Runs once: the
    /// system disables itself as soon as the <see cref="GridWorld"/> singleton exists.
    /// </summary>
    [UpdateInGroup(typeof(GridUpdateGroup), OrderFirst = true)]
    public partial struct GridMapSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridSettings>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (SystemAPI.HasSingleton<GridWorld>())
            {
                state.Enabled = false;
                return;
            }

            GridSettings settings = SystemAPI.GetSingleton<GridSettings>();
            state.EntityManager.CreateSingleton(new GridWorld
            {
                Map = new GridMap(settings.MinCell, settings.ChunkCount, Allocator.Persistent),
                Edits = new GridEditQueue(Allocator.Persistent),
            }, "GridWorld");

            state.Enabled = false;
        }

        public void OnDestroy(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton(out GridWorld gridWorld))
            {
                return;
            }

            gridWorld.Map.Dispose();
            gridWorld.Edits.Dispose();
        }
    }
}
