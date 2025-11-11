using UnityEngine;

namespace Objects.Agents
{
    public interface IMovingObject : IMComponent
    {
        float Radius { get; }
        Vector2 Position { get; }
        Vector2 TargetVelocity { get; }
        
        void SetVelocity(Vector2 velocity);
    }
}