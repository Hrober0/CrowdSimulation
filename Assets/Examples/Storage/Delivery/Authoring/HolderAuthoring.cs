using Unity.Entities;
using UnityEngine;

namespace Examples.Storage
{
    public class HolderAuthoring : MonoBehaviour
    {
        [SerializeField] private int _carryCapacity = 20;
        [SerializeField] private float _moveSpeed = 5f;

        public class HolderBaker : Baker<HolderAuthoring>
        {
            public override void Bake(HolderAuthoring a)
            {
                var e = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(e, new HolderComponent
                {
                    CarryCapacity = a._carryCapacity,
                    MoveSpeed = a._moveSpeed,
                    State = HolderState.Idle,
                    AssignedJob = Entity.Null,
                });
            }
        }
    }
}