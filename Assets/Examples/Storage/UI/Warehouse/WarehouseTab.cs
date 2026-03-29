using HCore.Extensions;
using HCore.UI;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UIElements;

namespace Examples.Storage.UI
{
    public class WarehouseTab : ITab
    {
        private VisualElement _root;
        private readonly UIElementScrollView<WarehouseElement> _warehouseList;
        private Button _placeButton;
        private bool _placing;

        public VisualElement Content => _root;

        public WarehouseTab()
        {
            _root = new();
            _root.style.flexGrow = 1;
            _root.style.flexDirection = FlexDirection.Column;

            BuildToolbar();

            var scrollView = UIStyledElements.NewScrollView(_root);
            _warehouseList = new(scrollView, direction: UIMethods.Direction.Vertical);

            WorldInputHandler.WorldClicked += OnWorldClick;
        }

        public void Update()
        {
            using var q = Main.EntityManager.CreateEntityQuery(typeof(StorageComponent));
            using var arr = q.ToEntityArray(Allocator.Temp);
            _warehouseList.SetElements(arr, (row, entity) => row.Refresh(entity));
        }

        public void DrawGizmos()
        {
            foreach (var warehouseElement in _warehouseList)
            {
                if (warehouseElement.Hovered)
                {
                    var storage = Main.EntityManager.GetComponentData<StorageComponent>(warehouseElement._warehouseEntity);
                    new Vector2(storage.WorldPosition.x, storage.WorldPosition.y).DrawPoint(Color.magenta, size: .3f);
                }
            }
        }

        public void SetActive(bool active)
        {
            _root.SetActive(active);
            if (_placing) TogglePlacing();
        }

        private void OnWorldClick(Vector3 worldPos)
        {
            if (!_placing) return;
            SpawnStorage(new float3(worldPos.x, worldPos.y, 0));
        }

        // ── UI construction ───────────────────────────────────────────────────

        private void BuildToolbar()
        {
            var toolbar = UIStyledElements.NewHorizontalContainer(_root);
            _placeButton = UIStyledElements.NewButtonPrimary(toolbar, "+ Place Storage", TogglePlacing);
        }

        // ── Logic ─────────────────────────────────────────────────────────────

        private void TogglePlacing()
        {
            _placing = !_placing;
            _placeButton.text = _placing ? "Cancel Placement" : "+ Place Storage";
            _placeButton.style.backgroundColor = _placing ? UIColors.Warning : UIColors.Accent;
        }

        private static void SpawnStorage(float3 worldPos)
        {
            var em = Main.EntityManager;

            using var prefabQuery = em.CreateEntityQuery(typeof(StoragePrefabsSingleton));

            if (!prefabQuery.TryGetSingleton<StoragePrefabsSingleton>(out var prefabs))
            {
                Debug.LogWarning("Storage Prefabs Singleton not found");
                return;
            }
            
            var entity = em.Instantiate(prefabs.Storage);

            var storage = em.GetComponentData<StorageComponent>(entity);
            var inputOffset  = storage.InputPoint  - storage.WorldPosition;
            var outputOffset = storage.OutputPoint - storage.WorldPosition;
            storage.WorldPosition = worldPos;
            storage.InputPoint    = worldPos + inputOffset;
            storage.OutputPoint   = worldPos + outputOffset;
            em.SetComponentData(entity, storage);

            em.SetComponentData(entity, LocalTransform.FromPosition(worldPos));

            ConnectionEditUtils.AutoConnect(em, entity);
        }
    }
}