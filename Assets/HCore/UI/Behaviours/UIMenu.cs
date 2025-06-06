using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using HCore;

namespace HCore.UI
{
    public interface IUIMenuOpenChange : IEventHandler
    {
        void OnMenuOpenChange(UIMenu menu);
    }

    public abstract class UIMenu : UIBahaviour, IUIOpenable
    {
        protected VisualElement _main;

        protected Coroutine _sUpdate = null;
        private readonly WaitForSeconds _sUpdateDelay = new(0.2f);

        public bool IsOpen { get; private set; } = false;
        public bool IgnoreSubMenu { get; protected set; } = false;
        public bool UseSUpdateOnOpen { get; protected set; } = false;

        public override void Initialize(VisualElement root)
        {
            base.Initialize(root);
            UIStatus.RegisterUI(this);
        }
        public override void Deinitialize()
        {
            base.Deinitialize();
            UIStatus.UnRegisterUI(this);
        }

        public virtual void Open()
        {
            UIMethods.SetActiveElement(_main, true);
            IsOpen = true;

            if (UseSUpdateOnOpen)
            {
                StartSUpdate();
            }

            EventBus.Invoke<IUIMenuOpenChange>(e => e.OnMenuOpenChange(this));
            UIStatus.UpdateOpenStateCallbacks(this);
        }
        public virtual void Close()
        {
            UIMethods.SetActiveElement(_main, false);
            IsOpen = false;

            if (UseSUpdateOnOpen)
                StopSUpdate();

            EventBus.Invoke<IUIMenuOpenChange>(e => e.OnMenuOpenChange(this));
            UIStatus.UpdateOpenStateCallbacks(this);
        }

        public void Open(bool open)
        {
            if (open)
            {
                Open();
            }
            else
            {
                Close();
            }
        }


        private void StartSUpdate()
        {
            _sUpdate ??= StartCoroutine(SUpdate());
        }
        protected void StopSUpdate()
        {
            if (_sUpdate != null)
            {
                StopCoroutine(_sUpdate);
                _sUpdate = null;
            }
        }
        private IEnumerator SUpdate()
        {
            OnSUpdate();

            yield return null;

            while (true)
            {
                OnSUpdate();
                yield return _sUpdateDelay;
            }
        }
        protected virtual void OnSUpdate() { }
    }
}