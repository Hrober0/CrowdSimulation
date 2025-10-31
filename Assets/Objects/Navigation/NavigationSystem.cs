using System;
using System.Collections.Generic;
using System.Linq;
using andywiecko.BurstTriangulator;
using HCore.Extensions;
using HCore.Shapes;
using HCore.Systems;
using Navigation;
using Objects.Agents;
using Objects.GenericSystems;
using Objects.Obstacles;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Triangle = Navigation.Triangle;

namespace Objects.Navigation
{
    public class NavigationSystem : MonoBehaviour, ISystem
    {
        [SerializeField] private List<Transform> _borderPoints;
        
        [Space]
        [SerializeField] private float _size = .5f;
        
        [Space]
        [SerializeField] private bool _drawConnections;
        [SerializeField] private bool _drawNodes;
        [SerializeField] private bool _drawNodesCenters;

        [Space]
        [SerializeField] private bool _drawObstacleTriangle;
        [SerializeField] private bool _drawObstacleBorder;
        
        [Space]
        [SerializeField] private List<SampleAgent> _agents; 
        
        private ObjectsSystem _objectsSystem;
        
        private NavMesh<IdAttribute> _navMesh;
        private NavObstacles<IdAttribute> _navObstacles;

        private readonly Dictionary<int, int> _obstacleIdToId = new();
        
        private readonly List<List<Portal>> _agentsPortals = new();
        
        public void Initialize(ISystemManager systems)
        {
            _objectsSystem = systems.Get<ObjectsSystem>();

            _objectsSystem.OnObjectRegisteredInit += RegisterObstacle;
            _objectsSystem.OnObjectUnregisteredInit += UnregisterObstacle;
            
            _navMesh = new(10);
            _navObstacles = new(10);
            
            AddInitNodes();
        }

        public void Deinitialize()
        {
            _objectsSystem.OnObjectRegisteredInit -= RegisterObstacle;
            _objectsSystem.OnObjectUnregisteredInit -= UnregisterObstacle;

            _navMesh.Dispose();
            _navObstacles.Dispose();
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(0) && _agents.Count > 0)
            {
                var targetClick = (float2)Camera.main.ScreenToWorldPoint(Input.mousePosition).To2D();
                if (!_navMesh.TryGetNodeIndex(targetClick, out var targetNodeIndex))
                {
                    return;
                }

                using var targetPlaces = new NativeList<float2>(_agents.Count, Allocator.TempJob);
                PathFinding.FindSpaces(targetClick, targetNodeIndex, _agents.Count, 1, _navMesh.Nodes, new SamplePathSeeker(), targetPlaces);
                
                var agentPositions = new NativeArray<float2>(_agents.Count, Allocator.TempJob);
                for (var index = 0; index < _agents.Count; index++)
                {
                    agentPositions[index] = _agents[index].Bounds.Center;
                }

                using var assignedPositions = new NativeArray<float2>(_agents.Count, Allocator.TempJob);
                PathFinding.AssignTargets(agentPositions, targetPlaces.AsArray(), assignedPositions);

                var requests = new NativeArray<StartAndTarget>(_agents.Count, Allocator.TempJob);
                for (var index = 0; index < _agents.Count; index++)
                {
                    requests[index] = new(_agents[index].Bounds.Center, assignedPositions[index]);
                }
                
                var results = new NativeStream(_agents.Count, Allocator.TempJob);
                Debug.Log("Sheduled");
                
                new FindPathsJob<IdAttribute, SamplePathSeeker>
                {
                    StartAndTargetEntry = requests,
                    NavMesh = _navMesh,
                    Seeker = new(),
                    ResultPaths = results.AsWriter(),
                }.Schedule(_agents.Count, 1).Complete();
                
                Debug.Log("Finished");
                _agentsPortals.Clear();

                var reader = results.AsReader();
                for (var index = 0; index < _agents.Count; index++)
                {
                    var portals = new List<Portal>();
                    
                    reader.BeginForEachIndex(index);
                    while (reader.RemainingItemCount > 0)
                    {
                        var portal = reader.Read<Portal>();
                        portals.Add(portal);    
                    }
                    reader.EndForEachIndex();
                    
                    var request = requests[index];
                    var lastPortal = portals.Count > 0 ? portals[^1].Center : request.StartPosition;
                    var direction =  math.normalize(request.TargetPosition - lastPortal);
                    var normal = new float2(-direction.y, direction.x);
                    portals.Add(new(request.TargetPosition + normal, request.TargetPosition - normal));
                    
                    _agentsPortals.Add(portals);
                }
                Debug.Log($"Converted {_agentsPortals.Count}");

                agentPositions.Dispose();
                results.Dispose();
                requests.Dispose();
            }

            for (int i = 0; i < _agentsPortals.Count; i++)
            {
                var agent = _agents[i];
                var portals = _agentsPortals[i];
                float2 agentPosition = agent.Bounds.Center;

                if (portals.Count > 0)
                {
                    var portal = portals[0];
                    DebugUtils.Draw(agentPosition, portal.Center, Color.black);
                    
                    if (GeometryUtils.Sign(agentPosition, portal.Left, portal.Right) > 0)
                    {
                        portals.RemoveAt(0);
                    }
                }
                
                float2 direction;
                if (portals.Count == 0)
                {
                    agent.TargetVelocity = Vector2.zero;    
                    continue;
                }
                
                if (portals.Count > 1)
                {
                    direction = PathFinding.ComputeGuidanceVector(agentPosition, portals[0], portals[1].Center);
                }
                else
                {
                    direction = math.normalize(portals[0].Center - agentPosition);
                }
                
                agent.TargetVelocity= direction;
            }
        }

        private void AddInitNodes()
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
                var triangle = new Triangle(
                    positions[triangles[i]],
                    positions[triangles[i + 1]],
                    positions[triangles[i + 2]]
                );
                _navMesh.AddNode(new(triangle, new()));
            }

            positions.Dispose();
        }

        private void RegisterObstacle(IObject obj, int objectId)
        {
            if (!obj.TryGetModule(out IObstacle _))
            {
                return;
            }
            
            using var border = new NativeList<float2>(16, Allocator.Temp);
            foreach (var p in obj.Bounds.GetBorderPoints())
            {
                border.Add(p);
            }
            PolygonUtils.ExpandPolygon(border, -_size);
            var id = _navObstacles.AddObstacle(border, new IdAttribute(objectId));
            
            _obstacleIdToId.Add(objectId, id);
            
            RunUpdate(obj.Bounds.Min, obj.Bounds.Max);
        }

        private void UnregisterObstacle(IObject obj, int objectId)
        {
            if (!obj.TryGetModule(out IObstacle _))
            {
                return;
            }

            if (!_obstacleIdToId.Remove(objectId, out var id))
            {
                return;
            }
            
            _navObstacles.RemoveObstacle(id);
            
            RunUpdate(obj.Bounds.Min, obj.Bounds.Max);
        }
        
        private void RunUpdate(float2 min, float2 max)
        {
            // Debug.Log("Run");
            new NavMeshUpdateJob<IdAttribute>
            {
                NavMesh = _navMesh,
                NavObstacles = _navObstacles,
                UpdateMin = min,
                UpdateMax = max,
            }.Run();
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
            }
        }
    }
}
