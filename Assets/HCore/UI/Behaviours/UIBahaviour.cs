using UnityEngine;
using UnityEngine.UIElements;

namespace HCore.UI
{
    public class UIBahaviour : MonoBehaviour
    {
        protected VisualElement _root;

        public bool IsInit { get; private set; } = false;


        public virtual void Initialize(VisualElement root)
        {
            IsInit = true;
            _root = root;
        }
        public virtual void Deinitialize()
        {
            IsInit = false;
        }
    }
}