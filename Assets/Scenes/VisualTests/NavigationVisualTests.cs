using System.Collections.Generic;
using System.Linq;
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
        [SerializeField] private bool _drawNodesCenters;

        [Space]
        [SerializeField] private bool _drawObstacleTriangle;
        [SerializeField] private bool _drawObstacleBorder;

        private NavMesh<IdAttribute> _navMesh;
        private NavObstacles<IdAttribute> _navObstacles;

        private void Start()
        {
            _navMesh = new(1);
            _navObstacles = new(1);

            _ = Test();
        }

        private void OnDestroy()
        {
            _navMesh.Dispose();
            _navObstacles.Dispose();
        }

        private async Awaitable Test()
        {
            if (_borderPoints.Count > 2)
            {
                await AddInitNodes(false);
            }

            // _ = SlowAddition();
            // _ = CheckRectangle();
            _ = UpdateMapObstacles();
            // _ = UpdatePath();
            _ = FollowPath();
        }

        private async Awaitable WaitForClick(KeyCode key = KeyCode.Space)
        {
            do
            {
                await Awaitable.NextFrameAsync();
            } while (!Input.GetKeyDown(key));
        }

        private async Awaitable SlowAddition()
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
            await WaitForClick();
            
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
            await WaitForClick(KeyCode.U);
            
            var exist = new List<int>();
            while (true)
            {
                foreach (var obst in _obstacles)
                {
                    if (obst == null || !obst.gameObject.activeInHierarchy)
                    {
                        continue;
                    }
                    
                    using var list = new NativeList<float2>(Allocator.Temp);
                    foreach (var p in GetRectangleFromTransform(obst))
                    {
                        list.Add(p);
                    }

                    PolygonUtils.ExpandPolygon(list, _size);
                    var id = _navObstacles.AddObstacle(list, new(exist.Count + 1));
                    exist.Add(id);
                }

                Debug.Log("Updated obstacles");
                RunUpdate();
                Debug.Log($"{_navMesh.GetCapacityStats()}\n{_navObstacles.GetCapacityStats()}");
                
                var pointsSize = Mathf.Max(_size * 10, 1) * Vector3.one;
                _pathOrigin.localScale = pointsSize;
                _pathTarget.localScale = pointsSize;

                await WaitForClick(KeyCode.U);

                exist.Reverse();
                foreach (var id in exist)
                {
                    _navObstacles.RemoveObstacle(id);
                }
                exist.Clear();
            }
        }

        private async Awaitable UpdatePath()
        {
            while (true)
            {
                await WaitForClick();
                float2 from = (Vector2)_pathOrigin.position;
                float2 to = (Vector2)_pathTarget.position;
                using var resultPath = FindPath(from, to);
                if (_drawNodes)
                {
                    foreach (Portal p in resultPath)
                    {
                        Debug.DrawLine(p.Left.To3D(), p.Right.To3D(), Color.yellow);
                    }
                }

                DrawPath(from, to, resultPath, Color.green, 5);
            }
        }
        
        private async Awaitable FollowPath()
        {
            while (true)
            {
                await WaitForClick();
                await Awaitable.NextFrameAsync();
                
                while (!Input.GetKeyDown(KeyCode.Space))
                {
                    await Awaitable.NextFrameAsync();
                    
                    var seaker = (float2)(Vector2)_pathOrigin.position;
                    var target = (float2)Camera.main.ScreenToWorldPoint(Input.mousePosition).To2D();
                    if (math.lengthsq(target - seaker) < 0.01f)
                    {
                        continue;
                    }
                    
                    using var portals = FindPath(seaker, target);
                    using var path = new NativeList<float2>(Allocator.Temp);
                    FunnelPath.FromPortals(seaker, target, portals.AsArray(), path);
                    var closeTarget = path.Length > 1 ? path[1] : target;
                    _pathOrigin.position += (Vector3)(Vector2)math.normalize(closeTarget - seaker) * Time.deltaTime * 2;
                    
                    if (_drawNodes)
                    {
                        foreach (Portal p in portals)
                        {
                            Debug.DrawLine(p.Left.To3D(), p.Right.To3D(), Color.green);
                        }
                    }
                }
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

        private NativeList<Portal> FindPath(float2 from, float2 to)
        {
            var resultPath = new NativeList<Portal>(Allocator.Temp);

            if (!_navMesh.TryGetNodeIndex(from, out var fromIndex))
            {
                Debug.LogError($"{from} not found");
                return new();
            }

            if (!_navMesh.TryGetNodeIndex(to, out var toIndex))
            {
                Debug.LogError($"{to} not found");
                return new();
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

            return resultPath;
        }

        private void DrawPath(float2 origin, float2 target, NativeList<Portal> portals, Color color, float duration)
        {
            if (portals.Length > 0)
            {
                using var path = new NativeList<float2>(Allocator.Temp);
                FunnelPath.FromPortals(origin, target, portals.AsArray(), path);
                for (var index = 0; index < path.Length - 1; index++)
                {
                    var p = path[index];
                    var p2 = path[index + 1];
                    Debug.DrawLine(p.To3D(), p2.To3D(), color, duration);
                }
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
                    _navMesh.DrawNodes(_drawNodesCenters);
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
            else
            {
                if (_drawNodes)
                {
                    Gizmos.color = Color.red;
                    var startPoints = new List<Vector2>();
                    foreach (var pointTransform in _borderPoints)
                    {
                        startPoints.Add(pointTransform.position.To2D());
                    }

                    IOutline.DrawBorderGizmos(startPoints);
                }

                if (_drawObstacleBorder)
                {
                    foreach (var obst in _obstacles)
                    {
                        if (obst != null && obst.gameObject.activeInHierarchy)
                        {
                            GetRectangleFromTransform(obst).DrawLoop(obst.gameObject.IsSelected() ? Color.green : Color.red);
                        }
                    }
                }
            }
        }

        private async Awaitable AddInitNodes(bool wait)
        {
            var positions = new NativeArray<float2>(_borderPoints.Count, Allocator.Persistent);
            for (var index = 0; index < _borderPoints.Count; index++)
            {
                Transform pointTransform = _borderPoints[index];
                positions[index] = pointTransform.position.To2D();
            }

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
                if (wait)
                {
                    await WaitForClick();
                }
                
                var triangle = new Triangle(
                    positions[triangles[i]],
                    positions[triangles[i + 1]],
                    positions[triangles[i + 2]]
                );
                _navMesh.AddNode(new(triangle, new()));
            }

            positions.Dispose();
        }

        private float2[] GetRectangleFromTransform(Transform transform)
        {
            var p = (float2)(Vector2)transform.position;
            var halfSize = (float2)(Vector2)transform.lossyScale * 0.5f;

            // rotation in radians
            float rad = math.radians(transform.rotation.eulerAngles.z);
            float cos = math.cos(rad);
            float sin = math.sin(rad);

            // define local rectangle corners (unrotated, relative to center)
            float2[] localCorners =
            {
                new float2(-halfSize.x, -halfSize.y),
                new float2(-halfSize.x, halfSize.y),
                new float2(halfSize.x, halfSize.y),
                new float2(halfSize.x, -halfSize.y),
            };

            // rotate and translate to world space
            var result = new float2[4];
            for (int i = 0; i < 4; i++)
            {
                float2 c = localCorners[i];
                float2 rotated = new(
                    c.x * cos - c.y * sin,
                    c.x * sin + c.y * cos
                );
                result[i] = p + rotated;
            }

            return result;
        }
    }
}