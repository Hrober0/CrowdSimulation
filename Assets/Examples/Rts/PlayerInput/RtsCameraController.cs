using UnityEngine;

namespace Examples.Rts
{
    /// <summary>
    /// Pan and zoom, so a map bigger than the screen can be looked at. Drag with the right or middle button,
    /// or use the arrow keys; scroll to zoom.
    ///
    /// Panning is done by working out where the world point under the cursor has moved to and cancelling it
    /// out, so the ground stays stuck to the pointer at any zoom level. Scaling a fixed speed by the zoom
    /// instead always feels wrong at one end of the range or the other.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class RtsCameraController : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float _keyboardPanSpeed = 20f;

        [SerializeField, Min(0f)] private float _zoomSpeed = 8f;

        [SerializeField, Min(1f)] private float _minZoom = 4f;

        [SerializeField, Min(1f)] private float _maxZoom = 60f;

        private Camera _camera;
        private Vector3 _dragOrigin;
        private bool _dragging;

        private void Awake() => _camera = GetComponent<Camera>();

        private void LateUpdate()
        {
            Zoom();
            DragPan();
            KeyboardPan();
        }

        private void Zoom()
        {
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Approximately(scroll, 0f) || !_camera.orthographic)
            {
                return;
            }

            float size = _camera.orthographicSize - scroll * _zoomSpeed * Time.unscaledDeltaTime * 10f;
            _camera.orthographicSize = Mathf.Clamp(size, _minZoom, _maxZoom);
        }

        private void DragPan()
        {
            bool held = Input.GetMouseButton(2) || Input.GetMouseButton(1);

            if (!held)
            {
                _dragging = false;
                return;
            }

            Vector3 cursor = ScreenToWorld(Input.mousePosition);

            if (!_dragging)
            {
                _dragOrigin = cursor;
                _dragging = true;
                return;
            }

            // The grab point must stay under the cursor, so the camera moves by whatever the drag displaced.
            Vector3 delta = _dragOrigin - cursor;
            transform.position += new Vector3(delta.x, delta.y, 0f);
        }

        private void KeyboardPan()
        {
            var input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (input.sqrMagnitude <= 0f)
            {
                return;
            }

            // Scaled by zoom, so a keypress covers the same fraction of the screen however far out you are.
            float speed = _keyboardPanSpeed * (_camera.orthographic ? _camera.orthographicSize / 10f : 1f);
            transform.position += (Vector3)(input.normalized * (speed * Time.unscaledDeltaTime));
        }

        private Vector3 ScreenToWorld(Vector3 screen)
        {
            screen.z = Mathf.Abs(transform.position.z);
            return _camera.ScreenToWorldPoint(screen);
        }
    }
}
