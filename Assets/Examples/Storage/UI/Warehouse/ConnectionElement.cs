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
        private Toggle        _enabledToggle;
        private Label         _overrideLock;
 
        // ── Bound data ────────────────────────────────────────────────────────
        private Entity       _fromEntity;
        private Entity       _toEntity;
        private ResourceType _resource;
        private bool         _suppressCallbacks;
 
        // ─────────────────────────────────────────────────────────────────────
 
        public override void Init(VisualElement root)
        {
            _root = root;
 
            root.style.flexDirection      = FlexDirection.Row;
            root.style.alignItems         = Align.Center;
            root.style.SetPadding(4);
            root.style.paddingLeft        = 8;
            root.style.borderBottomWidth  = 1;
            root.style.borderBottomColor  = UIColors.BorderFaint;
            root.style.minHeight          = 28;
 
            // Resource dot + name
            _resourceDot   = UIStyledElements.NewColorDot(root, UIColors.TextMuted, 7f);
            _resourceLabel = UIStyledElements.NewLabel(root, "—");
            _resourceLabel.style.minWidth  = 44;
            _resourceLabel.style.color     = UIColors.TextSecondary;
            _resourceLabel.style.fontSize  = UIColors.FontSizeS;
 
            // Target storage label
            _targetLabel = UIStyledElements.NewLabel(root, "—");
            _targetLabel.style.flexGrow    = 1;
            _targetLabel.style.color       = UIColors.TextSecondary;
            _targetLabel.style.fontSize    = UIColors.FontSizeS;
            _targetLabel.style.marginLeft  = 6;
 
            // Priority slider 0–255
            _prioritySlider = UIStyledElements.NewSliderInt(root, "", 0, 255, 128, OnPriorityChanged);
            _prioritySlider.style.flexGrow    = 1;
            _prioritySlider.style.marginLeft  = 8;
            _prioritySlider.style.marginRight = 8;
            _prioritySlider.style.maxWidth    = 120;
 
            // Enabled toggle (no label — space is tight)
            _enabledToggle = new Toggle();
            _enabledToggle.style.marginRight = 4;
            _enabledToggle.RegisterValueChangedCallback(evt => OnEnabledChanged(evt.newValue));
            root.Add(_enabledToggle);
        }
 
        // ─────────────────────────────────────────────────────────────────────
 
        public void Refresh(Entity fromEntity, in StorageConnectionElement conn)
        {
            _fromEntity = fromEntity;
            _toEntity   = conn.TargetStorage;
            _resource   = conn.Resource;
 
            _suppressCallbacks = true;
 
            var resourceColor              = PanelStyles.ResourceColor(conn.Resource);
            _resourceDot.style.backgroundColor = resourceColor;
            _resourceLabel.text            = conn.Resource.DisplayName();
            _resourceLabel.style.color     = resourceColor;
 
            _targetLabel.text = $"→  Storage #{conn.TargetStorage.Index}";
 
            bool enabled = conn.Active;
            _enabledToggle.value    = enabled;
            _prioritySlider.value   = conn.Priority;
 
            _suppressCallbacks = false;
        }
 
        // ─────────────────────────────────────────────────────────────────────
 
        private void OnPriorityChanged(int value)
        {
            if (_suppressCallbacks) return;
            ApplyEdit(enabled: _enabledToggle.value, priority: (byte)value);
        }
 
        private void OnEnabledChanged(bool value)
        {
            if (_suppressCallbacks) return;
            
            ApplyEdit(enabled: value, priority: (byte)_prioritySlider.value);
        }
 
        private void ApplyEdit(bool enabled, byte priority)
        {
            ConnectionEditUtils.ApplyEdit(Main.EntityManager, new ConnectionEdit
            {
                FromEntity = _fromEntity,
                ToEntity   = _toEntity,
                Resource   = _resource,
                Priority   = priority,
                Active     = enabled,
            });
        }
    }
}