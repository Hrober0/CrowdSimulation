using System;
using System.Collections.Generic;
using HCore;
using HCore.Shapes;
using HCore.Systems;
using Objects.GenericSystems;
using UnityEngine;

namespace Objects.GenericModules
{
    public class MapObject  : MonoBehaviour, IObject
    {
        private const int START_COMPONENT_CACHE_SIZE = 5;
        private const int EXTEND_COMPONENT_CACHE = 5;

        [SerializeField] private InterfaceField<IBoundsHolder> _boundsHolder;

        private KeyValuePair<Type, IMComponent>[] _modules = new KeyValuePair<Type, IMComponent>[START_COMPONENT_CACHE_SIZE];
        private int _modulesFreeSpaceIndex = 0;

        private ObjectsSystem _objectsSystem;

        public IShape Bounds => _boundsHolder.CachedValue.Bounds;
        public bool IsInitialized { get; private set; } = false;

        public void Init(ISystemManager systems)
        {
            _boundsHolder.CacheValue();
            foreach (var comp in GetComponents<IInitializable>())
            {
                comp.Initialize(systems);
            }

            _objectsSystem = systems.Get<ObjectsSystem>();
            IsInitialized = true;
        }
        public void Deinit()
        {
            foreach (var comp in GetComponents<IInitializable>())
            {
                comp.Deinitialize();
            }

            _objectsSystem.UnregisterObject(this);
            IsInitialized = false;
        }


        public T GetModule<T>() where T : IMComponent
        {
            var type = typeof(T);
            for (int i = 0; i < _modulesFreeSpaceIndex; i++)
            {
                ref var tto = ref _modules[i];
                if (tto.Key == type)
                    return (T)tto.Value;
            }

            var module = GetComponent(type) as IMComponent;
            if (_modulesFreeSpaceIndex == _modules.Length)
            {
                _modules = new KeyValuePair<Type, IMComponent>[_modules.Length + EXTEND_COMPONENT_CACHE];
                _modulesFreeSpaceIndex = 0;
            }
            _modules[_modulesFreeSpaceIndex] = new(type, module);
            _modulesFreeSpaceIndex++;
            return (T)module;
        }
        public bool TryGetModule<T>(out T module) where T : IMComponent
        {
            module = GetModule<T>();
            return module != null;
        }
    }
}