using HCore.UI;
using Unity.Entities;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;

namespace Examples.Storage.UI.Holders
{
    public class HolderElement : UIElement
    {
        // ── Child references ────────────────────────────────────────────────
        private Label _indexLabel;
        private Label _stateBadge;
        private Label _loadLabel;
        private Label _jobLabel;

        // ── State ────────────────────────────────────────────────────────────
        public Entity BoundEntity { get; private set; }

        // ────────────────────────────────────────────────────────────────────

        public override void Init(VisualElement root)
        {
            _root = root;

            // Outer row
            root.style.flexDirection = FlexDirection.Row;
            root.style.alignItems = Align.Center;
            root.style.backgroundColor = UIColors.Surface;
            root.style.SetBorderWidth(1);
            root.style.SetBorderColor(UIColors.Border);
            root.style.SetBorderRadius(4);
            root.style.SetPadding(5);
            root.style.paddingLeft = 10;
            root.style.marginBottom = 3;

            // Hover highlight
            root.RegisterHoverEvent(h =>
                root.style.backgroundColor = h ? UIColors.SurfaceHover : UIColors.Surface);

            // ── Entity index ─────────────────────────────────────────────────
            _indexLabel = UIStyledElements.NewLabel(root, "—");
            _indexLabel.style.minWidth = 70;
            _indexLabel.style.color = UIColors.TextMuted;

            // ── State badge ──────────────────────────────────────────────────
            _stateBadge = UIStyledElements.NewLabel(root, "IDLE");
            _stateBadge.style.minWidth = 90;
            _stateBadge.style.fontSize = UIColors.FontSizeXS;
            _stateBadge.style.SetPadding(2);
            _stateBadge.style.paddingLeft = 6;
            _stateBadge.style.paddingRight = 6;
            _stateBadge.style.SetBorderRadius(3);
            _stateBadge.style.unityTextAlign = TextAnchor.MiddleCenter;

            // ── Carried resource + amount ─────────────────────────────────────
            var loadGroup = UIStyledElements.NewHorizontalGroup(root);
            loadGroup.style.flexGrow = 1;
            loadGroup.style.alignItems = Align.Center;
            loadGroup.style.marginLeft = 8;

            UIStyledElements.NewLabel(loadGroup, "carrying:");
            _loadLabel = UIStyledElements.NewLabel(loadGroup, "—");
            _loadLabel.style.marginLeft = 4;
            _loadLabel.style.color = UIColors.TextPrimary;

            // ── Assigned job ──────────────────────────────────────────────────
            _jobLabel = UIStyledElements.NewLabel(root, "");
            _jobLabel.style.color = UIColors.TextMuted;
            _jobLabel.style.fontSize = UIColors.FontSizeXS;
            _jobLabel.style.minWidth = 70;
            _jobLabel.style.unityTextAlign = TextAnchor.MiddleRight;
        }

        // ────────────────────────────────────────────────────────────────────

        public void Refresh(Entity entity, HolderComponent h)
        {
            BoundEntity = entity;

            _indexLabel.text = $"Holder #{entity.Index}";

            // State badge — text and color from UIColors lookup
            _stateBadge.text = h.State.ToString();
            var stateColor = PanelStyles.HolderStateColor(h.State);
            _stateBadge.style.color = stateColor;
            _stateBadge.style.backgroundColor = stateColor.WithAlpha(0.12f);

            // Active row tint — left border accent when not idle
            bool active = h.State != HolderState.Idle && h.State != HolderState.Returning;
            _root.style.borderLeftWidth = active ? 3 : 1;
            _root.style.borderLeftColor = active ? stateColor : UIColors.Border;

            // Carried resource
            if (h.CurrentLoad > 0)
            {
                var resourceColor = PanelStyles.ResourceColor(h.CarriedType);
                _loadLabel.text = $"{h.CarriedType.DisplayName()}  {h.CurrentLoad}";
                _loadLabel.style.color = resourceColor;
            }
            else
            {
                _loadLabel.text = "—";
                _loadLabel.style.color = UIColors.TextMuted;
            }

            // Job reference
            _jobLabel.text = h.AssignedJob != Entity.Null
                ? $"job #{h.AssignedJob.Index}"
                : "";
        }
    }
}