using System;
using HCore.UI;
using Rts;
using UnityEngine.UIElements;

namespace Examples.Rts.UI
{
    /// <summary>
    /// One storage slot, shown the way §7 describes it: what is on the shelf, what is promised in and out,
    /// and the two thresholds that decide whether it is asking or giving.
    ///
    /// The reserved figures are the interesting ones to watch. They are why "if five loaves exist, only five
    /// loaves of orders exist" is true, and seeing them move is seeing the order market work.
    ///
    /// Priority is the one number here the player may change, because it is the one that is a *choice* rather
    /// than a consequence (§7, §15). The row does not change it itself - it says which item was clicked and
    /// by how much, and the panel finds the slot again on the building that is selected now. That is what
    /// makes the row safe to pool: it holds no entity, and a click cannot land on the building it was showing
    /// three selections ago.
    /// </summary>
    public class RtsSlotRow : UIElement
    {
        private VisualElement _dot;
        private Label _name;
        private Label _amount;
        private Label _reserved;
        private Label _thresholds;
        private VisualElement _fill;

        private ItemId _item;
        private Action<ItemId, int> _onPriorityChanged;

        public override void Init(VisualElement root)
        {
            base.Init(root);
            root.style.flexDirection = FlexDirection.Column;

            VisualElement row = UIStyledElements.NewHorizontalGroup(root);
            row.style.alignItems = Align.Center;

            _dot = UIStyledElements.NewColorDot(row, UIColors.TextMuted, 14f);

            _name = UIStyledElements.NewLabel(row, "");
            _name.style.minWidth = 120;

            _amount = UIStyledElements.NewLabel(row, "0 / 0");
            _amount.style.minWidth = 120;

            _reserved = UIStyledElements.NewLabel(row, "");
            _reserved.style.minWidth = 140;

            (_, _fill) = UIStyledElements.NewFillBar(root, UIColors.Accent);

            VisualElement footer = UIStyledElements.NewHorizontalGroup(root);
            footer.style.alignItems = Align.Center;

            _thresholds = UIStyledElements.NewLabel(footer, "");
            _thresholds.style.color = UIColors.TextMuted;
            _thresholds.style.minWidth = 200;

            // Bound once, when the row is made. The buttons capture the row and nothing else, so pooling can
            // hand this row to a different building without anything having to be unhooked.
            NewStep(footer, "-", -1);
            NewStep(footer, "+", 1);
        }

        private void NewStep(VisualElement parent, string text, int delta)
        {
            Button button = UIStyledElements.NewButton(parent, text, () => _onPriorityChanged?.Invoke(_item, delta));
            button.style.minWidth = 26;
        }

        public void Refresh(StorageSlot slot, Action<ItemId, int> onPriorityChanged)
        {
            _item = slot.Item;
            _onPriorityChanged = onPriorityChanged;

            _name.text = ItemCatalog.Name(slot.Item);
            _name.style.color = ItemCatalog.Colour(slot.Item);
            _dot.style.backgroundColor = ItemCatalog.Colour(slot.Item);

            _amount.text = $"{slot.Amount} / {slot.Capacity}";
            _fill.style.width = Length.Percent(UIMethods.CountPercent(slot.Amount, slot.Capacity) * 100f);

            string outgoing = slot.ReservedOut > 0 ? $"↑{slot.ReservedOut}" : "";
            string incoming = slot.ReservedIn > 0 ? $"↓{slot.ReservedIn}" : "";
            _reserved.text = $"{outgoing} {incoming}".Trim();

            _thresholds.text = $"in≤{slot.DeliverInUpTo}  out≥{slot.DeliverOutDownTo}  prio {slot.Priority}";
        }
    }
}
