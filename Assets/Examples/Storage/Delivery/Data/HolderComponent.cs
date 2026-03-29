using Unity.Entities;
using Unity.Mathematics;

namespace Examples.Storage
{
    public struct HolderComponent : IComponentData
    {
        public int          CarryCapacity;
        public int          CurrentLoad;
        public ResourceType CarriedType;
        public HolderState  State;
        public Entity       AssignedJob;      // source storage entity while a job is active
        public Entity       WaitingAtStorage; // dest storage entity while in WaitingAtDest state
        public float        MoveSpeed;
        public float3       TargetPos;
    }

    public enum HolderState : byte
    {
        Idle,
        MovingToSourceInput,  // avoidance ON  — heading to source.InputPoint
        ExitingSource,        // avoidance OFF — heading to source.OutputPoint (pickup done)
        MovingToDestInput,    // avoidance ON  — heading to dest.InputPoint
        WaitingAtDest,        // avoidance OFF — inside dest, delivery done, awaiting new job
        ExitingDest,          // avoidance OFF — heading to dest.OutputPoint (new job assigned)
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
