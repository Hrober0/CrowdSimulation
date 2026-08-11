using HCore.UI;
using Unity.Entities;
using UnityEngine.UIElements;

namespace Examples.Storage.UI
{
    public class WarehouseElement : UIElement
    {
        private Label _headerLabel;
        private Button _toggleButton;
        private VisualElement _expandContainer;
        private UIElementList<ItemElement> _slotList;
        
        public Entity _warehouseEntity { get; private set; }
        public bool Hovered { get; private set; }

        public override void Init(VisualElement root)
        {
            _root = root;
            root.style.SetMargin(4);
            root.RegisterHoverEvent(h => Hovered = h);

            var header = UIStyledElements.NewHorizontalGroup(root);
            _headerLabel = UIStyledElements.NewLabel(header, "Warehouse #—");
            _headerLabel.style.flexGrow = 1;

            UIStyledElements.NewButtonDanger(header, "×", DestroyWarehouse);
            _toggleButton = UIStyledElements.NewButtonIcon(header, "", ToggleSlots);

            _expandContainer = new VisualElement();
            _expandContainer.SetActive(false);
            root.Add(_expandContainer);

            var itemsG = UIStyledElements.NewHorizontalGroup(_expandContainer);
            UIStyledElements.NewLabel(itemsG, "Items");
            var items = new VisualElement();
            _expandContainer.Add(items);
            _slotList = new(items);

            ToggleSlots();
        }

        public void Refresh(Entity warehouseEntity)
        {
            _warehouseEntity = warehouseEntity;
            var em = Main.EntityManager;

            var storage = em.GetComponentData<StorageComponent>(warehouseEntity);
            _headerLabel.text = $"Warehouse #{warehouseEntity.Index} ({storage.WorldPosition})";

            var slots = em.GetBuffer<StorageSlot>(warehouseEntity, true);
            _slotList.SetElements(slots.AsNativeArray(), (bar, slot) => bar.Refresh(slot, warehouseEntity));
        }

        void ToggleSlots()
        {
            _expandContainer.SetActive(!_expandContainer.IsActive());
            _toggleButton.text = _expandContainer.IsActive() ? "/\\" : "\\/";
        }

        private void DestroyWarehouse()
        {
            ConnectionEditUtils.DestroyStorage(Main.EntityManager, _warehouseEntity);
        }
    }
}