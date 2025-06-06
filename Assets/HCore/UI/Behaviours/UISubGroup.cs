using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace HCore.UI
{
    public class UISubGroup : UIBahaviour, IUIMenuOpenChange
    {
        public enum CloseMode { DontClose, CloseWithMenu, CloseWithDefaultMenu }
        
        public event Action OnSubMenuOpenChange;
        public event Action<bool> OnOpenChange;
        
        [SerializeField] private UIMenu _defaultMenu;
        [SerializeField] private CloseMode _closeMode = CloseMode.DontClose;
        [SerializeField] private bool _manageUiOnTheSameLevel = true;

        private readonly List<UIMenu> _subMenus = new();

        public bool IsOpen { get; private set; } = false;
        private UIMenu _lastOpenMenu = null;

        public override void Initialize(VisualElement root)
        {
            base.Initialize(root);
            
            foreach (var menu in GetComponentsInChildren<UIMenu>())
            {
                if (_manageUiOnTheSameLevel || menu.transform != transform)
                {
                    _subMenus.Add(menu);
                }
            }

            EventBus.RegisterHandler<IUIMenuOpenChange>(this);
        }
        public override void Deinitialize()
        {
            base.Deinitialize();

            EventBus.UnregisterHandler<IUIMenuOpenChange>(this);
        }

        void IUIMenuOpenChange.OnMenuOpenChange(UIMenu changedMenu)
        {
            if (changedMenu.IgnoreSubMenu)
            {
                return;
            }

            if (!_subMenus.Contains(changedMenu))
            {
                return;
            }
            
            //Debug.Log($"OnMenuOpenChange: {changedMenu} {changedMenu.IsOpen}");
            if (changedMenu.IsOpen)
            {
                foreach (var subMenu in _subMenus)
                {
                    if (changedMenu != subMenu && subMenu.IgnoreSubMenu == false && subMenu.IsOpen)
                    {
                        subMenu.Close();
                    }
                }

                _lastOpenMenu = changedMenu;
                IsOpen = true;
                OnOpenChange?.Invoke(true);
            }
            else
            {
                if (!IsOpen || _defaultMenu == null)
                {
                    return;
                }

                if (_subMenus.Find(subMenu => subMenu.IsOpen))
                {
                    return;
                }

                switch (_closeMode)
                {
                    case CloseMode.DontClose:
                        if (_defaultMenu != null)
                        {
                            _defaultMenu.Open();
                        }
                        break;
                    case CloseMode.CloseWithDefaultMenu:
                        if (_defaultMenu == changedMenu)
                        {
                            Close();
                        }
                        break;
                    case CloseMode.CloseWithMenu:
                        Close();
                        break;
                }
            }
            
            OnSubMenuOpenChange?.Invoke();
        }

        public void Open(bool openDefault = false)
        {
            if (openDefault && _defaultMenu != null && !_defaultMenu.IsOpen)
            {
                _defaultMenu.Open();
            }
            else if (_lastOpenMenu != null && !_lastOpenMenu.IsOpen)
            {
                _lastOpenMenu.Open();
            }
            else if (_defaultMenu != null && !_defaultMenu.IsOpen)
            {
                _defaultMenu.Open();
            }

            IsOpen = true;
            OnOpenChange?.Invoke(true);
        }
        public void Close()
        {
            IsOpen = false;
            if (_lastOpenMenu != null && _lastOpenMenu.IsOpen)
            {
                _lastOpenMenu.Close();
            }
            OnOpenChange?.Invoke(false);
        }
    }
}
