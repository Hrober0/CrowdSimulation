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
    /// </summary>
    public class RtsSlotRow : UIElement
    {
        private VisualElement _dot;
        private Label _name;
        private Label _amount;
        private Label _reserved;
        private Label _thresholds;
        private VisualElement _fill;

        public override void Init(VisualElement root)
        {
            base.Init(root);
            root.style.flexDirection = FlexDirection.Column;

            VisualElement row = UIStyledElements.NewHorizontalGroup(root);
            row.style.alignItems = Align.Center;

            _dot = UIStyledElements.NewColorDot(row, UIColors.TextMuted, 7f);

            _name = UIStyledElements.NewLabel(row, "");
            _name.style.minWidth = 60;

            _amount = UIStyledElements.NewLabel(row, "0 / 0");
            _amount.style.minWidth = 60;

            _reserved = UIStyledElements.NewLabel(row, "");
            _reserved.style.minWidth = 70;

            (_, _fill) = UIStyledElements.NewFillBar(root, UIColors.Accent);

            _thresholds = UIStyledElements.NewLabel(root, "");
            _thresholds.style.color = UIColors.TextMuted;
        }

        public void Refresh(StorageSlot slot)
        {
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
