using HCore.UI;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace Examples.Storage.UI
{
    public class WarehouseElement : UIElement
    {
        Label _headerLabel;
        Button _toogleButton;
        VisualElement _slotContainer;
        UIElementList<SlotBarElement> _slotList;

        public override void Init(VisualElement root)
        {
            _root = root;
            root.style.SetMargin(2);

            var header = UIStyledElements.NewHorizontalGroup(root);
            _headerLabel = UIStyledElements.NewLabel(header, "Warehouse #—");
            _headerLabel.style.flexGrow = 1;

            _toogleButton = UIStyledElements.NewButtonIcon(header, "", ToggleSlots);

            _slotContainer = new VisualElement();
            _slotContainer.SetActive(false);
            root.Add(_slotContainer);
            
            _slotList = new(
                _slotContainer,
                () =>
                {
                    var ve = new VisualElement();
                    _slotContainer.Add(ve);
                    var bar = new SlotBarElement();
                    bar.Init(ve);
                    return bar;
                },
                hideOther: false
            );
            
            ToggleSlots();
        }

        public void Refresh(Entity e, EntityManager em)
        {
            var storage = em.GetComponentData<StorageComponent>(e);
            _headerLabel.text = $"Warehouse #{e.Index} ({storage.WorldPosition})";

            var slots = em.GetBuffer<StorageSlot>(e, true);
            _slotList.SetElements(slots.AsNativeArray(), (bar, slot) =>
            {
                bar.Refresh(slot, e, em);
            });
        }

        void ToggleSlots()
        {
            _slotContainer.SetActive(!_slotContainer.IsActive());
            _toogleButton.text = _slotContainer.IsActive() ? "/\\" : "\\/";
        }
    }
}