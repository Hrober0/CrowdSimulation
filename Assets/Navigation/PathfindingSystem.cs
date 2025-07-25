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
            _navMesh.Add(new float2(1, 1), new float2(3, 1), new float2(3, 3));
            await WaitForClick();
            _navMesh.Add(new float2(10, 10), new float2(8, 8), new float2(10, 8));
        }
        
        private void OnDrawGizmos()
        {
            if (_navMesh != null)
            {
                _navMesh.DrawNodes();
                _navMesh.DrawConnections();
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