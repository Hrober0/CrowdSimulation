using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using System;
using System.Collections;
using UnityEngine.Pool;

namespace HCore.UI
{
    public class UIElementMultiList<TMain> : IEnumerable<TMain> where TMain : UIElement
    {
        private readonly VisualElement _parent;
        private readonly Dictionary<Type, ObjectPool<TMain>> _pools = new();

        private readonly List<TMain> _activeElements = new();

        public UIElementMultiList(VisualElement parent, bool hideOther = true)
        {
            _parent = parent;

            if (hideOther)
            {
                UIMethods.HideAllElementChildrens(parent);
            }
        }

        public void AddType<T>(VisualTreeAsset pattern, Action<T> onCreatedMethod = null) where T : TMain, new()
        {
            AddType(CreateMethod);

            T CreateMethod()
            {
                var vElement = pattern.CloneTree();
                _parent.Add(vElement);
                var uiElement = new T();
                uiElement.Init(vElement);
                onCreatedMethod?.Invoke(uiElement);
                return uiElement;
            };
        }
        public void AddType<T>(Func<T> createdMethod) where T : TMain
        {
            _pools.Add(typeof(T), new ObjectPool<TMain>(createdMethod));
        }

        public T ShowElement<T>() where T : TMain
        {
            if (!_pools.TryGetValue(typeof(T), out var list))
            {
                Debug.LogError($"Not register {typeof(T)} type");
                return null;
            }

            var element = (T)list.Get();
            if (_activeElements.Count > 0)
            {
                element.Root.PlaceInFront(_activeElements[^1].Root);
            }
            element.SetActive(true);
            _activeElements.Add(element);
            return element;
        }

        public int Count => _activeElements.Count;
        public TMain this[int index] => _activeElements[index];

        public void Clear()
        {
            foreach (var element in _activeElements)
            {
                if (_pools.TryGetValue(element.GetType(), out var pool))
                {
                    pool.Release(element);
                }
                else
                {
                    Debug.LogError($"Element of type {element.GetType()} has no pool to return");
                }
                element.SetActive(false);
            }
            _activeElements.Clear();
        }

        public IEnumerator<TMain> GetEnumerator() => _activeElements.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
