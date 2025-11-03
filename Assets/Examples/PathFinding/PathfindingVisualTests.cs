using System;
using HCore.Extensions;
using Navigation;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace PathFindingTest
{
    public class PathfindingVisualTests : MonoBehaviour
    {
        public enum TestType
        {
            UpdatePath,
            FollowPath,
            FollowPathToPortal,
        }
        
        [SerializeField] private NavMeshVisualTests _meshVisual;
        
        [Space]
        [SerializeField] private Transform _pathOrigin;
        [SerializeField] private Transform _pathTarget;

        [Space]
        [SerializeField] private bool _drawPortals;
        
        [Space]
        [SerializeField] private TestType _testType = TestType.FollowPathToPortal;
        
        private void Start()
        {
            _ = RunTest();
        }

        private async Awaitable RunTest()
        {
            switch (_testType)
            {
                case TestType.UpdatePath:
                    _ = UpdatePath();
                    break;
                case TestType.FollowPath:
                    _ = FollowPath();
                    break;
                case TestType.FollowPathToPortal:
                    _ = FollowPathToPortal();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        
        private async Awaitable UpdatePath()
        {
            while (true)
            {
                await DebugUtils.WaitForClick();
                float2 from = (Vector2)_pathOrigin.position;
                float2 to = (Vector2)_pathTarget.position;
                using var resultPath = FindPath(from, to);
                if (_drawPortals)
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
                await DebugUtils.WaitForClick();
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
                    PathFinding.FunnelPath(seaker, target, portals.AsArray(), path);
                    var closeTarget = path.Length > 1 ? path[1] : target;
                    _pathOrigin.position += (Vector3)(Vector2)math.normalize(closeTarget - seaker) * Time.deltaTime * 2;
                    
                    if (_drawPortals)
                    {
                        foreach (Portal p in portals)
                        {
                            Debug.DrawLine(p.Left.To3D(), p.Right.To3D(), Color.green);
                        }
                    }
                }
            }
        }
        
        private async Awaitable FollowPathToPortal()
        {
            await DebugUtils.WaitForClick();
            while (true)
            {
                await Awaitable.NextFrameAsync();
                
                using var savedPortals = new NativeList<Portal>(Allocator.Persistent);
                
                while (true)
                {
                    await Awaitable.NextFrameAsync();
                    
                    var seaker = (float2)(Vector2)_pathOrigin.position;
                    var target = (float2)Camera.main.ScreenToWorldPoint(Input.mousePosition).To2D();
                    if (math.lengthsq(target - seaker) < 0.01f)
                    {
                        continue;
                    }
                    
                    using var portals = FindPath(seaker, target);
                    portals.Add(new(target, target));
                    float2 direction;
                    if (portals.Length > 1)
                    {
                        direction = PathFinding.ComputeGuidanceVector(seaker, portals[0], portals[1].Center);
                    }
                    else
                    {
                        direction = math.normalize(target - seaker);
                    }
                    // DebugUtils.Draw(seaker, seaker + direction, Color.yellow);
                    _pathOrigin.position += (Vector3)(Vector2)direction * Time.deltaTime * 2;
                    
                    if (_drawPortals)
                    {
                        foreach (Portal p in portals)
                        {
                            Debug.DrawLine(p.Left.To3D(), p.Right.To3D(), Color.green);
                        }
                    }

                    if (Input.GetKeyDown(KeyCode.Space))
                    {
                        savedPortals.Clear();
                        savedPortals.AddRange(portals.AsArray());
                        await Awaitable.NextFrameAsync();
                        break;
                    }
                }

                while (true)
                {
                    await Awaitable.NextFrameAsync();
                    
                    if (_drawPortals)
                    {
                        foreach (Portal p in savedPortals)
                        {
                            Debug.DrawLine(p.Left.To3D(), p.Right.To3D(), Color.green);
                        }
                    }
                    
                    if (Input.GetKeyDown(KeyCode.Space))
                    {
                        await Awaitable.NextFrameAsync();
                        break;
                    }
                }
                
            }
        }
        
        private NativeList<Portal> FindPath(float2 from, float2 to)
        {
            var resultPath = new NativeList<Portal>(Allocator.TempJob);

            Debug.Log($"{from} to {to}");
            var job = new FindPathJob<IdAttribute, SamplePathSeeker>
            {
                StartPosition = from,
                TargetPosition = to,
                NavMesh = _meshVisual.NavMesh,
                ResultPath = resultPath
            };
            job.Run();

            Debug.Log($"Found {resultPath.Length}");

            return resultPath;
        }

        private void DrawPath(float2 origin, float2 target, NativeList<Portal> portals, Color color, float duration)
        {
            if (portals.Length > 0)
            {
                using var path = new NativeList<float2>(Allocator.Temp);
                PathFinding.FunnelPath(origin, target, portals.AsArray(), path);
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
    }
}