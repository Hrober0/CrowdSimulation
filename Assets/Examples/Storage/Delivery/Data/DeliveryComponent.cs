using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Examples.Storage
{
    public struct HolderComponent : IComponentData
    {
        public int CarryCapacity;
        public int CurrentLoad;
        public ResourceType CarriedType;
        public HolderState State;
        public Entity AssignedJob;
        public float MoveSpeed;
        public float3 TargetPos;
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
    /// Added by HolderMovementSystem when the holder reaches TargetPos.
    /// ResourceTransferSystem reacts to it and owns all state transitions.
    /// Removed via ECB after processing.
    /// </summary>
    public struct ArrivalTag : IComponentData
    {
    }
}