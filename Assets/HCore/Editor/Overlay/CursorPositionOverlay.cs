using UnityEditor.Overlays;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace HCore.Editor
{
    [Overlay(typeof(SceneView), "Cursor position", true)]
    public class CursorPositionOverlay : Overlay
    {
        private Label _cordinatsLabel;
        private bool _isActive = false;

        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement() { name = "Cursor" };

            _cordinatsLabel = new Label()
            {
                style =
                {
                    fontSize = 15,
                    unityTextAlign = TextAnchor.MiddleCenter
                }
            };
            root.Add(_cordinatsLabel);
            UpdateVisibility(true);
            collapsed = false;
            return root;
        }

        public override void OnCreated()
        {
            base.OnCreated();
            displayedChanged += UpdateVisibility;
        }

        public override void OnWillBeDestroyed()
        {
            base.OnWillBeDestroyed();
            UpdateVisibility(false);
            displayedChanged -= UpdateVisibility;
        }

        private void UpdateVisibility(bool isVisable)
        {
            if (_isActive != isVisable)
            {
                _isActive = isVisable;
                if (isVisable)
                {
                    SceneView.duringSceneGui += UpdateLabel;
                }
                else
                {
                    SceneView.duringSceneGui -= UpdateLabel;
                }
            }
        }

        private void UpdateLabel(SceneView _)
        {
            var pos = GetMouseWorldPosition3d();
            _cordinatsLabel.text = $"{pos.x:F2} {pos.y:F2}";
        }

        private Vector3 GetMouseWorldPosition2d()
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            return ray.origin;
        }
        
        private Vector3 GetMouseWorldPosition3d()
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            var plane = new Plane(Vector3.up, Vector3.up * 0);
            plane.Raycast(ray, out float distance);
            var pos = ray.GetPoint(distance);
            return pos;
        }
    }
}