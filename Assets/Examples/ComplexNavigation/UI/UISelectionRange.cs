using System;
using ComplexNavigation;
using UnityEngine;

namespace Examples.ComplexNavigation.UI
{
    public class UISelectionRange : MonoBehaviour
    {
        [SerializeField] private RectTransform _selectionRect;
        [SerializeField] private Canvas _canvas;

        private Vector2 _startPosition;

        private void OnEnable()
        {
            AgentSelectionManager.OnSelectStart += ShowSelection;
            AgentSelectionManager.OnSelectPerforming += UpdateSelection;
            AgentSelectionManager.OnSelectionEnd += HideSelection;
        }

        private void OnDisable()
        {
            AgentSelectionManager.OnSelectStart -= ShowSelection;
            AgentSelectionManager.OnSelectPerforming -= UpdateSelection;
            AgentSelectionManager.OnSelectionEnd -= HideSelection;
        }

        private void ShowSelection(Vector2 worldPosition)
        {
            _startPosition = MousePosition.GetScreenPositionFromWorld(worldPosition);
            UpdateSelection(worldPosition);
            _selectionRect.gameObject.SetActive(true);
        }

        private void UpdateSelection(Vector2 worldPosition)
        {
            var canvasScale = _canvas.transform.localScale.x;
            var end = MousePosition.GetScreenPositionFromWorld(worldPosition);
            var center = (_startPosition + end) * 0.5f;
            var size = new Vector2(Mathf.Abs(end.x - _startPosition.x), Mathf.Abs(end.y - _startPosition.y));
            _selectionRect.anchoredPosition = (center - size * 0.5f) / canvasScale;
            _selectionRect.sizeDelta = size / canvasScale;
        }

        private void HideSelection(Vector2 _)
        {
            _selectionRect.gameObject.SetActive(false);
        }
    }
}