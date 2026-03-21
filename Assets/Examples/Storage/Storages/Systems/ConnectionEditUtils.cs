using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Examples.Storage
{
    public static class ConnectionEditUtils
    {
        const float AUTO_CONNECT_RADIUS  = 50f;
        
        public static void ApplyEdit(EntityManager em, in ConnectionEdit edit)
        {
            if (!em.Exists(edit.FromEntity)) return;
            if (!em.HasBuffer<StorageConnectionElement>(edit.FromEntity)) return;

            var buffer = em.GetBuffer<StorageConnectionElement>(edit.FromEntity);

            int found = -1;
            for (int i = 0; i < buffer.Length; i++)
            {
                var c = buffer[i];
                if (c.TargetStorage == edit.ToEntity && c.Resource == edit.Resource)
                {
                    found = i;
                    break;
                }
            }

            if (found >= 0)
            {
                var conn = buffer[found];
                conn.Priority = edit.Priority;
                conn.Flags = edit.Enabled
                    ? (conn.Flags | ConnectionFlags.Enabled | ConnectionFlags.PlayerOverride)
                    : (conn.Flags & ~ConnectionFlags.Enabled | ConnectionFlags.PlayerOverride);
                buffer[found] = conn;
            }
            else if (edit.Enabled)
            {
                buffer.Add(new StorageConnectionElement
                {
                    TargetStorage = edit.ToEntity,
                    Resource = edit.Resource,
                    Priority = edit.Priority,
                    MaxBatchSize = 20,
                    Flags = ConnectionFlags.Enabled | ConnectionFlags.PlayerOverride,
                });
            }
        }

        public static void AutoConnect(EntityManager em, Entity targetEntity)
        {
            if (!em.Exists(targetEntity)) return;
            if (!em.HasBuffer<StorageSlot>(targetEntity)) return;
            if (!em.HasComponent<StorageComponent>(targetEntity)) return;
            if (!em.HasBuffer<StorageConnectionElement>(targetEntity)) return;

            using var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<StorageComponent>(),
                ComponentType.ReadOnly<StorageSlot>(),
                ComponentType.ReadWrite<StorageConnectionElement>());

            using var allEntities = query.ToEntityArray(Allocator.Temp);

            var targetStorage = em.GetComponentData<StorageComponent>(targetEntity);
            var targetSlots = em.GetBuffer<StorageSlot>(targetEntity, isReadOnly: true);

            foreach (var neighbourEntity in allEntities)
            {
                if (neighbourEntity == targetEntity) continue;

                var neighbourStorage = em.GetComponentData<StorageComponent>(neighbourEntity);
                float dist = math.distance(targetStorage.WorldPosition,
                    neighbourStorage.WorldPosition);
                if (dist > AUTO_CONNECT_RADIUS) continue;

                var neighbourSlots = em.GetBuffer<StorageSlot>(neighbourEntity, isReadOnly: true);

                for (int i = 0; i < targetSlots.Length; i++)
                {
                    var slot = targetSlots[i];
                    if (!StorageSlotUtils.TryGetSlotIndex(neighbourSlots, slot.Resource, out _))
                        continue;

                    // target → neighbour
                    var targetConns = em.GetBuffer<StorageConnectionElement>(targetEntity);
                    AddIfMissing(ref targetConns, neighbourEntity, slot.Resource);

                    // neighbour → target
                    var neighbourConns = em.GetBuffer<StorageConnectionElement>(neighbourEntity);
                    AddIfMissing(ref neighbourConns, targetEntity, slot.Resource);
                }
            }
        }


        public static void AutoConnectAll(EntityManager em)
        {
            using var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<StorageComponent>());
            using var entities = query.ToEntityArray(Allocator.Temp);

            foreach (var e in entities)
                AutoConnect(em, e);
        }

        private static void AddIfMissing(
            ref DynamicBuffer<StorageConnectionElement> buffer,
            Entity target,
            ResourceType resource)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                var c = buffer[i];
                if (c.TargetStorage == target && c.Resource == resource) return;
            }

            buffer.Add(new StorageConnectionElement
            {
                TargetStorage = target,
                Resource = resource,
                Priority = 128,
                MaxBatchSize = 20,
                Flags = ConnectionFlags.Enabled,
            });
        }
    }
}