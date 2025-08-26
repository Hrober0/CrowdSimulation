using System.Collections.Generic;
using andywiecko.BurstTriangulator;
using HCore.Extensions;
using HCore.Shapes;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Navigation
{
    public class PathfindingSystem : MonoBehaviour
    {
        [SerializeField] private List<Transform> _borderPoints;
        
        [Space]
        [SerializeField] private bool _drawConnections;
        [SerializeField] private bool _drawNodes;

        [Space]
        [SerializeField] private bool _drawObstacleTriangle;
        [SerializeField] private bool _drawObstacleBorder;
        
        private NavMesh<IdAttribute> _navMesh;
        private NavObstacles<IdAttribute> _navObstacles;

        private void Start()
        {
            _navMesh = new(1);
            _navObstacles = new(1);

            if (_borderPoints.Count > 2)
            {
                AddInitNodes();
            }

            // _ = CheckInsertion();
            _ = CheckRectangle();
        }

        private void OnDestroy()
        {
            _navMesh.Dispose();
            _navObstacles.Dispose();
        }

        private async Awaitable WaitForClick()
        {
            do
            {
                await Awaitable.NextFrameAsync();
            } while (!Input.GetKeyDown(KeyCode.Space));
        }
        private async Awaitable CheckInsertion()
        {
            await WaitForClick();
            var o1 = _navObstacles.AddObstacle(new(1), new(1, 1), new(3, 1), new(3, 3));
            RunUpdate();
            
            await WaitForClick();
            var o2 = _navObstacles.AddObstacle(new(2),CreateSquareAsTriangles( new float2(10, 6), 3, 30));
            RunUpdate();
            
            await WaitForClick();
            _navObstacles.RemoveObstacle(o1);
            RunUpdate();
            
            await WaitForClick();
            _navObstacles.RemoveObstacle(o2);
            RunUpdate();
        }
        
        private async Awaitable CheckRectangle()
        {
            _navObstacles.AddObstacle(new(1),CreateSquareAsTriangles( new float2(10, 6), 3, 30));
            RunUpdate();
            
            float deg = 0;
            await WaitForClick();
            while (true)
            {
                var mpos = Camera.main.ScreenToWorldPoint(Input.mousePosition).To2D();
                // var mpos = new float2(5, 5);
                // mpos.DrawPoint(Color.magenta, 1);
                // Debug.Log($"{mpos} {deg} {CreateSquareAsTriangles(mpos, 1, deg).ElementsString()}");

                var size = 2f;
                var addMin = new float2(mpos.x - size, mpos.y - size);
                var addMax = new float2(mpos.x + size, mpos.y + size);
                var o1 = _navObstacles.AddObstacle(new(2),CreateSquareAsTriangles(mpos, size / 2, deg));
                RunUpdate(addMin, addMax);
                
                // await WaitForClick();
                
                await Awaitable.NextFrameAsync();
                _navObstacles.RemoveObstacle(o1);
                RunUpdate(addMin, addMax);
                
                // await WaitForClick();
                
                deg += Time.deltaTime * 20;
                Debug.Log($"{_navMesh.GetCapacityStats()}\n{_navObstacles.GetCapacityStats()}");
            }
        }
        
        private void RunUpdate() => RunUpdate(new float2(0, 0), new float2(20, 20));
        private void RunUpdate(float2 min, float2 max)
        {
            // Debug.Log("Run");
            new NaveMeshUpdateJob<IdAttribute>
            {
                NavMesh = _navMesh,
                NavObstacles = _navObstacles,
                UpdateMin = min,
                UpdateMax = max,
            }.Run();
        }
        
        private static List<float2> CreateSquareAsTriangles(float2 center, float size, float rotationDegrees)
        {
            float halfSize = size / 2f;
            float radians = math.radians(rotationDegrees);

            // Local space corners of square (counter-clockwise)
            float2[] localCorners = new float2[]
            {
                new float2(-halfSize, -halfSize),
                new float2( halfSize, -halfSize),
                new float2( halfSize,  halfSize),
                new float2(-halfSize,  halfSize)
            };

            float2x2 rotationMatrix = new float2x2(
                new float2(math.cos(radians), -math.sin(radians)),
                new float2(math.sin(radians),  math.cos(radians))
            );

            var worldCorners = new List<float2>(4);
            for (int i = 0; i < 4; i++)
            {
                worldCorners.Add(math.mul(rotationMatrix, localCorners[i]) + center);
            }

            return worldCorners;
        }
        
        private void OnDrawGizmos()
        {
            if (_navMesh.IsCreated)
            {
                if (_drawConnections)
                {
                    _navMesh.DrawConnections();
                }
                if (_drawNodes)
                {
                    _navMesh.DrawNodes();
                }
                if (_drawObstacleTriangle)
                {
                    _navObstacles.DrawLookup();
                }
                if (_drawObstacleBorder)
                {
                    _navObstacles.DrawEdges();
                }
            }
            else if (_drawNodes)
            {
                Gizmos.color = Color.red;
                var startPoints = new List<Vector2>();
                foreach (var pointTransform in _borderPoints)
                {
                    startPoints.Add(pointTransform.position.To2D());
                }
                IOutline.DrawBorderGizmos(startPoints);
            }
        }

        private void AddInitNodes()
        {
            var positions = new NativeArray<float2>(_borderPoints.Count, Allocator.TempJob);
            for (var index = 0; index < _borderPoints.Count; index++)
            {
                Transform pointTransform = _borderPoints[index];
                positions[index] = pointTransform.position.To2D();
            }

            using var triangulator = new Triangulator<float2>(Allocator.TempJob)
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
                _navMesh.AddNode(new(triangle, new()));
            }
            
            positions.Dispose();
        }
    }
}