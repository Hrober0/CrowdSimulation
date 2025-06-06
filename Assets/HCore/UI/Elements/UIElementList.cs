using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace HCore.UI
{
    public class UIElementList<T> : IEnumerable<T> where T : UIElement, new()
    {
        private readonly Func<T> _createMethod;

        private readonly List<T> _list = new();
        private int _numberOfVisableElements = 0;

        public VisualElement Container { get; }
        public int Count => _numberOfVisableElements;

        public UIElementList(VisualElement container, VisualTreeAsset pattern, Action<T> onCreatedMethod = null, bool hideOther = true)
        {
            Container = container;
            _createMethod = CreateNew;

            if (hideOther)
            {
                UIMethods.HideAllElementChildrens(container);
            }

            T CreateNew()
            {
                var vElement = pattern.CloneTree();
                Container.Add(vElement);
                var uiElement = new T();
                uiElement.Init(vElement);
                onCreatedMethod?.Invoke(uiElement);
                return uiElement;
            }
        }
        public UIElementList(VisualElement container, Func<T> createMethod, bool hideOther = true)
        {
            Container = container;
            _createMethod = createMethod;

            if (hideOther)
            {
                UIMethods.HideAllElementChildrens(container);
            }
        }

        public T this[int index] => _list[index];

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
            {
                yield return _list[i];
            }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public T FindElement(Predicate<T> match) => _list.Find(match);
        public bool TryGetElement(Predicate<T> match, out T element)
        {
            element = _list.Find(match);
            return element != null;
        }

        /// <summary>
        /// Set active and return next element
        /// Create new if it is necessary
        /// </summary>
        public T ShowElement()
        {
            if (_numberOfVisableElements >= _list.Count)
            {
                _list.Add(_createMethod());
            }

            var element = _list[_numberOfVisableElements];
            _numberOfVisableElements++;

            element.SetActive(true);
            return element;
        }

        public void SetElement(Action<T> setupMethod)
        {
            var element = ShowElement();
            setupMethod(element);
        }
        public void SetElement<TData>(TData data, Action<T, TData> setupMethod)
        {
            var element = ShowElement();
            setupMethod(element, data);
        }
        public void SetElements<TData>(IEnumerable<TData> data, Action<T, TData> setupMethod)
        {
            _numberOfVisableElements = 0;

            foreach (var d in data)
            {
                SetElement(d, setupMethod);
            }

            for (int i = _numberOfVisableElements; i < _list.Count; i++)
            {
                _list[i].SetActive(false);
            }
        }

        public void Clear()
        {
            for (int i = 0; i < _numberOfVisableElements; i++)
            {
                _list[i].SetActive(false);
            }
            _numberOfVisableElements = 0;
        }
    }
}