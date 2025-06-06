using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace HCore.UI
{
    public class UIProgressBar : UIElement
    {
        public enum Orientation { Vertical, Horizontal }

        private VisualElement _barFill;
        private Orientation _orientation;
        private float? _lastPercent = null;

        public UIProgressBar() { }
        public UIProgressBar(VisualElement root, Orientation orientation = Orientation.Horizontal)
        {
            Init(root);
            SetOrientation(orientation);
        }
        
        public override void Init(VisualElement root)
        {
            base.Init(root);
            SetOrientation(Orientation.Horizontal);
        }

        public void SetOrientation(Orientation orientation)
        {
            _orientation = orientation;
            _barFill = _root.Q<VisualElement>(orientation == Orientation.Vertical ? "ProgressBar_VerticalFill" : "ProgressBar_HorizontalFill");
        }

        public virtual void SetValue(float currentValue, float maxValue) => SetValue(currentValue, 0, maxValue);
        public virtual void SetValue(float currentValue, float minValue, float maxValue)
        {
            currentValue -= minValue;
            maxValue -= minValue;
            SetValue(maxValue == 0 ? 0 : Mathf.Clamp(currentValue / maxValue, 0f, 1f));
        }
        public virtual void SetValue(float percent)
        {
            if (_lastPercent == percent)
                return;

            _lastPercent = percent;

            if (_orientation == Orientation.Vertical)
            {
                _barFill.style.scale = new Scale(new Vector2(1, percent));
            }
            else
            {
                _barFill.style.scale = new Scale(new Vector2(percent, 1));
            }
        }

        public VisualElement Fill => _barFill;
    }
}
