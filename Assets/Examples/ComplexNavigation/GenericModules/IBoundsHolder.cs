using HCore.Shapes;
using UnityEngine;

namespace Objects
{
    public interface IBoundsHolder : IMComponent
    {
        IShape Bounds { get; }
        IShape CreateBounds(Vector2 position);
    }
}