using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace HCore.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class UIGroup : MonoBehaviour
    {
        private UIBahaviour[] _behaviour;
        private bool _isInitialize = false;

        protected virtual void Start()
        {
            Initialize();
        }
        public void Initialize()
        {
            if (_isInitialize)
            {
                Debug.LogWarning($"{name} UIGroup is already Initialize");
                return;
            }

            _behaviour = GetComponentsInChildren<UIBahaviour>();

            var document = GetComponent<UIDocument>();
            var root = document.rootVisualElement;

            foreach (var item in _behaviour)
            {
                if (!item.IsInit)
                {
                    item.Initialize(root);
                }
            }

            Debug.Log($"Initialized: {_behaviour.Length} menus in {name}");
            _isInitialize = true;
        }

        private void OnDestroy()
        {
            Desincilize();
        }
        private void Desincilize()
        {
            if (!_isInitialize)
            {
                Debug.LogWarning($"{name} UIGroup is already Desincilize");
                return;
            }

            foreach (var item in _behaviour)
            {
                item.Deinitialize();
            }

            _isInitialize = false;
        }
    }
}