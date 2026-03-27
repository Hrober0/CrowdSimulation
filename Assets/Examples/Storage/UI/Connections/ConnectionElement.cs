using HCore.UI;
using Unity.Entities;
using UnityEngine.UIElements;

namespace Examples.Storage.UI
{
    public class ConnectionElement : UIElement
    {
        // ── Children ─────────────────────────────────────────────────────────
        private VisualElement _resourceDot;
        private Label         _resourceLabel;
        private Label         _targetLabel;
        private SliderInt     _prioritySlider;
        private EnumField     _modeField;

        // ── Bound data ────────────────────────────────────────────────────────
        private Entity _connectionEntity;
        private bool   _suppressCallbacks;

        // ─────────────────────────────────────────────────────────────────────

        public override void Init(VisualElement root)
        {
            _root = root;

            root.style.flexDirection     = FlexDirection.Row;
            root.style.alignItems        = Align.Center;
            root.style.SetPadding(4);
            root.style.paddingLeft       = 8;
            root.style.borderBottomWidth = 1;
            root.style.borderBottomColor = UIColors.BorderFaint;
            root.style.minHeight         = 28;

            _resourceDot = UIStyledElements.NewColorDot(root, UIColors.TextMuted, 7f);
            _resourceLabel = UIStyledElements.NewLabel(root, "—");
            _resourceLabel.style.minWidth = 44;
            _resourceLabel.style.color    = UIColors.TextSecondary;
            _resourceLabel.style.fontSize = UIColors.FontSizeS;

            _targetLabel = UIStyledElements.NewLabel(root, "—");
            _targetLabel.style.flexGrow   = 1;
            _targetLabel.style.color      = UIColors.TextSecondary;
            _targetLabel.style.fontSize   = UIColors.FontSizeS;
            _targetLabel.style.marginLeft = 6;

            _prioritySlider = UIStyledElements.NewSliderInt(root, "", 0, 255, 128, OnPriorityChanged);
            _prioritySlider.style.flexGrow    = 1;
            _prioritySlider.style.marginLeft  = 8;
            _prioritySlider.style.marginRight = 8;
            _prioritySlider.style.maxWidth    = 120;

            _modeField = UIStyledElements.NewEnumPicker<ConnectionMode>(
                root, ConnectionMode.Disabled, OnModeChanged);
            _modeField.style.minWidth = 72;

            UIStyledElements.NewButtonIcon(root, "×", RemoveConnection);
        }

        // ─────────────────────────────────────────────────────────────────────

        public void Refresh(Entity connectionEntity)
        {
            _connectionEntity = connectionEntity;

            var em   = Main.EntityManager;
            var conn = em.GetComponentData<StorageConnectionComponent>(connectionEntity);

            _suppressCallbacks = true;

            var resourceColor = PanelStyles.ResourceColor(conn.Resource);
            _resourceDot.style.backgroundColor = resourceColor;
            _resourceLabel.text        = conn.Resource.DisplayName();
            _resourceLabel.style.color = resourceColor;

            var arrow = conn.Mode switch
            {
                ConnectionMode.AToB   => "→",
                ConnectionMode.BToA   => "←",
                ConnectionMode.TwoWays => "↔",
                _                     => "–",
            };
            _targetLabel.text = $"#{conn.StorageA.Index} {arrow} #{conn.StorageB.Index}";

            bool active = conn.Mode != ConnectionMode.Disabled;
            _modeField.value          = conn.Mode;
            _prioritySlider.value     = conn.Priority;
            _prioritySlider.SetInteractable(active);
            _root.style.opacity       = active ? 1f : 0.5f;

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
            ConnectionEditUtils.ApplyEdit(Main.EntityManager, _connectionEntity, mode, priority);
        }

        private void RemoveConnection()
        {
            ConnectionEditUtils.RemoveConnection(Main.EntityManager, _connectionEntity);
        }
    }
}
