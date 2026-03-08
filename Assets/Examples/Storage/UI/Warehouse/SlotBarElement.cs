using HCore.Extensions;
using HCore.UI;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace Examples.Storage.UI
{
    public class SlotBarElement : UIElement
    {
        private Label _nameLabel;
        private Label _amountLabel;
        private VisualElement _fillBar;
        private Label _reservedOut, _reservedIn;
        
        private Entity _entity;
        private EntityManager _em;
        private ResourceType _resourceType;

        public override void Init(VisualElement root)
        {
            _root = root;
            root.style.flexDirection = FlexDirection.Column;

            var row = UIStyledElements.NewHorizontalGroup(root);
            // resource name label (set in Refresh)
            UIStyledElements.NewLabel(row, "—");

            var amountGroup = UIStyledElements.NewHorizontalGroup(row);
            amountGroup.style.minWidth = 100;
            _nameLabel = UIStyledElements.NewLabel(amountGroup, "");
            _amountLabel = UIStyledElements.NewLabel(amountGroup, "0 / 0");
            _reservedOut = UIStyledElements.NewLabel(amountGroup, "");
            _reservedIn = UIStyledElements.NewLabel(amountGroup, "");

            UIStyledElements.NewButtonIcon(row, "-", OnDecrement);
            UIStyledElements.NewButtonIcon(row, "+", OnIncrement);

            UIStyledElements.NewDivider(row);

            (_, _fillBar) = UIStyledElements.NewFillBar(row, UIColors.Accent);
        }

        public void Refresh(StorageSlot slot, Entity entity, EntityManager em)
        {
            _entity = entity;
            _em = em;
            _resourceType = slot.Resource;
            
            _nameLabel.text = slot.Resource.DisplayName();
            _nameLabel.style.color = PanelStyles.ResourceColor(slot.Resource);
            
            _amountLabel.text = $"{slot.CurrentAmount} / {slot.Capacity}";
            float pct = UIMethods.CountPercent(slot.CurrentAmount, slot.Capacity);
            _fillBar.style.width = Length.Percent(pct * 100f);

            _reservedOut.text = slot.ReservedOutgoing > 0 ? $"↑{slot.ReservedOutgoing}" : "";
            _reservedIn.text = slot.ReservedIncoming > 0 ? $"↓{slot.ReservedIncoming}" : "";
        }

        void OnIncrement() => ModifyAmount(+5);
        void OnDecrement() => ModifyAmount(-5);

        void ModifyAmount(int delta)
        {
            var slots = _em.GetBuffer<StorageSlot>(_entity);
            var index = slots.AsNativeArray().FindIndex(s => s.Resource == _resourceType);
            var slot = slots[index];
            slot.CurrentAmount = Mathf.Clamp(slot.CurrentAmount + delta, 0, slot.Capacity);
            slots[index] = slot;
        }
    }
}