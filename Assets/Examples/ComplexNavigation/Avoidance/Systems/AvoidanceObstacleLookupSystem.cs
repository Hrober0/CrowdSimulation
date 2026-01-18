using Avoidance;
using CustomNativeCollections;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace ComplexNavigation
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class AvoidanceObstacleLookupSystem : SystemBase
    {
        public NativeSpatialLookup<ObstacleVertex> SpatialLookup;

        protected override void OnCreate()
        {
            base.OnCreate();

            SpatialLookup = new(
                capacity: 4096,
                cellSize: 1f,
                Allocator.Persistent
            );
        }

        protected override void OnDestroy()
        {
            if (SpatialLookup.IsCreated)
            {
                SpatialLookup.Dispose();
            }

            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
        }
    }

    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(AvoidanceObstacleLookupSystem))]
    [BurstCompile]
    public partial struct AvoidanceObstacleLookupSystemUpdateSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            bool requireUpdate = false;
            foreach (var (_, entity) in SystemAPI.Query<RefRO<UpdateAvoidanceObstacle>>().WithEntityAccess())
            {
                SystemAPI.SetComponentEnabled<UpdateAvoidanceObstacle>(entity, false);
                requireUpdate = true;
            }

            if (!requireUpdate)
            {
                return;
            }

            NativeSpatialLookup<ObstacleVertex>
                lookup = state.World.GetExistingSystemManaged<AvoidanceObstacleLookupSystem>().SpatialLookup;
            lookup.Clear();

            JobHandle addVerticesHandle = new AddObstacleVerticesToLookupJob
            {
                Vertices = lookup.Values,
            }.Schedule(state.Dependency);
            addVerticesHandle.Complete();
            
            JobHandle buildLookupHandle = new BuildObstacleVerticesLookupJob
            {
                Lookup = lookup.Lookup.AsParallelWriter(),
                Vertices = lookup.Values,
                ChunkSizeMultiplier = lookup.InvCellSize,
            }.Schedule(lookup.Values.Length, 16, addVerticesHandle);
            state.Dependency = buildLookupHandle;
            
            buildLookupHandle.Complete();
        }
    }

    [BurstCompile]
    public partial struct AddObstacleVerticesToLookupJob : IJobEntity
    {
        public NativeList<ObstacleVertex> Vertices;

        public void Execute(in Entity entity, in DynamicBuffer<ObstacleVertexBuffer> vertexBuffer)
        {
            if (vertexBuffer.Length < 2)
            {
                return;
            }

            int firstVertexIndex = Vertices.Length;

            for (int i = 0; i < vertexBuffer.Length; i++)
            {
                var obstacleVertex = new ObstacleVertex()
                {
                    ObjectId = entity.Index,
                    VertexIndex = Vertices.Length,
                };

                obstacleVertex.Next = i < vertexBuffer.Length - 1 ? obstacleVertex.VertexIndex + 1 : firstVertexIndex;
                obstacleVertex.Previous = i > 0 ? obstacleVertex.VertexIndex - 1 : firstVertexIndex + vertexBuffer.Length - 1;

                float2 previousVertex = vertexBuffer[i == 0 ? vertexBuffer.Length - 1 : i - 1].Vertex;
                float2 currentVertex = vertexBuffer[i].Vertex;
                float2 nextVertex = vertexBuffer[i == vertexBuffer.Length - 1 ? 0 : i + 1].Vertex;

                obstacleVertex.Point = currentVertex;
                obstacleVertex.Direction = math.normalize(nextVertex - currentVertex);

                if (vertexBuffer.Length == 2)
                {
                    obstacleVertex.Convex = true;
                }
                else
                {
                    float t = RVOMath.LeftOf(
                        previousVertex,
                        currentVertex,
                        nextVertex);
                    obstacleVertex.Convex = (t >= 0f);
                }

                Vertices.Add(obstacleVertex);
            }
        }
    }
}