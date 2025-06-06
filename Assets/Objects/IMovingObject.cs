using HCore.Shapes;
using UnityEngine;

namespace Objects
{
    public interface IMovingObject : IBEntity, IMComponent
    {
        Vector2 TargetVelocity { get; }
    }
}