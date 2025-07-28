using System;
using System.Collections.Generic;
using HCore.Extensions;
using HCore.Shapes;
using Unity.Mathematics;
using UnityEngine;

namespace Navigation
{
    public class PathfindingSystem : MonoBehaviour
    {
        [SerializeField] private List<Transform> _borderPoints;
        [SerializeField] private bool _drawConnections;
        [SerializeField] private bool _drawNodes;
        [SerializeField] private bool _drawObstacles;

        private NavMesh _navMesh;

        private void Start()
        {
            var startPoints = new List<Vector2>();
            foreach (var pointTransform in _borderPoints)
            {
                startPoints.Add(pointTransform.position.To2D());
            }
            _navMesh = new NavMesh(startPoints);

            // _ = CheckInsertion();
            _ = CheckRectangle();
        }

        private void OnDestroy()
        {
            _navMesh.Dispose();
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
            var o1 = _navMesh.AddObstacle(new()
            {
                new(new(1, 1), new(3, 1), new(3, 3))
            });
            
            await WaitForClick();
            var o2 = _navMesh.AddObstacle(new()
            {
                new(new(10, 10), new(8, 8), new(10, 8))
            });
            
            await WaitForClick();
            _navMesh.RemoveObstacle(o1);
            
            await WaitForClick();
            _navMesh.RemoveObstacle(o2);
        }
        private async Awaitable CheckRectangle()
        {
            float deg = 0;
            await WaitForClick();
            while (true)
            {
                // var mpos = Camera.main.ScreenToWorldPoint(Input.mousePosition).To2D();
                var mpos = new float2(5, 5);
                // mpos.DrawPoint(Color.magenta, 1);
                // Debug.Log($"{mpos} {deg} {CreateSquareAsTriangles(mpos, 1, deg).ElementsString()}");
                var o1 = _navMesh.AddObstacle(CreateSquareAsTriangles(mpos, 1, deg));

                await Awaitable.NextFrameAsync();
                _navMesh.RemoveObstacle(o1);

                deg += Time.deltaTime * 20;
                Debug.Log(_navMesh.Nodes.Length);
            }
        }
        
        public static List<Triangle> CreateSquareAsTriangles(float2 center, float size, float rotationDegrees)
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

            float2[] worldCorners = new float2[4];
            for (int i = 0; i < 4; i++)
            {
                worldCorners[i] = math.mul(rotationMatrix, localCorners[i]) + center;
            }

            // Create two triangles
            return new List<Triangle>
            {
                new Triangle(worldCorners[0], worldCorners[1], worldCorners[2]),
                new Triangle(worldCorners[0], worldCorners[2], worldCorners[3])
            };
        }
        
        private void OnDrawGizmos()
        {
            if (_navMesh != null)
            {
                if (_drawConnections)
                {
                    _navMesh.DrawConnections();
                }
                if (_drawNodes)
                {
                    _navMesh.DrawNodes();
                }
                if (_drawObstacles)
                {
                    _navMesh.DrawObstacles();
                }
            }
            else
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
    }
}