using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HCore.Systems
{
    public class MonoBehaviourSystemManager : MonoBehaviour, ISystemManager
    {
        [SerializeField]
        private List<InterfaceField<ISystem>> _systemsOrdered;

        private readonly Dictionary<Type, ISystem> _systems = new();

        public bool IsOperational { get; private set; } = false;

        private bool _isInitialize = false;

        private void Start()
        {
            StartCoroutine(Initialize());
        }
        
        private void OnDestroy()
        {
            Deinitilize();
        }

        protected virtual IEnumerator Initialize()
        {
            if (_isInitialize)
            {
                Debug.LogWarning($"{typeof(MonoBehaviourSystemManager)} is or was initialized");
                yield break;
            }

            _isInitialize = true;

            yield return null;

            Debug.Log("=== <b>Gameplay System Initialization</b> ===");
            foreach (var systemWraper in _systemsOrdered)
            {
                var system = systemWraper.Value;
                if (!_systems.TryAdd(system.GetType(), system))
                {
                    Debug.LogWarning($"Duplicated {system.GetType()} system");
                }
                else
                {
                    Debug.Log($"<b>Initialization...</b> {system.GetType()}");
                    system.Initialize(this);
                }
            }

            IsOperational = true;
        }
        
        private void Deinitilize()
        {
            IsOperational = false;
            Debug.Log("=== <b>Gameplay System Deinitilization</b> ===");
            for (int i = _systemsOrdered.Count - 1; i >= 0 ; i--)
            {
                Debug.Log($"<b>Deinitilization...</b> {_systemsOrdered[i].Value.GetType()}");
                _systemsOrdered[i].Value.Deinitialize();
            }
            _systems.Clear();
            _isInitialize = false;
        }

        // ReSharper disable Unity.PerformanceAnalysis
        public TSystem Get<TSystem>() where TSystem : ISystem
        {
            if (_systems.TryGetValue(typeof(TSystem), out var system))
            {
                if (system == null)
                {
                    Debug.LogError($"System {typeof(TSystem)} is no exist in {name}", this);
                    return default;
                }

                if (system is TSystem tsys)
                    return tsys;
            }

            Debug.LogError($"System {typeof(TSystem)} is no exist in {name}", this);
            return default;
        }
    }
}