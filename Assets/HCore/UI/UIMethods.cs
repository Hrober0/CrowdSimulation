using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace HCore.UI
{
    public static class UIMethods
    {
        public const int DEFAULT_NAME_WIDTH = 150;
        public const int SMALL_INPUT_WIDTH = 50;
        public const int LINE_SPACE = 10;

        public enum Direction
        {
            Horizontal,
            Vertical
        }

        #region elements modifications

        public static void SetActiveElement(VisualElement element, bool state)
        {
            if (element == null)
            {
                Debug.LogWarning("Visual element is null!");
                return;
            }

            DisplayStyle stargetDisplay = state ? DisplayStyle.Flex : DisplayStyle.None;
            if (element.style.display != stargetDisplay)
                element.style.display = stargetDisplay;
        }

        public static void SetActive(this VisualElement element, bool state) => SetActiveElement(element, state);

        public static bool VisualElementActive(VisualElement element)
        {
            if (element == null)
            {
                Debug.LogWarning("Visual element is null!");
                return false;
            }

            return element.style.display.value == DisplayStyle.Flex;
        }

        public static bool IsActive(this VisualElement element) => VisualElementActive(element);

        public static void HideAllElementChildrens(VisualElement element)
        {
            if (element == null)
            {
                Debug.LogWarning("Visual element is null!");
                return;
            }

            for (int i = 0; i < element.childCount; i++)
                SetActiveElement(element.ElementAt(i), false);
        }

        public static void SetElementClass(VisualElement element, string className, bool value)
        {
            if (element == null)
            {
                Debug.LogWarning("Visual element is null!");
                return;
            }

            if (!value)
                element.RemoveFromClassList(className);
            else if (!element.ClassListContains(className))
                element.AddToClassList(className);
        }

        public static void SetClass(this VisualElement element, string className, bool state) => SetElementClass(element, className, state);

        public static void SetInteractable(this VisualElement element, bool interactable)
        {
            element.pickingMode = interactable ? PickingMode.Position : PickingMode.Ignore;
            element.IterateHierarchy(v => v.pickingMode = interactable ? PickingMode.Position : PickingMode.Ignore);
        }

        public static void RegisterHoverEvent(this VisualElement element, Action<bool> action)
        {
            element.RegisterCallback<MouseEnterEvent>(_ => action?.Invoke(true));
            element.RegisterCallback<MouseLeaveEvent>(_ => action?.Invoke(false));
        }

        public static void RegisterHoverClass(this VisualElement element, string className, VisualElement target = null)
        {
            element.RegisterHoverEvent((active) => (target ?? element).SetClass(className, active));
        }

        #endregion

        #region query

        public static VisualElement GetRoot(this VisualElement element)
        {
            while (element.parent != null)
            {
                element = element.parent;
            }

            return element;
        }

        public static void IterateHierarchy(this VisualElement visualElement, Action<VisualElement> action)
        {
            var stack = new Stack<VisualElement>();
            stack.Push(visualElement);

            while (stack.Count > 0)
            {
                VisualElement currentElement = stack.Pop();
                for (int i = 0; i < currentElement.hierarchy.childCount; i++)
                {
                    VisualElement child = currentElement.hierarchy.ElementAt(i);
                    stack.Push(child);
                    action.Invoke(child);
                }
            }
        }

        #endregion

        #region math

        public static float CountPercent(float current, float max) => max == 0 ? 0 : Mathf.Clamp(current / max, 0f, 1f);
        public static string DisplayedPercent(float percent) => Mathf.RoundToInt(percent * 100) + "%";
        public static string DisplayedPercent(float current, float max) => DisplayedPercent(CountPercent(current, max));

        #endregion

        #region multi-click

        private const float MULTI_CLICKS_RESET_TIME = 0.4f;
        private static readonly Dictionary<MultiClickKey, (int clickCount, float time)> MultiClicks = new();

        public static void RegisterMultiClick(Button button, Action callback, int clickCount = 2)
        {
            MultiClickKey key = new(button, callback, clickCount);

            button.RegisterCallback<ClickEvent>(evt => OnMultiClickButtonClicked(key));

            if (!MultiClicks.ContainsKey(key))
                MultiClicks.Add(key, (0, 0));
        }

        private static void OnMultiClickButtonClicked(MultiClickKey key)
        {
            float clickTime = Time.timeSinceLevelLoad;
            if (MultiClicks.TryGetValue(key, out var value))
            {
                if (clickTime - value.time > MULTI_CLICKS_RESET_TIME)
                    MultiClicks[key] = (1, clickTime);
                else
                {
                    MultiClicks[key] = (value.clickCount + 1, clickTime);
                    if (value.clickCount + 1 >= key.targetClickCount)
                        key.callback.Invoke();
                }
            }
        }

        private struct MultiClickKey
        {
            public Button button;
            public Action callback;
            public int targetClickCount;

            public MultiClickKey(Button button, Action callback, int targetClickCount)
            {
                this.button = button;
                this.callback = callback;
                this.targetClickCount = targetClickCount;
            }
        }

        #endregion

        #region style helpers

        public static void SetBorderWidth(this IStyle style, float width)
        {
            style.borderBottomWidth = width;
            style.borderTopWidth = width;
            style.borderRightWidth = width;
            style.borderLeftWidth = width;
        }

        public static void SetBorderColor(this IStyle style, Color color)
        {
            style.borderBottomColor = color;
            style.borderTopColor = color;
            style.borderRightColor = color;
            style.borderLeftColor = color;
        }

        public static void SetBorderRadius(this IStyle s, float r)
        {
            s.borderTopLeftRadius = r;
            s.borderTopRightRadius = r;
            s.borderBottomLeftRadius = r;
            s.borderBottomRightRadius = r;
        }

        public static void SetPadding(this IStyle style, float width)
        {
            style.paddingBottom = width;
            style.paddingTop = width;
            style.paddingRight = width;
            style.paddingLeft = width;
        }

        public static void SetMargin(this IStyle style, float width)
        {
            style.marginBottom = width;
            style.marginTop = width;
            style.marginRight = width;
            style.marginLeft = width;
        }

        #endregion
    }
}