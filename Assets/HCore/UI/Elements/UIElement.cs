using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace HCore.UI
{
    public class UIElement
    {
        protected VisualElement _root;

        public virtual void Init(VisualElement root)
        {
            _root = root;
        }

        public virtual void SetActive(bool value) => UIMethods.SetActiveElement(_root, value);
        public VisualElement Root => _root;
        
        protected void Init(VisualElement parent, VisualTreeAsset pattern)
        {
            TemplateContainer root = pattern.CloneTree();
            parent.Add(root);
            Init(root);
        }
    }
}
