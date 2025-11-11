using System;
using System.Collections.Generic;
using HCore.Systems;
using UnityEngine;

namespace Objects.GenericSystems
{
    public class ObjectsSystem : MonoBehaviour, ISystem
    {
        public event Action<IObject, int> OnObjectRegisteredInit;
        public event Action<IObject> OnObjectRegistered;
        
        public event Action<IObject, int> OnObjectUnregisteredInit;
        public event Action<IObject> OnObjectUnregistered;
        
        public event Action<int> OnObjectsCapacityChanged;
        
        private const int DEFAULT_CAPACITY = 200;
        private const int ADD_CAPACITY = 100;

        private readonly Stack<int> _freeObjectsIndexes = new();
        
        public IObject[] Objects;
        public float ChunkSize => 5f;
        public int DefaultCapacity => DEFAULT_CAPACITY;

        void IInitializable.Initialize(ISystemManager systems)
        {
            Objects = new IObject[DEFAULT_CAPACITY];
            for (int i = DEFAULT_CAPACITY - 1; i >= 0; i--)
            {
                _freeObjectsIndexes.Push(i);
            }
        }
        void IInitializable.Deinitialize()
        {
        }

        public IEnumerable<IObject> GetAllObjects()
        {
            for (int i = 0; i < Objects.Length; i++)
            {
                if (Objects[i] != null)
                {
                    yield return Objects[i];
                }    
            }
        }

        public void RegisterObject(IObject obj)
        {
            if (TryGetIndex(obj, out _))
            {
                Debug.LogWarning($"Object is already register");
                return;
            }

            if (!_freeObjectsIndexes.TryPop(out int index))
            {
                index = Objects.Length;
                var newSize = index + ADD_CAPACITY;

                Array.Resize(ref Objects, newSize);
                OnObjectsCapacityChanged?.Invoke(newSize);

                for (int i = newSize - 1; i > index; i--)
                {
                    _freeObjectsIndexes.Push(i);
                }
            }

            Objects[index] = obj;

            OnObjectRegisteredInit?.Invoke(obj, index);
            OnObjectRegistered?.Invoke(obj);
        }
        public void UnregisterObject(IObject obj)
        {
            if (!TryGetIndex(obj, out int index))
            {
                Debug.LogWarning($"Object is not register");
                return;
            }

            Objects[index] = null;
            _freeObjectsIndexes.Push(index);

            OnObjectUnregisteredInit?.Invoke(obj, index);
            OnObjectUnregistered?.Invoke(obj);
        }

        private bool TryGetIndex(IObject obj, out int index)
        {
            for (int i = 0; i < Objects.Length; i++)
            {
                if (Objects[i] == obj)
                {
                    index = i;
                    return true;
                }
            }
            index = -1;
            return false;
        }
    }
}