using HCore.Extensions;
using HCore.UI;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
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

        public void SetActive(bool active) => _root.SetActive(active);

        private void OnWorldClick(Vector3 worldPos)
        {
            if (!_placing) return;
            SpawnStorage(new float3(worldPos.x, worldPos.y, 0));
        }

        // ── UI construction ───────────────────────────────────────────────────

        private void BuildToolbar()
        {
            var toolbar = UIStyledElements.NewHorizontalGroup(_root);
            toolbar.style.backgroundColor = UIColors.Surface;
            toolbar.style.SetBorderWidth(1);
            toolbar.style.SetBorderColor(UIColors.Border);
            toolbar.style.SetBorderRadius(4);
            toolbar.style.SetPadding(6);
            toolbar.style.paddingLeft = 10;
            toolbar.style.marginBottom = 6;
            toolbar.style.alignItems = Align.Center;

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
            var entity = em.CreateEntity();

            em.AddComponentData(entity, new StorageComponent { WorldPosition = worldPos });

            var slots = em.AddBuffer<StorageSlot>(entity);
            slots.Add(new StorageSlot { Resource = ResourceType.Wood, Capacity = 20 });
            slots.Add(new StorageSlot { Resource = ResourceType.Stone, Capacity = 20 });

            em.AddBuffer<ConnectionRefElement>(entity);

            ConnectionEditUtils.AutoConnect(em, entity, 5);
        }
    }
}