using CustomNativeCollections;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// Notices enemies near somebody's post and asks for them to be dealt with (design §8, §13.3 #8).
    ///
    /// **A threat is posted as an order rather than acted on as a reflex**, and that is the decision this
    /// system exists to express. Letting every soldier independently walk at whatever it can see is fewer
    /// lines and gives the thundering herd §8 was written to prevent: ten soldiers converge on one raider,
    /// nine of them arrive to find it dead, and the flank they left is open. An order is claimed by exactly
    /// one agent, ranks against everything else wanting doing, and ages if nobody takes it - all of which the
    /// market already does for bread.
    ///
    /// One order per threat, keyed on the threat itself. Several soldiers looking at the same raider post the
    /// same order and the second one finds it already there, which is the same "post or refresh" shape the
    /// storage and work requests use.
    ///
    /// **Who may serve it is not written on it.** A Fight order's target is the enemy, so the agents eligible
    /// to take it are everyone that thing is an enemy *of* - which with three sides on the map is two
    /// different factions, both correctly dispatched, without a faction field or a relationship table
    /// anywhere.
    /// </summary>
    [UpdateInGroup(typeof(RtsEconomyGroup))]
    [UpdateAfter(typeof(WorkRequestSystem))]
    [UpdateBefore(typeof(OrderAgingSystem))]
    public partial struct ThreatDetectionSystem : ISystem
    {
        /// <summary>
        /// What a threat is worth against a loaf of bread. Above every economic priority in the catalog,
        /// because an idle hauler that could be shooting a raider is a much worse use of a body than a
        /// warehouse waiting another ten seconds for its delivery.
        /// </summary>
        private const byte FIGHT_PRIORITY = 12;

        private ComponentLookup<Faction> _factions;
        private ComponentLookup<Health> _health;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<OrderBook>();
            state.RequireForUpdate<AgentSpatialHash>();

            _factions = state.GetComponentLookup<Faction>(isReadOnly: true);
            _health = state.GetComponentLookup<Health>(isReadOnly: true);
        }

        public void OnUpdate(ref SystemState state)
        {
            OrderBook book = SystemAPI.GetSingleton<OrderBook>();
            NativeSpatialHash<AgentMove> crowd = SystemAPI.GetSingleton<AgentSpatialHash>().Hash;
            double now = SystemAPI.Time.ElapsedTime;

            _factions.Update(ref state);
            _health.Update(ref state);

            // Seeded with whatever is already being fought, so a threat somebody is walking at is neither
            // posted again nor retired. Without this the order goes up afresh every tick, a second soldier
            // takes it, then a third - which is precisely the pile-on that posting orders at all was for.
            NativeHashSet<Entity> threats = CollectEngaged(ref state);
            var candidates = new NativeList<AgentMove>(16, Allocator.Temp);

            // Looked for from the *post* rather than from the soldier. A garrison guards a place, so what
            // counts as a threat is what comes near the place - and a soldier that has been drawn out to the
            // edge of its leash must not be able to see, and then be sent at, something further out still.
            foreach ((RefRO<Post> post, RefRO<Faction> faction)
                     in SystemAPI.Query<RefRO<Post>, RefRO<Faction>>().WithAll<Weapon, AgentMove>())
            {
                Post watch = post.ValueRO;
                if (watch.Leash <= 0f)
                {
                    continue;
                }

                candidates.Clear();
                crowd.QueryAABB(watch.Home - watch.Leash, watch.Home + watch.Leash, candidates);

                foreach (AgentMove candidate in candidates)
                {
                    if (!IsThreat(candidate, faction.ValueRO, watch))
                    {
                        continue;
                    }

                    if (threats.Add(candidate.Entity))
                    {
                        PostThreat(book, candidate.Entity, now);
                    }
                }
            }

            Retire(book, threats);

            candidates.Dispose();
            threats.Dispose();
        }

        /// <summary>
        /// What is already somebody's job. A fight order is claimed and then emptied, so the only record that
        /// a raider is being dealt with is the soldier walking at it.
        /// </summary>
        private NativeHashSet<Entity> CollectEngaged(ref SystemState state)
        {
            var engaged = new NativeHashSet<Entity>(16, Allocator.Temp);

            foreach (RefRO<AssignedOrder> assigned in SystemAPI.Query<RefRO<AssignedOrder>>())
            {
                if (assigned.ValueRO.Kind == OrderKind.Fight)
                {
                    engaged.Add(assigned.ValueRO.Target);
                }
            }

            return engaged;
        }

        /// <summary>
        /// An enemy, alive, and actually inside the leash rather than merely inside the box the hash was
        /// asked for - a corner of that box is half again as far out as the radius.
        /// </summary>
        private bool IsThreat(in AgentMove candidate, Faction mine, in Post watch)
        {
            if (!_factions.HasComponent(candidate.Entity)
                || !Faction.AreEnemies(_factions[candidate.Entity], mine))
            {
                return false;
            }

            if (_health.HasComponent(candidate.Entity) && !_health[candidate.Entity].IsAlive)
            {
                return false;
            }

            return watch.IsWithinLeash(candidate.Position);
        }

        /// <summary>
        /// One order per threat. <see cref="ItemId.None"/> keys it beside the work orders, which cannot
        /// collide: a work order's target is a building and a fight order's is a body.
        /// </summary>
        private static void PostThreat(OrderBook book, Entity threat, double now)
        {
            if (book.TryFind(threat, ItemId.None, out int index))
            {
                Order existing = book[index];
                existing.Amount = 1;
                existing.Priority = FIGHT_PRIORITY;
                book[index] = existing;
                return;
            }

            book.Add(new Order
            {
                Kind = OrderKind.Fight,
                Target = threat,
                Item = ItemId.None,

                // One at a time. A second soldier on the same raider is not forbidden - it is simply not
                // *asked for*, and what stops the pile-on is that the order goes away once it is claimed.
                Amount = 1,
                Priority = FIGHT_PRIORITY,
                PostedTime = now,
                LastClaimedTime = now,
                Effective = FIGHT_PRIORITY,
            });
        }

        /// <summary>
        /// Drops the orders for threats nobody can see any more - killed, walked off, or stepped inside a
        /// building. Only fight orders, for the reason <c>WorkRequestSystem</c> gives: a retirement pass that
        /// could not tell its own orders from another system's would delete them on the tick they were posted.
        /// </summary>
        private static void Retire(OrderBook book, in NativeHashSet<Entity> threats)
        {
            for (int i = book.Length - 1; i >= 0; i--)
            {
                if (book[i].Kind == OrderKind.Fight && !threats.Contains(book[i].Target))
                {
                    book.RemoveAt(i);
                }
            }
        }
    }
}
