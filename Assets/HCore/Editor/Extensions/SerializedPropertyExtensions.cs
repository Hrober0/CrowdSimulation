using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;

namespace HCore.Editor
{
    public static class SerializedPropertyExtensions
    {
        static readonly Regex rgx = new Regex(@"\[\d+\]", RegexOptions.Compiled);
        public static object GetValue(this SerializedProperty property)
        {
            object obj = property.serializedObject.targetObject;
            string path = property.propertyPath.Replace(".Array.data", "");
            string[] fieldStructure = path.Split('.');
            for (int i = 0; i < fieldStructure.Length; i++)
            {
                if (fieldStructure[i].Contains("["))
                {
                    int index = System.Convert.ToInt32(new string(fieldStructure[i].Where(c => char.IsDigit(c)).ToArray()));
                    obj = GetFieldValueWithIndex(rgx.Replace(fieldStructure[i], ""), obj, index);
                }
                else
                {
                    obj = GetFieldValue(fieldStructure[i], obj);
                }
            }
            return obj;
        }
        private static object GetFieldValueWithIndex(string fieldName, object obj, int index, BindingFlags bindings = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        {
            FieldInfo field = GetField(fieldName, obj, bindings);
            if (field != null)
            {
                object list = field.GetValue(obj);
                if (list.GetType().IsArray)
                {
                    return ((Array)list).GetValue(index);
                }
                if (list is IEnumerable)
                {
                    return ((IList)list)[index];
                }
            }
            return default;
        }
        private static object GetFieldValue(string fieldName, object obj, BindingFlags bindings = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        {
            FieldInfo field = GetField(fieldName, obj, bindings);
            if (field != null)
                return field.GetValue(obj);

            return default;
        }
        private static FieldInfo GetField(string fieldName, object obj, BindingFlags bindings)
        {
            Type type = obj.GetType();
            while (type != null)
            {
                FieldInfo field = type.GetField(fieldName, bindings);
                if (field != null)
                    return field;

                type = type.BaseType;
            }

            return null;
        }

        public static bool IsInArray(this SerializedProperty property, out int index)
        {
            index = -1;
            var splited = property.propertyPath.Split(".Array.data");
            if (splited.Length < 2)
            {
                return false;
            }
            var a = splited[^1].TrimStart('[').TrimEnd(']');
            return int.TryParse(a, out index);
        }
    }
}