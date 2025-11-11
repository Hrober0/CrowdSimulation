using HCore.Extensions;
using HCore.Shapes;
using HCore.Systems;
using UnityEngine;

namespace Objects.GenericModules
{
    public class MShapeRectangle : MonoBehaviour, IInitializable, IBoundsHolder
    {
        public IShape Bounds { get; private set; }

        void IInitializable.Initialize(ISystemManager systems)
        {
            Bounds = CreateBounds(transform.position.To2D());
        }
        void IInitializable.Deinitialize() { }

        public IShape CreateBounds(Vector2 position)
        {
            Vector2 hSize = transform.lossyScale.To2D() * 0.5f;
            return Rectangle.CreateByMinMax(position - hSize, position + hSize);
        }

#if UNITY_EDITOR
        private Vector3 __lastPos = Vector3.zero;
        private Vector3 __lastScale = Vector3.zero;
        private void OnDrawGizmos()
        {
            if (Application.isPlaying)
                return;
            if (Bounds == null || __lastPos != transform.position || __lastScale != transform.lossyScale)
            {
                Bounds = CreateBounds(transform.position.To2D());
                __lastPos = transform.position;
                __lastScale = transform.lossyScale;
            }
            Gizmos.color = Color.red;
            Bounds.DrawBorderGizmos();
        }
#endif
    }
}