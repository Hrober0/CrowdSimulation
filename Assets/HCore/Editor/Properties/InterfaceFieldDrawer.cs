using UnityEngine;
using System;
using UnityEditor;

namespace HCore.Editor
{
    [CustomPropertyDrawer(typeof(InterfaceField<>))]
    public class InterfaceFieldDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty objectRef = property.FindPropertyRelative("objectRef");
            object propertObject = property.GetValue();
            Type genericType = propertObject.GetType().GetGenericArguments()[0];

            if (objectRef.objectReferenceValue != null && !genericType.IsAssignableFrom(objectRef.objectReferenceValue.GetType()))
            {
                if (objectRef.objectReferenceValue is GameObject gameObject)
                {
                    objectRef.objectReferenceValue = gameObject.GetComponent(genericType);
                    var components = gameObject.GetComponents(genericType);
                    switch (components.Length)
                    {
                        case 0:
                            Debug.LogError($"{gameObject.name} object has no components inherit from {genericType}");
                            objectRef.objectReferenceValue = null;
                            break;
                        case 1:
                            objectRef.objectReferenceValue = components[0];
                            break;
                        default:
                            Debug.LogError($"{gameObject.name} object has more than one component inheriting from {genericType}, selected the first one");
                            objectRef.objectReferenceValue = components[0];
                            break;
                    }
                }
                else
                {
                    Debug.LogError($"{objectRef.objectReferenceValue} component does not inherit from {genericType}");
                    objectRef.objectReferenceValue = null;
                }
            }

            EditorGUI.BeginProperty(position, label, property);
            EditorGUI.PropertyField(position, objectRef, new GUIContent(property.displayName));
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, false);
        }
    }
}