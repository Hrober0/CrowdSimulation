using UnityEngine.UIElements;

namespace Examples.Storage.UI
{
    public interface ITab
    {
        void Update();
        void SetActive(bool active);
        VisualElement Content { get; }

        /// <summary>Called every frame regardless of which tab is active.</summary>
        void DrawGizmos() { }
    }
}