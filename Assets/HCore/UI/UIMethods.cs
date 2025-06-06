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

        public enum Direction { Horizontal, Vertical }

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
        public static void RegisterHoverClass(this VisualElement element, string className, VisualElement target=null)
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

        #region assets

        private static readonly Dictionary<string, VisualTreeAsset> LoadedVTAssets = new();

        /// <summary>
        /// Load and cach visual element from resources folder
        /// </summary>
        /// <param name="path">Path in the resources folder, without extension (UI/UIFiles/....)</param>
        public static VisualTreeAsset LoadVTAsset(string path)
        {
            if (LoadedVTAssets.TryGetValue(path, out VisualTreeAsset asset))
                return asset;

            asset = UnityEngine.Resources.Load<VisualTreeAsset>(path);
            if (asset == null)
                Debug.LogWarning("Asset not found at " + path);

            LoadedVTAssets.Add(path, asset);
            return asset;
        }

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

        #region elements names

        public static string ScrollViewContent => "unity-content-container";

        #endregion

        #region elements creation

        public static VisualElement NewSpace(VisualElement root, float space = LINE_SPACE)
        {
            var element = new VisualElement();
            element.style.marginBottom = space;
            element.style.marginRight = space;
            root.Add(element);
            return element;
        }

        public static Button NewButton(VisualElement root, string text, Action onClick)
        {
            var button = new Button { text = text };
            button.RegisterCallback<ClickEvent>(evt => onClick());
            root.Add(button);
            return button;
        }

        public static Label NewLabel(VisualElement root, string text)
        {
            var label = new Label { text = text };
            root.Add(label);
            label.style.marginLeft = 2;
            return label;
        }

        public static (Label name, Label content) NewLabel(VisualElement root, string name, object content, int space = DEFAULT_NAME_WIDTH)
        {
            var group = NewHorizontalGroup(root);
            group.style.marginTop = 2;
            group.style.marginBottom = 2;
            var nameLabel = NewLabel(group, name);
            nameLabel.style.minWidth = space;
            var contentLabel = NewLabel(group, content.ToString());
            return (nameLabel, contentLabel);
        }

        public static Label NewHeader(VisualElement root, string text)
        {
            var label = NewLabel(root, $"<b>{text}</b>");
            label.style.marginBottom = 2;
            label.style.marginTop = 12;
            return label;
        }

        public static TextField NewTextField(VisualElement root, string text, string value, Action<string> onChange = null)
        {
            var field = new TextField(text);
            field.value = value;
            if (onChange != null)
                field.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            root.Add(field);
            return field;
        }

        public static Toggle NewToggle(VisualElement root, string text, Action<bool> onChange = null, int labelWidth = DEFAULT_NAME_WIDTH, bool defaultValue = false)
        {
            var group = NewHorizontalGroup(root);
            var label = NewLabel(group, text);
            label.style.minWidth = labelWidth;
            var toggle = new Toggle();
            toggle.value = defaultValue;
            toggle.RegisterValueChangedCallback(evt => onChange?.Invoke(evt.newValue));
            group.Add(toggle);
            return toggle;
        }

        public static VisualElement NewContainer(VisualElement root)
        {
            var container = new VisualElement();
            SetStyleContainer(container);
            root.Add(container);
            return container;
        }

        public static VisualElement NewHorizontalGroup(VisualElement root)
        {
            var group = new VisualElement();
            group.style.flexDirection = FlexDirection.Row;
            group.style.flexShrink = 0;
            root.Add(group);
            return group;
        }

        #endregion

        #region style

        public static void SetStyleContainer(VisualElement element)
        {
            var style = element.style;
            style.flexShrink = 0;
            style.SetMargin(5);
            style.SetPadding(5);
            style.SetBorderWidth(1);
            style.SetBorderColor(UIColors.EditorBorder);
            style.backgroundColor = UIColors.EditorContent;
        }

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
