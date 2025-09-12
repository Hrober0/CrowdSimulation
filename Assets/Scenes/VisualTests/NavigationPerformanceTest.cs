using System.Collections.Generic;
using HCore;
using HCore.Extensions;
using Navigation;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace VisualTests
{
    public class NavigationPerformanceTest : MonoBehaviour
    {
        [SerializeField] private TextAsset _jsonFile; 
        [SerializeField] private float _fps = 30f;
        [SerializeField] private float _scale = 0.01f;
        
        [Space]
        [SerializeField] private bool _drawVideoLines = true;
        [SerializeField] private bool _drawNodesLines = true;
        [SerializeField] private bool _drawObstaclesLines = true;
        
        [Space]
        [SerializeField] private Transform _pathOrigin;
        [SerializeField] private Transform _pathTarget;

        private NavMesh<IdAttribute> _navMesh;
        private NavObstacles<IdAttribute> _navObstacles;
        
        // private HashSet<int> invalidFrames = new();
        private int _currentFrame = 0;
        
        private void Start()
        {
            _navMesh = new(10, capacity: 2048);
            _navObstacles = new(5, capacity: 10_000);

            AddInitNodes();
            
            var frames = Decode(_jsonFile.text, _scale);
            _ = PlayVideo(frames);
            
            _ = MovePoint(_pathOrigin, new()
            {
                new(1, -35),
                new(47, -35),
                new(47, -1),
                new(1, -1),
            });
            _ = MovePoint(_pathTarget, new()
            {
                new(47, -1),
                new(1, -1),
                new(1, -35),
                new(47, -35),
            });
            
            _ = UpdatePath();
        }

        private void OnDestroy()
        {
            _navMesh.Dispose();
            _navObstacles.Dispose();
        }

        private List<List<List<Vector2>>> Decode(string csv, float scale)
        {
            var frames = new List<List<List<Vector2>>>();
            
            var lines = csv.Split('\n');
            
            Debug.Log($"Decoding {lines.Length} lines");
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex];
                if (string.IsNullOrWhiteSpace(line))
                {
                    Debug.Log($"{lineIndex + 1}: empty line");
                    continue;
                }

                var frame = new List<List<Vector2>>();
                var objects = line.Trim().Split(';');

                foreach (var objStr in objects)
                {
                    if (string.IsNullOrWhiteSpace(objStr))
                    {
                        continue;
                    }

                    var points = new List<Vector2>();
                    var pointStrs = objStr.Trim().Split(' ');

                    foreach (var pt in pointStrs)
                    {
                        if (string.IsNullOrWhiteSpace(pt))
                        {
                            Debug.LogWarning($"{lineIndex + 1}: Point is empty");
                            continue;
                        }

                        var xy = pt.Split(',');
                        if (xy.Length != 2)
                        {
                            Debug.LogWarning($"{lineIndex + 1}: Point {pt} has invalid format");
                            continue;
                        }

                        if (int.TryParse(xy[0], out int x) &&
                            int.TryParse(xy[1], out int y))
                        {
                            points.Add(new Vector2(x * scale, y * -scale));
                        }
                        else
                        {
                            Debug.LogWarning($"Point {pt} has invalid format");
                        }
                    }

                    frame.Add(points);
                }

                frames.Add(frame);
            }

            Debug.Log($"Decoded {frames.Count} frames");
            return frames;
        }
        
        private async Awaitable PlayVideo(List<List<List<Vector2>>> frames)
        {
            if (_jsonFile == null)
            {
                Debug.LogError("JSON file not assigned in Inspector!");
                return;
            }
            
            var wait = true;

            for (var index = 0; index < frames.Count; index++)
            {
                Debug.Log($"Playing frame {index + 1}/{frames.Count}");
                List<List<Vector2>> frame = frames[index];
                var delay = 1f / _fps;

                _currentFrame = index;
                
                if (_drawVideoLines)
                {
                    DrawFrame(frame, delay);
                }
                
                UpdateObstacles(frame);
                
                Draw(delay);
                
                await Awaitable.WaitForSecondsAsync(delay);
                
                if (Input.GetKey(KeyCode.S))
                {
                    wait = true;
                }
                while (wait)
                {
                    if (_drawVideoLines)
                    {
                        DrawFrame(frame, Time.deltaTime);
                    }
                    Draw(Time.deltaTime);
                    await Awaitable.NextFrameAsync();
                    if (Input.GetKeyDown(KeyCode.Space))
                    {
                        wait = false;
                    }
                }
                // Debug.Log($"{_navMesh.GetCapacityStats()}\n{_navObstacles.GetCapacityStats()}");
            }

            // var s = "";
            // foreach (var f in invalidFrames)
            // {
            //     s += $"{f}, ";
            // }
            // Debug.Log(s);
        }

        private async Awaitable UpdatePath()
        {
            // await Awaitable.WaitForSecondsAsync(.1f);
            while (true)
            {
                float2 origin = (Vector2)_pathOrigin.position;
                float2 target = (Vector2)_pathTarget.position;
                
                // Debug.Log($"{origin} to {target}");
                var portals = new NativeList<Portal>(Allocator.TempJob);
                var job = new FindPathJob<IdAttribute, SamplePathSeeker>
                {
                    StartPosition = origin,
                    TargetPosition = target,
                    NavMesh = _navMesh,
                    ResultPath = portals
                };
                job.Run();
                
                // Debug.Log($"Found {portals.Length}");
                
                var path = new NativeList<float2>(Allocator.Temp);
                PathFinding.FunnelPath(origin, target, portals.AsArray(), path);
                
                // Debug.Log($"FunnelPath {path.Length}");
                float delay = 1f / _fps;

                if (portals.Length > 0 && _currentFrame > 43)
                {
                    for (var index = 0; index < path.Length - 1; index++)
                    {
                        var p = path[index];
                        var p2 = path[index + 1];
                        DebugUtils.Draw(p, p2, Color.green, delay);
                    }
                }
                else
                {
                    DebugUtils.Draw(origin, target, Color.green, delay);
                }
                
                path.Dispose();
                portals.Dispose();
                
                await Awaitable.WaitForSecondsAsync(delay);
                if (Input.GetKey(KeyCode.E))
                {
                    return;
                }
            }
        }

        private async Awaitable MovePoint(Transform point, List<Vector2> targets, float speed = 4f)
        {
            point.position = targets[0];
            while (true)
            {
                foreach (var target in targets)
                {
                    // Debug.Log($"{point} {target}");
                    while (true)
                    {
                        var dif = target - (Vector2)point.position;
                        var move = speed * Time.deltaTime;
                        if (dif.magnitude < move)
                        {
                            break;
                        }
                        point.position += (Vector3)(dif.normalized * move); 
                        await Awaitable.NextFrameAsync();
                    }
                }
                await Awaitable.NextFrameAsync();
            }
        }
        
        private void Draw(float duration)
        {
            if (_drawNodesLines)
            {
                DrawNodes(duration);
            }
            if (_drawObstaclesLines)
            {
                DrawObstacles(duration);
            }
        }

        private void DrawFrame(List<List<Vector2>> frame, float duration)
        {
            foreach (var obj in frame)
            {
                for (int i = 0; i < obj.Count; i++)
                {
                    Debug.DrawLine(obj[i], obj[(i + 1) % obj.Count], Color.black, duration, false);
                }
            }
        }

        private void UpdateObstacles(List<List<Vector2>> frame)
        {
            _navObstacles.Clear();
 
            using var list = new NativeList<float2>(128,Allocator.TempJob);
            foreach (var obj in frame)
            {
                list.Clear();
                foreach (var p in obj)
                {
                    list.Add(p);
                }

                var id = _navObstacles.RunAddObstacle(list, new(1));
                //
                // if (id == -1)
                // {
                //     invalidFrames.Add(_currentFrame);
                // }
            }
            
            // Debug.Log("Run");
            new NavMeshUpdateJob<IdAttribute>
            {
                NavMesh = _navMesh,
                NavObstacles = _navObstacles,
                UpdateMin = new(0f, -36f),
                UpdateMax = new(48f, 0f),
            }.Run();
        }
        
        private void AddInitNodes()
        {
            // _navMesh.AddNode(new(new Triangle(
            //     new(-4, -40),
            //     new(52, -40),
            //     new(52, 4)
            //     ), new()));
            // _navMesh.AddNode(new(new Triangle(
            //     new(-4, -40),
            //     new(52, 4),
            //     new(-4, 4)
            // ), new()));
            
            _navMesh.AddNode(new(new Triangle(
                new(0, -36),
                new(48, -36),
                new(48, 0)
            ), new()));
            _navMesh.AddNode(new(new Triangle(
                new(0, -36),
                new(48, 0),
                new(0, 0)
            ), new()));
        }
        
        public void DrawObstacles(float duration)
        {
            using NativeArray<int> keys = _navObstacles.ObstacleEdges.GetKeyArray(Allocator.Temp);
            foreach (var key in keys)
            {
                float2 center = float2.zero;
                int number = 0;
                var color = ColorUtils.GetColor(key);
                foreach (var edge in _navObstacles.ObstacleEdges.GetValuesForKey(key))
                {
                    DebugUtils.Draw(edge.A, edge.B, color, duration);

                    center += edge.A;
                    center += edge.B;
                    number++;
                }
                
                // (center / (number * 2)).To3D().DrawPoint(color, duration, 0.1f);
            }
        }
        
        public void DrawNodes(float duration)
        {
            foreach (var node in _navMesh.Nodes)
            {
                DebugUtils.Draw(node.CornerA, node.CornerB, Color.white, duration);
                DebugUtils.Draw(node.CornerB, node.CornerC, Color.white, duration);
                DebugUtils.Draw(node.CornerC, node.CornerA, Color.white, duration);
                // node.Triangle.GetCenter.To3D().DrawPoint(Color.gray, duration, size: 0.1f);
            }
        }
    }
}
