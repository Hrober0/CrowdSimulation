using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace HCore.UI
{
    public static class UIMethodsEditor
    {
        public const float HORIZONTAL_SPACING = 2f;

        #region create

        public static IntegerField NewIntField(VisualElement root, string text, int value, Action<int> onChange = null)
        {
            var field = new IntegerField(text);
            field.value = value;
            if (onChange != null)
                field.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            root.Add(field);
            return field;
        }

        public static FloatField NewFloatField(VisualElement root, string text, float value, Action<float> onChange = null)
        {
            var field = new FloatField(text);
            field.value = value;
            if (onChange != null)
                field.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            root.Add(field);
            return field;
        }

        public static (Label, Slider, FloatField) NewSliderHorizontalFull(VisualElement root, string text, float min, float max, Action<float> onChange = null, int labelWidth = UIMethods.DEFAULT_NAME_WIDTH)
        {
            var group = UIMethods.NewHorizontalGroup(root);
            var label = UIMethods.NewLabel(group, text);
            label.style.minWidth = labelWidth;
            var slider = new Slider(min, max, SliderDirection.Horizontal);
            slider.RegisterValueChangedCallback(evt => onChange?.Invoke(evt.newValue));
            slider.style.flexGrow = 1;
            group.Add(slider);
            var field = NewFloatField(group, "", slider.value, (value) => {
                value = Mathf.Clamp(value, min, max);
                slider.value = value;
            });
            field.style.minWidth = UIMethods.SMALL_INPUT_WIDTH;
            slider.RegisterValueChangedCallback(evt => field.SetValueWithoutNotify(evt.newValue));
            return (label, slider, field);
        }
        public static Slider NewSliderHorizontal(VisualElement root, string text, float min, float max, Action<float> onChange = null, int labelWidth = UIMethods.DEFAULT_NAME_WIDTH)
        {
            var (_, slider, _) = NewSliderHorizontalFull(root, text, min, max, onChange, labelWidth);
            return slider;
        }

        public static Label NewTitle(VisualElement root, string text, int size = 19)
        {
            var label = UIMethods.NewLabel(root, $"<b>{text}</b>");
            label.style.marginBottom = 2;
            label.style.fontSize = size;
            return label;
        }

        public static VisualElement NewSettingsContent(VisualElement root, string title)
        {
            var content = new VisualElement();
            root.style.paddingLeft = 7;
            root.style.paddingTop = 2;
            root.Add(content);
            NewTitle(content, title, 19);
            return content;
        }

        #endregion

        public static IEnumerable<T> GetObjects<T>() where T : UnityEngine.Object
        {
            var t = typeof(T);
            var guids = AssetDatabase.FindAssets($"t: {t}");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = (T)AssetDatabase.LoadAssetAtPath(path, t);
                yield return asset;
            }
        }

        public static bool IsLastElementInList(SerializedProperty property)
        {
            if (!IsPartOfList(property))
                return false;

            string propertyPath = property.propertyPath;
            int elementIndex = GetElementIndex(propertyPath);

            SerializedProperty parentList = GetParentList(property);

            return elementIndex == parentList.arraySize - 1;
        }

        public static bool IsPartOfList(SerializedProperty property)
        {
            return property.propertyPath.Contains("[") && property.propertyPath.Contains("]");
        }

        public static int GetElementIndex(string propertyPath)
        {
            int startIndex = propertyPath.LastIndexOf('[') + 1;
            int endIndex = propertyPath.LastIndexOf(']');
            string indexString = propertyPath.Substring(startIndex, endIndex - startIndex);

            if (int.TryParse(indexString, out int index))
                return index;

            return -1;
        }

        public static SerializedProperty GetParentList(SerializedProperty property)
        {
            string propertyPath = property.propertyPath;
            int lastDotIndex = propertyPath.LastIndexOf('.');
            string parentPath = propertyPath.Substring(0, lastDotIndex);

            return property.serializedObject.FindProperty(parentPath);
        }

        public static float GetIndentLength(Rect sourceRect)
        {
            Rect indentRect = EditorGUI.IndentedRect(sourceRect);
            float indentLength = indentRect.x - sourceRect.x;

            return indentLength;
        }
    }
}