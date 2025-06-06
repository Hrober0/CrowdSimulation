using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using System;

namespace HCore.Editor
{
    [CustomPropertyDrawer(typeof(TypeField<>))]
    public class TypeFieldDrawer : PropertyDrawer
    {
        private readonly struct TypePopupCache
        {
            public AdvancedTypePopup TypePopup { get; }
            public AdvancedDropdownState State { get; }

            public TypePopupCache(AdvancedTypePopup typePopup, AdvancedDropdownState state)
            {
                TypePopup = typePopup;
                State = state;
            }
        }

        private const int MAX_TYPE_POPUP_LINE = 13;

        private readonly Dictionary<string, TypePopupCache> _typePopups = new();
        private readonly Dictionary<Type, GUIContent> _typeNameCaches = new();
        private readonly GUIContent _noneTypeContent = new("<none>");
        private readonly GUIContent _errorTypeContent = new("<error>");

        private SerializedProperty _typeNameRef;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            object propertObject = property.GetValue();
            Type baseType = propertObject.GetType().GetGenericArguments()[0];

            var typeNameRef = property.FindPropertyRelative("_assemblyQualifiedName");
            string typeName = typeNameRef.stringValue;
            Type currentType = TypeFiledOperations.GetSystemType(typeName, false);
            var content = currentType switch
            {
                null when !string.IsNullOrEmpty(typeName) => _errorTypeContent,
                null => _noneTypeContent,
                _ => GetTypeNameContent(currentType)
            };

            var popupPosition = new Rect(position);
            popupPosition.width -= EditorGUIUtility.labelWidth;
            popupPosition.x += EditorGUIUtility.labelWidth;
            popupPosition.height = EditorGUIUtility.singleLineHeight;

            EditorGUI.BeginProperty(position, label, property);

            if (EditorGUI.DropdownButton(popupPosition, content, FocusType.Keyboard))
            {
                TypePopupCache popup = GetTypePopup(baseType);
                _typeNameRef = typeNameRef;
                popup.TypePopup.Show(popupPosition);
            }
            
            if (property.IsInArray(out var index))
            {
                label.text = $"Element {index}";
            }
            EditorGUI.LabelField(position, label);

            EditorGUI.EndProperty();
        }

        private TypePopupCache GetTypePopup(Type baseType)
        {
            var typeName = HFormat.GetTypeName(baseType);

            if (_typePopups.TryGetValue(typeName, out TypePopupCache result))
                return result;
            
            var state = new AdvancedDropdownState();

            var types = new List<Type>();
            foreach (var p in TypeCache.GetTypesDerivedFrom(baseType))
            {
                if ((p.IsPublic || p.IsNestedPublic) && !p.IsGenericType)
                    types.Add(p);
            }

            var popup = new AdvancedTypePopup(types, MAX_TYPE_POPUP_LINE, state);
            popup.OnItemSelected += item =>
            {
                var type = item.Type;
                var name = TypeFiledOperations.GetTypeNameFromType(type);

                _typeNameRef.stringValue = name;
                _typeNameRef.serializedObject.ApplyModifiedProperties();
                _typeNameRef.serializedObject.Update();
            };

            result = new TypePopupCache(popup, state);
            _typePopups.Add(typeName, result);
            return result;
        }

        private GUIContent GetTypeNameContent(Type type)
        {
            if (_typeNameCaches.TryGetValue(type, out var result))
                return result;

            var typeName = HFormat.GetTypeName(type);
            result = new GUIContent(typeName);
            _typeNameCaches.Add(type, result);
            return result;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, false);
        }
    }
}