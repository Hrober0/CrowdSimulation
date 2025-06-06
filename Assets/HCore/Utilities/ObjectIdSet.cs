using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HCore
{
    public static class ObjectIdSet
    {
        public const int DEFAULT_ID = 0;

        private static readonly Dictionary<int, IAutoId> _loadedObjects = new();
        private static readonly HashSet<Type> _checkedType = new();

        public static T GetObject<T>(string idStr, bool logNull = true) where T : Object, IAutoId
        {
            if (int.TryParse(idStr, out var id))
                return GetObject<T> (id, logNull);

            Debug.LogWarning($"Id {idStr} is not int");
            return default;
        }
        public static T GetObject<T>(int id, bool logNull = true) where T : Object, IAutoId
        {
            if (_loadedObjects.TryGetValue(id, out var obj))
            {
                if (obj is T objT)
                    return objT;
                
                if (logNull)
                    Debug.LogWarning($"Object of {id} id is not of type {typeof(T)}");
                
                return default;
            }


            var type = typeof(T);
            if (_checkedType.Contains(type))
            {
                if (logNull)
                    Debug.LogWarning($"Object of {type} with id {id} not found!");

                return default;
            }

            var objects = Resources.LoadAll<T>("");
            foreach (var nObj in objects)
            {
                if (!_loadedObjects.TryAdd(nObj.Id, nObj))
                {
                    var existingObj = _loadedObjects[nObj.Id];

                    if (nObj != existingObj)
                    {
                        Debug.LogWarning($"Duplicated id {nObj.Id} in {nObj.name} and {(existingObj as Object).name}");
                    }
                }
            }

            _checkedType.Add(type);

            return GetObject<T>(id, logNull);
        }

        public static bool TryGetObject<T>(string id, out T obj) where T : Object, IAutoId
        {
            obj = GetObject<T>(id, false);
            return obj != null;
        }
        public static bool TryGetObject<T>(int id, out T obj) where T : Object, IAutoId
        {
            obj = GetObject<T>(id, false);
            return obj != null;
        }
    }
}