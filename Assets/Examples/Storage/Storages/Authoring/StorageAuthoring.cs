using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Examples.Storage
{
    public class StorageAuthoring : MonoBehaviour
    {
        [Serializable]
        public struct SlotDefinition
        {
            public ResourceType Resource;
            public int          Capacity;
            public int          StartAmount;
        }

        public List<SlotDefinition> Slots = new();

        [Tooltip("Where holders enter the storage. Defaults to the storage position if unset.")]
        public Transform InputPoint;

        [Tooltip("Where holders exit the storage. Defaults to the storage position if unset.")]
        public Transform OutputPoint;

        public class StorageBaker : Baker<StorageAuthoring>
        {
            public override void Bake(StorageAuthoring a)
            {
                var e = GetEntity(TransformUsageFlags.None);

                float3 pos    = a.transform.position;
                float3 input  = a.InputPoint  != null ? (float3)a.InputPoint.position  : pos;
                float3 output = a.OutputPoint != null ? (float3)a.OutputPoint.position : pos;

                AddComponent(e, new StorageComponent
                {
                    WorldPosition = pos,
                    InputPoint    = input,
                    OutputPoint   = output,
                });

                var slots = AddBuffer<StorageSlot>(e);
                foreach (var def in a.Slots)
                {
                    slots.Add(new StorageSlot
                    {
                        Resource      = def.Resource,
                        Capacity      = def.Capacity,
                        CurrentAmount = def.StartAmount,
                    });
                }

                AddBuffer<ConnectionRefElement>(e);
            }
        }
    }
}
