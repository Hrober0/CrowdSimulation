using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Examples.Storage
{
    public struct HolderComponent : IComponentData
    {
        public int          CarryCapacity;
        public int          CurrentLoad;
        public ResourceType CarriedType;
        public HolderState  State;
        public Entity       AssignedJob;  // set to source storage while job is active; Null when idle
        public float        MoveSpeed;
        public float3       TargetPos;
    }
 
    public enum HolderState : byte
    {
        Idle,
        MovingToSource,
        Picking,
        MovingToDest,
        Delivering,
        Returning,
    }
 
    /// <summary>
    /// Signals that a holder has reached its TargetPos.
    /// Implemented as IEnableableComponent so it is added once to the archetype
    /// and then only toggled (a bitmask flip) — no structural change, no chunk move.
    ///
    /// HolderMovementSystem  → enables  via ECB.SetComponentEnabled(..., true)
    /// ResourceTransferSystem → disables via EnabledRefRW in the query iteration
    /// </summary>
    public struct ArrivalTag : IComponentData, IEnableableComponent { }
}