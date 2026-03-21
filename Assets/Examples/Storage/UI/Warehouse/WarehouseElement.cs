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
        private UIElementList<ConnectionElement> _connList;
        
        private Entity _warehouseEntity;

        public override void Init(VisualElement root)
        {
            _root = root;
            root.style.SetMargin(2);

            var header = UIStyledElements.NewHorizontalGroup(root);
            _headerLabel = UIStyledElements.NewLabel(header, "Warehouse #—");
            _headerLabel.style.flexGrow = 1;

            _toggleButton = UIStyledElements.NewButtonIcon(header, "", ToggleSlots);

            _expandContainer = new VisualElement();
            _expandContainer.SetActive(false);
            root.Add(_expandContainer);

            var itemsG = UIStyledElements.NewHorizontalGroup(_expandContainer);
            UIStyledElements.NewLabel(itemsG, "Items");
            var items = new VisualElement();
            _expandContainer.Add(items);
            _slotList = new(items);

            var connectionG = UIStyledElements.NewHorizontalGroup(_expandContainer);
            UIStyledElements.NewLabel(connectionG, "Connections");
            UIStyledElements.NewButtonIcon(connectionG, "+", AddConnection);
            var connections = new VisualElement();
            _expandContainer.Add(connections);
            _connList = new(connections);

            ToggleSlots();
        }

        public void Refresh(Entity warehouseEntity)
        {
            _warehouseEntity = warehouseEntity;
                
            var em = Main.EntityManager;

            var storage = em.GetComponentData<StorageComponent>(warehouseEntity);
            _headerLabel.text = $"Warehouse #{warehouseEntity.Index} ({storage.WorldPosition})";

            var slots = em.GetBuffer<StorageSlot>(warehouseEntity, true);
            _slotList.SetElements(slots.AsNativeArray(), (bar, slot) => { bar.Refresh(slot, warehouseEntity, em); });

            var connections = em.GetBuffer<StorageConnectionElement>(warehouseEntity, true);
            _connList.SetElements(connections.AsNativeArray(),
                (element, connectionElement) => element.Refresh(warehouseEntity, connectionElement));
        }

        void ToggleSlots()
        {
            _expandContainer.SetActive(!_expandContainer.IsActive());
            _toggleButton.text = _expandContainer.IsActive() ? "/\\" : "\\/";
        }

        private void AddConnection()
        {
            ConnectionEditUtils.AutoConnect(Main.EntityManager, _warehouseEntity);
        }
        
        private static void AddConnectionIfMissing(
            EntityCommandBuffer ecb,
            BufferLookup<StorageConnectionElement> connLookup,
            Entity fromEntity,
            Entity toEntity,
            ResourceType resource)
        {
            // Check existing buffer to avoid duplicates
            if (connLookup.HasBuffer(fromEntity))
            {
                var existing = connLookup[fromEntity];
                for (int i = 0; i < existing.Length; i++)
                {
                    var c = existing[i];
                    if (c.TargetStorage == toEntity && c.Resource == resource) return;
                }
            }
 
            ecb.AppendToBuffer(fromEntity, new StorageConnectionElement
            {
                TargetStorage = toEntity,
                Resource      = resource,
                Priority      = 128,
                MaxBatchSize  = 20,
                Flags         = ConnectionFlags.Enabled,
            });
        }
    }
}