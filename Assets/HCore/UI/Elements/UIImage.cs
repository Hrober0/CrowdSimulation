using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace HCore.UI
{
    public class UIImage : UIElement
    {
        protected VisualElement _icon;

        public UIImage() { }
        public UIImage(VisualElement root)
        {
            Init(root);
        }

        public override void Init(VisualElement root)
        {
            base.Init(root);
            _icon = _root.Q<VisualElement>("Image");
            SetGray(false);
        }
        
        public virtual void SetImage(Sprite icon)
        {
            _icon.style.backgroundImage = new (icon);
        }

        public void SetGray(bool value) => _icon.style.unityBackgroundImageTintColor = value ? Color.gray : Color.white;
    }
}