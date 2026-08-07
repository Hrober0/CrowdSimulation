using System;
using Rts;
using Unity.Entities;
using UnityEngine;

namespace Examples.Rts
{
    /// <summary>
    /// What a building stores, and on what terms (design §7). Goes on the same object as
    /// <see cref="BuildingAuthoring"/>.
    ///
    /// The presets are the four rows of the design's table. They are worth using: the interesting mistakes
    /// in this model are all threshold mistakes, and the presets are the combinations known to behave.
    /// </summary>
    public class StorageAuthoring : MonoBehaviour
    {
        /// <summary>The roles of §7's table. <see cref="Custom"/> is the escape hatch.</summary>
        public enum StorageRole
        {
            /// <summary>Never asks, gives everything away. A mine, or a crafter's output shelf.</summary>
            Source,

            /// <summary>Always wants more, gives anything away to whoever wants it more.</summary>
            Warehouse,

            /// <summary>Wants a buffer and never gives it back. A crafter's input shelf.</summary>
            Consumer,

            Custom,
        }

        [Serializable]
        private struct Slot
        {
            [Min(1), Tooltip("Which item. Any non-zero number; the game layer decides what it means.")]
            public ushort Item;

            [Min(0)] public int Capacity;

            [Min(0), Tooltip("How much is on the shelf when the game starts.")]
            public int StartingAmount;

            public StorageRole Role;

            [Tooltip("Custom only: requests deliveries while below this.")]
            public int DeliverInUpTo;

            [Tooltip("Custom only: gives units away only while above this.")]
            public int DeliverOutDownTo;

            [Range(0, 10), Tooltip("Custom only. A source must be strictly below its taker, and 0 never asks.")]
            public int Priority;
        }

        [SerializeField] private Slot[] _slots =
        {
            new() { Item = 1, Capacity = 100, Role = StorageRole.Warehouse },
        };

        [SerializeField, Min(0)]
        [Tooltip("Haulers allowed to work this building at once. 0 uses the default.")]
        private int _maxConcurrentHaulers;

        private class StorageBaker : Baker<StorageAuthoring>
        {
            public override void Bake(StorageAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.None);

                DynamicBuffer<StorageSlot> slots = AddBuffer<StorageSlot>(entity);
                foreach (Slot slot in authoring._slots)
                {
                    slots.Add(Resolve(slot));
                }

                if (authoring._maxConcurrentHaulers > 0)
                {
                    AddComponent(entity, new HaulLimit { MaxConcurrent = authoring._maxConcurrentHaulers });
                }
            }

            private static StorageSlot Resolve(in Slot slot)
            {
                var resolved = new StorageSlot
                {
                    Item = new ItemId(slot.Item),
                    Capacity = slot.Capacity,
                    Amount = Mathf.Clamp(slot.StartingAmount, 0, slot.Capacity),
                };

                switch (slot.Role)
                {
                    case StorageRole.Source:
                        resolved.DeliverInUpTo = 0;
                        resolved.DeliverOutDownTo = 0;
                        resolved.Priority = 0;
                        break;

                    case StorageRole.Warehouse:
                        resolved.DeliverInUpTo = slot.Capacity;
                        resolved.DeliverOutDownTo = 0;
                        resolved.Priority = 1;
                        break;

                    case StorageRole.Consumer:
                        // In equals out, which is the "never give back what I have been given" of §7.
                        resolved.DeliverInUpTo = slot.DeliverInUpTo > 0 ? slot.DeliverInUpTo : slot.Capacity;
                        resolved.DeliverOutDownTo = resolved.DeliverInUpTo;
                        resolved.Priority = 5;
                        break;

                    default:
                        resolved.DeliverInUpTo = slot.DeliverInUpTo;
                        resolved.DeliverOutDownTo = slot.DeliverOutDownTo;
                        resolved.Priority = (byte)Mathf.Clamp(slot.Priority, 0, 255);
                        break;
                }

                return resolved;
            }
        }
    }
}
