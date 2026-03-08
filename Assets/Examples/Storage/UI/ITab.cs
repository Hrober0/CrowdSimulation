using UnityEngine.UIElements;

namespace Examples.Storage.UI
{
    public interface ITab
    {
        void Update();
        void SetActive(bool active);
        VisualElement Content { get; }
    }
}