using System.Collections.Generic;
using Unity.AI.Navigation;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;

namespace Benchmarks
{
    public class DefaultObstacleProvider : MonoBehaviour, IObstacleProvider
    {
        [SerializeField] private Transform _floor;
        [SerializeField] private List<GameObject> _spawned = new();

        [SerializeField] private int _notWalkableArea = 1;

        public void Initialize(float2 size)
        {
            _floor.position = new Vector3(size.x / 2, 0, size.y / 2);
            _floor.localScale = new Vector3(size.x / 10, 1, size.y / 10);
        }

        public void SpawnObstacle(float2 position, float size)
        {
            var go = new GameObject("NavMeshObstacle_2D");
            go.transform.SetParent(transform, false);

            // 2D → XZ plane
            go.transform.position = new Vector3(position.x, 0f, position.y);

            var volume = go.AddComponent<NavMeshModifierVolume>();
            volume.area = _notWalkableArea;

            // XZ footprint, minimal height
            volume.center = Vector3.zero;
            volume.size = new Vector3(size, 2f, size);

            _spawned.Add(go);

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        public void ClearAll()
        {
            foreach (var go in _spawned)
                DestroyImmediate(go);

            _spawned.Clear();

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
    }
}