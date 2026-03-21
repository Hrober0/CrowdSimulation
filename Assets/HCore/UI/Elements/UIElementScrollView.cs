using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Direction = HCore.UI.UIMethods.Direction;

namespace HCore.UI
{
    public class UIElementScrollView<T> : UIElementList<T> where T : UIElement, new()
    {
        public const float DEFAULT_SCROLL_MOVE = 10f;
        private readonly Direction _direction;

        private readonly ScrollView _scrollView;

        public UIElementScrollView(ScrollView scrollView, VisualTreeAsset pattern, Direction direction = Direction.Horizontal,
                                   Action<T> onCreatedMethod = null, bool hideOther = true)
            : base(scrollView.contentContainer, pattern, onCreatedMethod, hideOther)
        {
            _scrollView = scrollView;
            _scrollView.RegisterCallback<WheelEvent>(HandleMouseScroll);
            _direction = direction;
        }

        public UIElementScrollView(ScrollView scrollView, Func<T> createMethod, Direction direction = Direction.Horizontal,
                                   bool hideOther = true)
            : base(scrollView.contentContainer, createMethod, hideOther)
        {
            _scrollView = scrollView;
            _scrollView.RegisterCallback<WheelEvent>(HandleMouseScroll);
            _direction = direction;
        }
        
        public UIElementScrollView(ScrollView scrollView, Action<T> onCreatedMethod = null, Direction direction = Direction.Horizontal)
            : base(scrollView.contentContainer, onCreatedMethod)
        {
            _scrollView = scrollView;
            _scrollView.RegisterCallback<WheelEvent>(HandleMouseScroll);
            _direction = direction;
        }

        public void MoveView(float move, float moveTime = 0.5f)
        {
            if (_direction == Direction.Horizontal)
            {
                SetViewOffset(ClampOffset(_scrollView.scrollOffset.x + move));
            }
            else
            {
                SetViewOffset(ClampOffset(_scrollView.scrollOffset.y + move));
            }
        }

        public void SetViewOffset(float offset)
        {
            offset = ClampOffset(offset);
            _scrollView.scrollOffset = offset * (_direction == Direction.Horizontal ? Vector2.right : Vector2.up);
        }

        private void HandleMouseScroll(WheelEvent evt)
        {
            if (!evt.shiftKey)
            {
                return;
            }

            evt.StopPropagation();

            var currentOffset = _direction == Direction.Horizontal ? _scrollView.scrollOffset.x : _scrollView.scrollOffset.y;
            var dir = _direction == Direction.Horizontal ? -1 : 1;
            var move = evt.delta.y * DEFAULT_SCROLL_MOVE * dir;
            SetViewOffset(currentOffset + move);
        }

        private float ClampOffset(float offset)
        {
            float max = _direction == Direction.Horizontal
                ? _scrollView.contentContainer.layout.width - _scrollView.contentViewport.layout.width
                : _scrollView.contentContainer.layout.height - _scrollView.contentViewport.layout.height;
            return Mathf.Clamp(offset, 0, Mathf.Max(0, max));
        }
    }
}