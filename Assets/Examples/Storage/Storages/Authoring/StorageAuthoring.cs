using System;
using System.Collections.Generic;
using Unity.Entities;
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

        public class StorageBaker : Baker<StorageAuthoring>
        {
            public override void Bake(StorageAuthoring a)
            {
                var e = GetEntity(TransformUsageFlags.None);
                AddComponent(e, new StorageComponent
                {
                    WorldPosition = a.transform.position,
                });

                var slots = AddBuffer<StorageSlot>(e);
                foreach (var def in a.Slots)
                {
                    slots.Add(new StorageSlot
                    {
                        Resource = def.Resource,
                        Capacity = def.Capacity,
                        CurrentAmount = def.StartAmount,
                    });
                }

                AddBuffer<StorageConnectionElement>(e);
            }
        }
    }
}