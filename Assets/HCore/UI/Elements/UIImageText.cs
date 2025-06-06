using UnityEngine;
using UnityEngine.UIElements;

namespace HCore.UI
{
    public class UIImageText : UIImage
    {
        protected Label _label;

        public UIImageText() : base() { }
        public UIImageText(VisualElement root) : base(root) { }

        public override void Init(VisualElement root)
        {
            base.Init(root);
            _label = _root.Q<Label>("Text");
        }

        public void SetText(string textValue)
        {
            if (textValue != _label.text)
                _label.text = textValue;
        }
        public void SetText(string textValue, Color color)
        {
            SetText(textValue);
            _label.style.color = color;
        }

        public void SetTextRed(bool value) => UIMethods.SetElementClass(_label, "TColor_Red", value);
    }
}