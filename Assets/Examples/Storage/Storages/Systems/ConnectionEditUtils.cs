using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Examples.Storage
{
    public static class ConnectionEditUtils
    {
        const float AUTO_CONNECT_RADIUS = 50f;

        /// <summary>
        /// Updates mode and priority on an existing connection entity directly.
        /// </summary>
        public static void ApplyEdit(EntityManager em, Entity connectionEntity,
                                     ConnectionMode mode, byte priority)
        {
            if (!em.Exists(connectionEntity)) return;
            var conn = em.GetComponentData<StorageConnectionComponent>(connectionEntity);
            conn.Mode = mode;
            conn.Priority = priority;
            em.SetComponentData(connectionEntity, conn);
        }

        public static void AddConnection(EntityManager em, Entity storageA, Entity storageB,
            ResourceType resource, ConnectionMode mode = ConnectionMode.AToB)
        {
            AddIfMissing(em, storageA, storageB, resource, mode);
        }

        public static void RemoveConnection(EntityManager em, Entity connectionEntity)
        {
            if (!em.Exists(connectionEntity)) return;
            var conn = em.GetComponentData<StorageConnectionComponent>(connectionEntity);

            RemoveRef(em, conn.StorageA, connectionEntity);
            RemoveRef(em, conn.StorageB, connectionEntity);
            em.DestroyEntity(connectionEntity);
        }

        private static void RemoveRef(EntityManager em, Entity storage, Entity connectionEntity)
        {
            if (!em.Exists(storage)) return;
            if (!em.HasBuffer<ConnectionRefElement>(storage)) return;
            var refs = em.GetBuffer<ConnectionRefElement>(storage);
            for (int i = refs.Length - 1; i >= 0; i--)
            {
                if (refs[i].Value == connectionEntity)
                {
                    refs.RemoveAt(i);
                    break;
                }
            }
        }

        public static void AutoConnect(EntityManager em, Entity targetEntity)
        {
            if (!em.Exists(targetEntity)) return;
            if (!em.HasBuffer<StorageSlot>(targetEntity)) return;
            if (!em.HasComponent<StorageComponent>(targetEntity)) return;
            if (!em.HasBuffer<ConnectionRefElement>(targetEntity)) return;

            using var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<StorageComponent>(),
                ComponentType.ReadOnly<StorageSlot>(),
                ComponentType.ReadWrite<ConnectionRefElement>());

            using var allEntities = query.ToEntityArray(Allocator.Temp);

            var targetStorage = em.GetComponentData<StorageComponent>(targetEntity);

            // ── Phase 1: read-only — collect all pairs to create ─────────────
            // DynamicBuffer handles are invalidated by structural changes (CreateEntity),
            // so all slot reads must complete before AddIfMissing is called.
            var pending = new NativeList<PendingConnection>(Allocator.Temp);

            foreach (var neighbourEntity in allEntities)
            {
                if (neighbourEntity == targetEntity) continue;

                var neighbourStorage = em.GetComponentData<StorageComponent>(neighbourEntity);
                if (math.distance(targetStorage.WorldPosition, neighbourStorage.WorldPosition) > AUTO_CONNECT_RADIUS) continue;

                var targetSlots = em.GetBuffer<StorageSlot>(targetEntity, isReadOnly: true);
                var neighbourSlots = em.GetBuffer<StorageSlot>(neighbourEntity, isReadOnly: true);

                for (int i = 0; i < targetSlots.Length; i++)
                {
                    var resource = targetSlots[i].Resource;
                    if (!StorageSlotUtils.TryGetSlotIndex(neighbourSlots, resource, out _))
                        continue;

                    pending.Add(new PendingConnection(targetEntity, neighbourEntity, resource, ConnectionMode.AToB));
                }
            }

            // ── Phase 2: structural changes — safe now, no live buffer handles ──
            for (int i = 0; i < pending.Length; i++)
            {
                var p = pending[i];
                AddIfMissing(em, p.StorageA, p.StorageB, p.Resource, p.Mode);
            }

            pending.Dispose();
        }

        private struct PendingConnection
        {
            public Entity StorageA;
            public Entity StorageB;
            public ResourceType Resource;
            public ConnectionMode Mode;

            public PendingConnection(Entity a, Entity b, ResourceType res, ConnectionMode mode)
            {
                StorageA = a;
                StorageB = b;
                Resource = res;
                Mode = mode;
            }
        }

        public static void AutoConnectAll(EntityManager em)
        {
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<StorageComponent>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            foreach (var e in entities) AutoConnect(em, e);
        }

        // ─────────────────────────────────────────────────────────────────────

        private static void AddIfMissing(
            EntityManager em,
            Entity storageA,
            Entity storageB,
            ResourceType resource,
            ConnectionMode mode)
        {
            // Check A's ref buffer for an existing connection with same A→B+resource.
            var refsA = em.GetBuffer<ConnectionRefElement>(storageA, isReadOnly: true);
            for (int i = 0; i < refsA.Length; i++)
            {
                if (!em.Exists(refsA[i].Value)) continue;
                var c = em.GetComponentData<StorageConnectionComponent>(refsA[i].Value);
                if (c.Resource == resource && (c.StorageA == storageA && c.StorageB == storageB ||
                                               c.StorageA == storageB && c.StorageA == storageB))
                    return; // already exists
            }

            // Create the connection entity.
            var connEntity = em.CreateEntity();
            em.AddComponentData(connEntity, new StorageConnectionComponent
            {
                StorageA = storageA,
                StorageB = storageB,
                Resource = resource,
                Priority = 128,
                MaxBatchSize = 20,
                Mode = mode,
            });

            // Both sides hold a ref so each warehouse panel can display the connection.
            em.GetBuffer<ConnectionRefElement>(storageA).Add(new ConnectionRefElement { Value = connEntity });
            em.GetBuffer<ConnectionRefElement>(storageB).Add(new ConnectionRefElement { Value = connEntity });
        }
    }
}