using Unity.Entities;
using UnityEngine;

namespace ComplexNavigation
{
    public class AgentAuthoring : MonoBehaviour
    {
        [SerializeField] private float _movementSpeed = 10f;
        [SerializeField] private float _rotationSpeed = 10f;
        
        public class Baker : Baker<AgentAuthoring>
        {
            public override void Bake(AgentAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new AgentMovementData
                    {
                        MovementSpeed = authoring._movementSpeed,
                        RotationSpeed = authoring._rotationSpeed
                    });
                AddComponent(entity, new TargetData{ TargetPosition = new(5,5)});
            }
        }
    }
}
