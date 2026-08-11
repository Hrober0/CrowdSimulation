using HCore.UI;
using Unity.Entities;
using UnityEngine.UIElements;

namespace Examples.Storage.UI
{
    public class ConnectionElement : UIElement
    {
        // ── Children ─────────────────────────────────────────────────────────
        private VisualElement _resourceDot;
        private Label _resourceLabel;
        private Label _targetLabel;
        private SliderInt _prioritySlider;
        private EnumField _modeField;

        // ── Bound data ────────────────────────────────────────────────────────
        public Entity ConnectionEntity { get; private set; }
        public bool Hovered { get; private set; }
        private bool _suppressCallbacks;

        // ─────────────────────────────────────────────────────────────────────

        public override void Init(VisualElement root)
        {
            base.Init(root);

            root.style.flexDirection = FlexDirection.Row;
            root.style.alignItems = Align.Center;
            root.style.SetPadding(8);
            root.style.paddingLeft = 16;
            root.style.borderBottomWidth = 2;
            root.style.borderBottomColor = UIColors.BorderFaint;
            root.style.minHeight = 56;
            root.RegisterHoverEvent(h => Hovered = h);

            _resourceDot = UIStyledElements.NewColorDot(root, UIColors.TextMuted, 14f);
            _resourceLabel = UIStyledElements.NewLabel(root, "—");
            _resourceLabel.style.minWidth = 88;
            _resourceLabel.style.color = UIColors.TextSecondary;
            _resourceLabel.style.fontSize = UIColors.FontSizeS;

            _targetLabel = UIStyledElements.NewLabel(root, "—");
            _targetLabel.style.flexGrow = 1;
            _targetLabel.style.color = UIColors.TextSecondary;
            _targetLabel.style.fontSize = UIColors.FontSizeS;
            _targetLabel.style.marginLeft = 12;

            _prioritySlider = UIStyledElements.NewSliderInt(root, "", 0, 255, 128, OnPriorityChanged);
            _prioritySlider.style.flexGrow = 1;
            _prioritySlider.style.marginLeft = 16;
            _prioritySlider.style.marginRight = 16;
            _prioritySlider.style.maxWidth = 240;

            _modeField = UIStyledElements.NewEnumPicker(root, ConnectionMode.Disabled, OnModeChanged);
            _modeField.style.minWidth = 144;

            UIStyledElements.NewButtonIcon(root, "×", RemoveConnection);
        }

        // ─────────────────────────────────────────────────────────────────────

        public void Refresh(Entity connectionEntity)
        {
            ConnectionEntity = connectionEntity;

            var em = Main.EntityManager;
            var conn = em.GetComponentData<StorageConnectionComponent>(connectionEntity);

            _suppressCallbacks = true;

            var resourceColor = PanelStyles.ResourceColor(conn.Resource);
            _resourceDot.style.backgroundColor = resourceColor;
            _resourceLabel.text = conn.Resource.DisplayName();
            _resourceLabel.style.color = resourceColor;

            var arrow = conn.Mode switch
            {
                ConnectionMode.AToB => "→",
                ConnectionMode.BToA => "←",
                ConnectionMode.TwoWays => "↔",
                _ => "–",
            };
            _targetLabel.text = $"#{conn.StorageA.Index} {arrow} #{conn.StorageB.Index}";

            bool active = conn.Mode != ConnectionMode.Disabled;
            _modeField.value = conn.Mode;
            _prioritySlider.value = conn.Priority;
            _prioritySlider.SetInteractable(active);
            _root.style.opacity = active ? 1f : 0.5f;

            _suppressCallbacks = false;
        }

        // ─────────────────────────────────────────────────────────────────────

        private void OnPriorityChanged(int value)
        {
            if (_suppressCallbacks) return;
            ApplyEdit((ConnectionMode)_modeField.value, (byte)value);
        }

        private void OnModeChanged(ConnectionMode mode)
        {
            if (_suppressCallbacks) return;
            bool active = mode != ConnectionMode.Disabled;
            _prioritySlider.SetInteractable(active);
            _root.style.opacity = active ? 1f : 0.5f;
            ApplyEdit(mode, (byte)_prioritySlider.value);
        }

        private void ApplyEdit(ConnectionMode mode, byte priority)
        {
            ConnectionEditUtils.ApplyEdit(Main.EntityManager, ConnectionEntity, mode, priority);
        }

        private void RemoveConnection()
        {
            ConnectionEditUtils.RemoveConnection(Main.EntityManager, ConnectionEntity);
        }
    }
}