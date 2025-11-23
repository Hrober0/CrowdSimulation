using Navigation;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace ComplexNavigation
{
    public class ObstacleRectangleAuthoring : MonoBehaviour
    {
        public class Baker : Baker<ObstacleRectangleAuthoring>
        {
            public override void Bake(ObstacleRectangleAuthoring authoring)
            {
                float2[] vertexes = DebugUtils.GetRectangleFromTransform(authoring.transform);

                float2 min = vertexes[0];
                float2 max = vertexes[0];
                for (int i = 1; i < vertexes.Length; i++)
                {
                    min = math.min(min, vertexes[i]);
                    max = math.max(max, vertexes[i]);
                }

                var entity = GetEntity(TransformUsageFlags.Dynamic);

                DynamicBuffer<ObstacleVertexBuffer> vertexBuffer = AddBuffer<ObstacleVertexBuffer>(entity);
                foreach (float2 vertex in vertexes)
                {
                    vertexBuffer.Add(new() { Vertex = vertex });
                }
                
                AddComponent<UpdateAvoidanceObstacle>(entity);
            }
        }
    }
}