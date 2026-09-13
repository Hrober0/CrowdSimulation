using System;
using GridNav;
using Unity.Entities;
using Unity.Mathematics;

namespace Tests.EditorTests.GridNavTests
{
    /// <summary>
    /// A world with the grid and the gate graph wired up in frame order, so a test can queue edits, tick,
    /// and look at the result.
    /// </summary>
    internal sealed class GridNavTestWorld : IDisposable
    {
        private readonly SystemHandle _gridMapSystem;
        private readonly SystemHandle _gridApplySystem;
        private readonly SystemHandle _gateGraphSystem;
        private readonly SystemHandle _flowFieldSystem;

        public GridNavTestWorld(int sizeInCells)
        {
            World = new World("GridNavTests");

            _gridMapSystem = World.CreateSystem<GridMapSystem>();
            _gridApplySystem = World.CreateSystem<GridApplySystem>();
            _gateGraphSystem = World.CreateSystem<ChunkGateGraphSystem>();
            _flowFieldSystem = World.CreateSystem<FlowFieldCacheSystem>();

            World.EntityManager.CreateSingleton(
                GridSettings.FromCells(new int2(sizeInCells, sizeInCells), centerOnOrigin: true)
            );
            _gridMapSystem.Update(World.Unmanaged);
        }

        public World World { get; }

        public GridWorld Grid => GetSingleton<GridWorld>();

        /// <summary>The civilian gate graph. There is one per traversal in play now (§14.4).</summary>
        public ChunkGateGraph Graph => GraphFor(default);

        public ChunkGateGraph GraphFor(Traversal traversal)
        {
            using EntityQuery query = World.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ChunkGateGraph>());

            using var graphs = query.ToComponentDataArray<ChunkGateGraph>(Unity.Collections.Allocator.Temp);
            foreach (ChunkGateGraph graph in graphs)
            {
                if (graph.Traversal.Equals(traversal))
                {
                    return graph;
                }
            }

            return default;
        }

        /// <summary>
        /// Asks for a gate graph for a traversal that can breach. One tick later it exists (§14.4).
        /// </summary>
        public void RequestGraph(Traversal traversal) =>
            GetSingleton<GateGraphRequests>().Request(traversal);

        public FlowFieldCache Fields => GetSingleton<FlowFieldCache>();

        public GridMap Map => Grid.Map;

        public void Enqueue(GridEdit edit) => Grid.Edits.Enqueue(edit);

        /// <summary>The grid write phase, then the navigation phase - the order the real groups run in.</summary>
        public void Tick()
        {
            _gridApplySystem.Update(World.Unmanaged);
            _gateGraphSystem.Update(World.Unmanaged);
            _flowFieldSystem.Update(World.Unmanaged);
            World.EntityManager.CompleteAllTrackedJobs();
        }

        private T GetSingleton<T>() where T : unmanaged, IComponentData
        {
            using EntityQuery query = World.EntityManager.CreateEntityQuery(typeof(T));
            return query.GetSingleton<T>();
        }

        public void Dispose() => World.Dispose();
    }
}
