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

            _ = Init();
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
        private async Awaitable Init()
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