using Unity.Entities;
using UnityEngine;

namespace ComplexNavigation
{
    public class AgentAuthoring : MonoBehaviour
    {
        [SerializeField] private float _radius = .5f;

        [Space]
        [SerializeField] private float _movementSpeed = 10f;

        [SerializeField] private float _rotationSpeed = 10f;

        public class Baker : Baker<AgentAuthoring>
        {
            public override void Bake(AgentAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new AgentCoreData
                {
                    Entity = entity,
                    MaxSpeed = 1,
                    Radius = authoring._radius,
                });
                AddComponent(entity, new AgentMovementData
                {
                    MovementSpeed = authoring._movementSpeed,
                    RotationSpeed = authoring._rotationSpeed
                });
                AddComponent<Selected>(entity);
                SetComponentEnabled<Selected>(entity, false);
                
                AddComponent<TargetData>(entity);
                SetComponentEnabled<TargetData>(entity, false);
                
                AddComponent<FindPathRequest>(entity);
                SetComponentEnabled<FindPathRequest>(entity, false);
                AddBuffer<PathBuffer>(entity);
                AddComponent<AgentPathState>(entity);
            }
        }
    }
}