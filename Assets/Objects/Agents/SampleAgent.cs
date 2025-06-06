using System;
using HCore.Shapes;
using HCore.Systems;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Objects.Agents
{
    public class SampleAgent : MonoBehaviour, IMovingObject, IInitializable, IBoundsHolder
    {
        [SerializeField] private float _radius = 1;
        [SerializeField] private float _speed = 1;

        private Vector2 _velocity = Vector2.zero;
        
        public float Radius => _radius;
        public Vector2 Position => transform.position;
        public Vector2 TargetVelocity { get; private set; }

        public IShape Bounds { get; private set; }
        
        public void Initialize(ISystemManager systems)
        {
            TargetVelocity = Random.insideUnitCircle.normalized;
            Bounds = CreateBounds(Position);
        }
        public void Deinitialize() {}

        private void Update()
        {
            transform.position += (Vector3)(_velocity * (_speed * Time.deltaTime));
            Bounds = CreateBounds(Position);
        }

        public IShape CreateBounds(Vector2 position) => new Circle(position, _radius);
        
        public void SetVelocity(Vector2 velocity)
        {
            _velocity = velocity;
        }
        
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            if (Bounds != null)
            {
                Bounds.DrawBorder(Color.green);
            }
            else
            {
                CreateBounds(Position).DrawBorderGizmos();
            }
        }
    }
}