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
        [SerializeField] private List<Transform> _obstacles;

        [SerializeField] private float _size;

        [Space]
        [SerializeField] private Transform _pathOrigin;

        [SerializeField] private Transform _pathTarget;

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
            // _ = CheckRectangle();
            _ = UpdateMapObstacles();
            _ = UpdatePath();
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
            var o2 = _navObstacles.AddObstacle(new(2), CreateSquareAsTriangles(new float2(10, 6), 3, 30));
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
            _navObstacles.AddObstacle(new(1), CreateSquareAsTriangles(new float2(10, 6), 3, 30));
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
                var o1 = _navObstacles.AddObstacle(new(2), CreateSquareAsTriangles(mpos, size / 2, deg));
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

        private async Awaitable UpdateMapObstacles()
        {
            var exist = new List<int>();
            while (true)
            {
                foreach (var id in exist)
                {
                    _navObstacles.RemoveObstacle(id);
                }

                do
                {
                    await Awaitable.NextFrameAsync();
                } while (!Input.GetKeyDown(KeyCode.U));

                foreach (var obs in _obstacles)
                {
                    var p = obs.position;
                    var s = obs.lossyScale / 2f;
                    using var list = new NativeList<float2>(Allocator.Temp)
                    {
                        new float2(p.x - s.x, p.y - s.y),
                        new float2(p.x - s.x, p.y + s.y),
                        new float2(p.x + s.x, p.y + s.y),
                        new float2(p.x + s.x, p.y - s.y)
                    };
                    PolygonUtils.ExpandPolygon(list, _size);
                    var id = _navObstacles.AddObstacle(list, new(exist.Count + 1));
                    exist.Add(id);
                }

                Debug.Log("Updated obstacles");
                RunUpdate();
            }
        }

        private async Awaitable UpdatePath()
        {
            while (true)
            {
                await WaitForClick();
                FindPath((Vector2)_pathOrigin.position, (Vector2)_pathTarget.position);
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

        private void FindPath(float2 from, float2 to)
        {
            using var resultPath = new NativeList<Portal>(Allocator.Temp);

            if (!_navMesh.TryGetNodeIndex(from, out var fromIndex))
            {
                Debug.LogError($"{from} not found");
                return;
            }

            if (!_navMesh.TryGetNodeIndex(to, out var toIndex))
            {
                Debug.LogError($"{to} not found");
                return;
            }

            Debug.Log($"{from} to {to}");
            var job = new FindPathJob<IdAttribute, SamplePathSeeker>
            {
                StartPos = from,
                StartNodeIndex = fromIndex,
                TargetPos = to,
                TargetNodeIndex = toIndex,
                Nodes = _navMesh.Nodes,
                ResultPath = resultPath
            };
            job.Execute();

            Debug.Log($"Found {resultPath.Length}");

            foreach (Portal p in resultPath)
            {
                Debug.DrawLine(p.Left.To3D(), p.Right.To3D(), Color.green, 5);
            }

            DrawPath(from, to, resultPath, Color.magenta, 10);
        }

        private void DrawPath(float2 origin, float2 target, NativeList<Portal> portals, Color color, int duration)
        {
            if (portals.Length > 0)
            {
                using var path = new NativeList<float2>(Allocator.Temp);
                var newPortals = new NativeArray<Portal>(portals.Length + 2, Allocator.Temp);
                newPortals[0] = new Portal(origin, origin);
                for (int i = 0; i < portals.Length; i++)
                {
                    newPortals[i + 1] = portals[i];
                }

                newPortals[^1] = new Portal(target, target);
                FunnelPath.FromPortals(newPortals, path);
                // Debug.DrawLine(origin.To3D(), path[0].To3D(), color, duration);
                for (var index = 0; index < path.Length - 1; index++)
                {
                    var p = path[index];
                    var p2 = path[index + 1];
                    Debug.DrawLine(p.To3D(), p2.To3D(), color, duration);
                }

                // Debug.DrawLine(target.To3D(), portals[^1].Center.To3D(), color, duration);
                newPortals.Dispose();
            }
            else
            {
                Debug.DrawLine(origin.To3D(), target.To3D(), color, duration);
            }
        }

        private static List<float2> CreateSquareAsTriangles(float2 center, float size, float rotationDegrees)
        {
            float halfSize = size / 2f;
            float radians = math.radians(rotationDegrees);

            // Local space corners of square (counter-clockwise)
            float2[] localCorners = new float2[]
            {
                new float2(-halfSize, -halfSize),
                new float2(halfSize, -halfSize),
                new float2(halfSize, halfSize),
                new float2(-halfSize, halfSize)
            };

            float2x2 rotationMatrix = new float2x2(
                new float2(math.cos(radians), -math.sin(radians)),
                new float2(math.sin(radians), math.cos(radians))
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