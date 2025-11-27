using andywiecko.BurstTriangulator;
using CustomNativeCollections;
using Navigation;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace ComplexNavigation
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class NavMeshSystem : SystemBase
    {
        public NavObstacles<IdAttribute> NavObstacles;
        public NavMesh<IdAttribute> NavMesh;
        public NativeHashMap<Entity, int> EntityToObstacle;

        protected override void OnCreate()
        {
            base.OnCreate();

            NavObstacles = new(1, 4096);
            NavMesh = new(1, 4096);
            EntityToObstacle = new(4096, Allocator.Persistent);

            AddInitNodes();
        }

        protected override void OnDestroy()
        {
            NavObstacles.Dispose();
            NavMesh.Dispose();
            EntityToObstacle.Dispose();

            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
        }

        private void AddInitNodes()
        {
            var positions = new NativeArray<float2>(new float2[]
            {
                new float2(-13, -8),
                new float2(13, -8),
                new float2(13, 8),
                new float2(-13, 8),
            }, Allocator.TempJob);

            using var triangulator = new Triangulator<float2>(Allocator.Persistent)
            {
                Input =
                {
                    Positions = positions,
                },
            };
            triangulator.Run();

            var triangles = triangulator.Output.Triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var triangle = new Triangle(
                    positions[triangles[i]],
                    positions[triangles[i + 1]],
                    positions[triangles[i + 2]]
                );
                NavMesh.AddNode(new(triangle, new()));
            }

            positions.Dispose();
        }
    }

    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(NavMeshSystem))]
    [BurstCompile]
    public partial struct NavMeshUpdateSystem : ISystem
    {
        private EntityQuery _updateQuery;

        public void OnCreate(ref SystemState state)
        {
            _updateQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<UpdateNavigation>(),
                ComponentType.ReadOnly<ObstacleVertexBuffer>()
            );
        }

        public void OnDestroy(ref SystemState state)
        {
        }

        public void OnUpdate(ref SystemState state)
        {
            if (_updateQuery.IsEmpty)
            {
                // If no updates, just draw edges once.
                return;
            }

            using var updateRequests = new NativeList<UpdateNavObstacleJob.UpdateData>(Allocator.TempJob);
            using var updatedVertices = new NativeList<float2>(Allocator.TempJob);

            foreach (var (updateNavigation, entity)
                     in SystemAPI.Query<RefRO<UpdateNavigation>>().WithEntityAccess())
            {
                SystemAPI.SetComponentEnabled<UpdateNavigation>(entity, false);

                var vertexBuffer = SystemAPI.GetBuffer<ObstacleVertexBuffer>(entity);
                int startIndex = updatedVertices.Length;
                foreach (var vert in vertexBuffer)
                {
                    updatedVertices.Add(vert.Vertex);
                }

                int endIndex = updatedVertices.Length - 1;

                updateRequests.Add(new UpdateNavObstacleJob.UpdateData
                {
                    Entity = entity,
                    Update = updateNavigation.ValueRO,
                    StartVertexIndex = startIndex,
                    EndVertexIndex = endIndex
                });
            }

            var navMeshSystemManaged = state.World.GetExistingSystemManaged<NavMeshSystem>();

            state.Dependency = new UpdateNavObstacleJob
            {
                NavObstacles = navMeshSystemManaged.NavObstacles,
                EntityToObstacle = navMeshSystemManaged.EntityToObstacle,

                Request = updateRequests,
                Vertices = updatedVertices,
                ExpandSize = .1f,
            }.Schedule(state.Dependency);

            state.Dependency.Complete();
            
            state.Dependency = new UpdateNavMeshJob<IdAttribute>
            {
                NavMesh = navMeshSystemManaged.NavMesh,

                NavObstacles = navMeshSystemManaged.NavObstacles,
                Request = updateRequests,
                Vertices = updatedVertices,
                ExpandSize = .1f,
            }.Schedule(state.Dependency);

            state.Dependency.Complete();
        }
    }

    [BurstCompile]
    public struct UpdateNavObstacleJob : IJob
    {
        public NavObstacles<IdAttribute> NavObstacles;
        public NativeHashMap<Entity, int> EntityToObstacle;

        [ReadOnly] public NativeList<UpdateData> Request;
        [ReadOnly] public NativeList<float2> Vertices;
        [ReadOnly] public float ExpandSize;

        public void Execute()
        {
            using var obstacleVertices = new NativeList<float2>(16, Allocator.Temp);
            foreach (var request in Request)
            {
                obstacleVertices.Clear();
                for (int i = request.StartVertexIndex; i <= request.EndVertexIndex; i++)
                {
                    obstacleVertices.Add(Vertices[i]);
                }

                if (ExpandSize > 0)
                {
                    PolygonUtils.ExpandPolygon(obstacleVertices, -ExpandSize);
                }

                if (!EntityToObstacle.TryGetValue(request.Entity, out var currentId))
                {
                    currentId = -1;
                }

                switch (request.Update.Type)
                {
                    case UpdateNavigation.UpdateType.Add:
                    {
                        if (currentId >= 0)
                        {
                            Debug.LogWarning("Attempted to add obstacle that already exists");
                            break;
                        }

                        var newId = NavObstacles.AddObstacle(obstacleVertices, new(1));
                        EntityToObstacle[request.Entity] = newId;
                        break;
                    }
                    case UpdateNavigation.UpdateType.Update:
                    {
                        if (currentId >= 0)
                        {
                            NavObstacles.RemoveObstacle(currentId);
                        }

                        var newId = NavObstacles.AddObstacle(obstacleVertices, new(1));
                        EntityToObstacle[request.Entity] = newId;
                        break;
                    }
                    case UpdateNavigation.UpdateType.Remove:
                    {
                        if (currentId < 0)
                        {
                            Debug.LogWarning("Attempted to remove obstacle that doesn't exist");
                            break;
                        }

                        NavObstacles.RemoveObstacle(currentId);
                        EntityToObstacle.Remove(request.Entity);
                        break;
                    }
                }
            }
        }

        public struct UpdateData
        {
            public Entity Entity;
            public UpdateNavigation Update;
            public int StartVertexIndex;
            public int EndVertexIndex;
        }
    }

    [BurstCompile]
    public struct UpdateNavMeshJob<T> : IJob where T : unmanaged, INodeAttributes<T>
    {
        private const float CHUNK_SIZE = 10;
        private const float CHUNK_SIZE_INV = 1f / CHUNK_SIZE;

        public NavMesh<T> NavMesh;

        [ReadOnly] public NavObstacles<T> NavObstacles;
        [ReadOnly] public NativeList<UpdateNavObstacleJob.UpdateData> Request;
        [ReadOnly] public NativeList<float2> Vertices;
        [ReadOnly] public float ExpandSize;

        public void Execute()
        {
            using var areasToUpdate = new NativeHashSet<int2>(Vertices.Length, Allocator.Temp);
            foreach (var request in Request)
            {
                float2 min = Vertices[request.EndVertexIndex];
                float2 max = Vertices[request.EndVertexIndex];
                for (int i = request.StartVertexIndex; i < request.EndVertexIndex; i++)
                {
                    var vertex = Vertices[i];
                    min = math.min(min, vertex);
                    max = math.max(max, vertex);
                }

                min -= ExpandSize;
                max += ExpandSize;
                int2 minC = SpatialHashMethods.CellOf(min, CHUNK_SIZE_INV);
                int2 maxC = SpatialHashMethods.CellOf(max, CHUNK_SIZE_INV);

                for (int y = minC.y; y <= maxC.y; y++)
                for (int x = minC.x; x <= maxC.x; x++)
                {
                    areasToUpdate.Add(new(x, y));
                }
            }

            foreach (var area in areasToUpdate)
            {
                float2 min = SpatialHashMethods.CellMin(area, CHUNK_SIZE_INV);
                float2 max = min + CHUNK_SIZE;
                // Debug.Log($"Update {min} {max}");
                new NavMeshUpdateJob<T>
                {
                    NavMesh = NavMesh,
                    NavObstacles = NavObstacles,
                    UpdateMin = min,
                    UpdateMax = max,
                }.Execute();
            }
        }
    }
}