using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HCore
{
    [Serializable]
    public struct TypeField<T>
    {
        [SerializeField]
        private string _assemblyQualifiedName;

        private Type _cashedType;

        public Type Value
        {
            get
            {
                if (_cashedType == null)
                {
                    _cashedType = TypeFiledOperations.GetSystemType(_assemblyQualifiedName);
                }
                return _cashedType;
            }
        }

        public TypeField(Type type)
        {
            _cashedType = type;
            _assemblyQualifiedName = TypeFiledOperations.GetTypeNameFromType(type);
        }

        public bool HasValue => Value != null;

        public static implicit operator Type(TypeField<T> field) => field.Value;
    }

    public static class TypeFiledOperations
    {
        public static Type GetSystemType(string assemblyQualifiedName, bool logError = true)
        {
            if (string.IsNullOrEmpty(assemblyQualifiedName))
                return null;

            var type = Type.GetType(assemblyQualifiedName);
            if (type != null)
                return type;

            if (logError)
                Debug.LogError($"Type defined by string {assemblyQualifiedName} is missing");
            return null;
        }

        public static string GetTypeNameFromType(Type type) => type == null ? null : type.AssemblyQualifiedName;
    }
}