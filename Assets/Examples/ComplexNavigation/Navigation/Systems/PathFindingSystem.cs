using Navigation;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;

namespace ComplexNavigation
{
    public partial struct PathFindingSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var navMeshSystem = state.World.GetExistingSystemManaged<NavMeshSystem>();
            var entityManager = state.EntityManager;

            // Query all entities with FindPathRequest + PathBuffer
            var query = SystemAPI.QueryBuilder()
                                 .WithAll<FindPathRequest>()
                                 .WithAll<PathBuffer>()
                                 .Build();

            using var entities = query.ToEntityArray(Allocator.TempJob);
            if (entities.Length == 0)
            {
                return;
            }

            var startAndTargetArray = new NativeArray<StartAndTarget>(entities.Length, Allocator.TempJob);
            using var stream = new NativeStream(entities.Length, Allocator.TempJob);

            // Fill startAndTargetArray from entities
            for (int i = 0; i < entities.Length; i++)
            {
                var transform = entityManager.GetComponentData<LocalTransform>(entities[i]);
                var request = entityManager.GetComponentData<FindPathRequest>(entities[i]);
                startAndTargetArray[i] = new StartAndTarget(transform.Position.xy, request.TargetPosition);
            }

            // Schedule the Burst job
            var job = new FindPathsJob<IdAttribute, SamplePathSeeker>
            {
                StartAndTargetEntry = startAndTargetArray,
                NavMesh = navMeshSystem.NavMesh,
                Seeker = new SamplePathSeeker(),
                ResultPaths = stream.AsWriter()
            };

            job.Schedule(entities.Length, 1).Complete();

            // Read back results and write to PathBuffer
            var reader = stream.AsReader();
            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];

                var buffer = entityManager.GetBuffer<PathBuffer>(entity);
                buffer.Clear();

                reader.BeginForEachIndex(i);
                while (reader.RemainingItemCount > 0)
                {
                    var portal = reader.Read<PathPortal>();
                    buffer.Add(new PathBuffer { Portal = portal });
                }

                reader.EndForEachIndex();

                // Rest path index
                entityManager.SetComponentData<PathIndex>(entity, new() { Index = 0 });

                // Disable the request after path is computed
                entityManager.SetComponentEnabled<FindPathRequest>(entity, false);
            }

            // Dispose temporary arrays
            startAndTargetArray.Dispose();
        }
    }
}