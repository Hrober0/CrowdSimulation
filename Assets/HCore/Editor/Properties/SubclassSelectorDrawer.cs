using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;

namespace HCore.Editor
{
	[CustomPropertyDrawer(typeof(SubclassSelectorAttribute))]
	public class SubclassSelectorDrawer : PropertyDrawer
	{
        private readonly struct TypePopupCache
		{
			public AdvancedTypePopup TypePopup { get; }
			public AdvancedDropdownState State { get; }

			public TypePopupCache (AdvancedTypePopup typePopup,AdvancedDropdownState state)
			{
				TypePopup = typePopup;
				State = state;
			}
		}

        private const int maxTypePopupLineCount = 13;
        private static readonly Type unityObjectType = typeof(UnityEngine.Object);
        private static readonly GUIContent nullDisplayName = new(TypeMenuUtility.k_NullDisplayName);
        private static readonly GUIContent isNotManagedReferenceLabel = new("The property type is not manage reference.");

        private readonly Dictionary<string, TypePopupCache> typePopups = new();
        private readonly Dictionary<string, GUIContent> typeNameCaches = new();

		private SerializedProperty targetProperty;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			EditorGUI.BeginProperty(position,label,property);

			if (property.propertyType == SerializedPropertyType.ManagedReference)
			{
				// Draw the subclass selector popup.
				var popupPosition = new Rect(position);
				popupPosition.width -= EditorGUIUtility.labelWidth;
				popupPosition.x += EditorGUIUtility.labelWidth;
				popupPosition.height = EditorGUIUtility.singleLineHeight;

                if (EditorGUI.DropdownButton(popupPosition, GetTypeName(property), FocusType.Keyboard))
				{
					TypePopupCache popup = GetTypePopup(property);
					targetProperty = property;
					popup.TypePopup.Show(popupPosition);
				}

                // Draw the managed reference property.
                EditorGUI.PropertyField(position, property, label, true);
			}
			else
			{
                EditorGUI.LabelField(position, label, isNotManagedReferenceLabel);
			}

			EditorGUI.EndProperty();
		}

        private TypePopupCache GetTypePopup(SerializedProperty property)
		{
			// Cache this string. This property internally call Assembly.GetName, which result in a large allocation.
			string managedReferenceFieldTypename = property.managedReferenceFieldTypename;

			if (!typePopups.TryGetValue(managedReferenceFieldTypename,out TypePopupCache result))
			{
				var state = new AdvancedDropdownState();
				
				Type baseType = ManagedReferenceUtility.GetType(managedReferenceFieldTypename);
				var popup = new AdvancedTypePopup(
					TypeCache.GetTypesDerivedFrom(baseType).Append(baseType).Where(p =>
						(p.IsPublic || p.IsNestedPublic) &&
						!p.IsAbstract &&
						!p.IsGenericType &&
						!unityObjectType.IsAssignableFrom(p) &&
						Attribute.IsDefined(p,typeof(SerializableAttribute))
					),
					maxTypePopupLineCount,
					state
				);
				popup.OnItemSelected += item =>
				{
					Type type = item.Type;
					object obj = targetProperty.SetManagedReference(type);
					targetProperty.isExpanded = (obj != null);
					targetProperty.serializedObject.ApplyModifiedProperties();
					targetProperty.serializedObject.Update();
				};

				result = new TypePopupCache(popup, state);
				typePopups.Add(managedReferenceFieldTypename, result);
			}
			return result;
		}

		private GUIContent GetTypeName(SerializedProperty property)
		{
			// Cache this string.
			string managedReferenceFullTypename = property.managedReferenceFullTypename;

			if (string.IsNullOrEmpty(managedReferenceFullTypename))
                return nullDisplayName;

            if (typeNameCaches.TryGetValue(managedReferenceFullTypename,out GUIContent cachedTypeName))
                return cachedTypeName;

            Type type = ManagedReferenceUtility.GetType(managedReferenceFullTypename);
			string typeName = null;

			AddTypeMenuAttribute typeMenu = TypeMenuUtility.GetAttribute(type);
			if (typeMenu != null)
			{
				typeName = typeMenu.GetTypeNameWithoutPath();
				if (!string.IsNullOrWhiteSpace(typeName))
				{
					typeName = ObjectNames.NicifyVariableName(typeName);
				}
			}

			if (string.IsNullOrWhiteSpace(typeName))
			{
				typeName = ObjectNames.NicifyVariableName(type.Name);
			}

			var result = new GUIContent(typeName);
			typeNameCaches.Add(managedReferenceFullTypename,result);
			return result;
		}

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			return EditorGUI.GetPropertyHeight(property, true);
		}
	}
}