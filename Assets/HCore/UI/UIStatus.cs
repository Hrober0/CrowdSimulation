using System;
using System.Collections;
using System.Collections.Generic;
using HCore.Extensions;
using UnityEngine;

namespace HCore.UI
{
    public static class UIStatus
    {
        private static readonly Dictionary<Type, UIBahaviour> _uiByType = new();

        private static readonly Dictionary<Type, List<Action<bool>>> _onOpenChangeCallbacks = new();

        public static void RegisterUI(UIBahaviour ui)
        {
            if (_uiByType.TryAdd(ui.GetType(), ui))
            {
                if (ui is IUIOpenable openable)
                {
                    UpdateOpenStateCallbacks(openable);
                }
            }
            else
            {
                Debug.LogWarning($"Multiple occurrence of {ui.GetType()}");
            }
        }
        public static void UnRegisterUI(UIBahaviour ui)
        {
            var typesToRemove = new List<Type>();
            foreach (var item in _uiByType)
            {
                if (item.Value == ui)
                {
                    typesToRemove.Add(item.Key);
                }
            }
            foreach (var type in typesToRemove)
            {
                _uiByType.Remove(type);
            }
            if (typesToRemove.Count == 0)
            {
                Debug.LogWarning($"UI of {ui.GetType()} was not register");
            }
        }
        
        public static void RegisterOpenChange<T>(Action<bool> callback) where T : UIBahaviour
        {
            List<Action<bool>> callbacks = _onOpenChangeCallbacks.GetOrAddDefault(typeof(T), () => new());
            if (callbacks.Contains(callback))
            {
                Debug.LogWarning("UI open change callback is already register");
            }
            else
            {
                callbacks.Add(callback);
            }
        }
        public static void UnRegisterOpenChange<T>(Action<bool> callback)
        {
            if (_onOpenChangeCallbacks.TryGetValue(typeof(T), out List<Action<bool>> callbacks))
            {
                if (!callbacks.Remove(callback))
                {
                    Debug.LogWarning("UI open change callback is not register");
                }
            }
            else
            {
                Debug.LogWarning($"UI open change callback type {typeof(T)} is not register");
            }
        }

        public static T Get<T>() where T : UIBahaviour
        {
            if (_uiByType.TryGetValue(typeof(T), out var ui))
            {
                if (ui == null)
                {
                    Debug.LogError($"UI {typeof(T)} is not register");
                    return default;
                }

                if (ui is T tui)
                    return tui;
            }

            foreach (var newUI in _uiByType.Values)
            {
                if (newUI is T newTUI)
                {
                    _uiByType.Add(typeof(T), newTUI);
                    return newTUI;
                }
            }

            Debug.LogError($"UI {typeof(T)} is not register");
            return default;
        }

        public static void UpdateOpenStateCallbacks(IUIOpenable ui)
        {
            if (_onOpenChangeCallbacks.TryGetValue(ui.GetType(), out var callbacks))
            {
                foreach (var callback in callbacks)
                {
                    callback(ui.IsOpen);
                }
            }
        }
    }
}
