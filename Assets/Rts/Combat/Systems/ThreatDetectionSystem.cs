using Unity.Burst;
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
    ///
    /// **Asked once per agent, not once per soldier** (Docs/RtsBattleScaling.md P1). The question is "is this
    /// body inside some other side's leash", and the first version answered it from the soldier's end: every
    /// post searched the agent hash over its whole leash box. That is soldiers × leash² cell probes, and a
    /// leash long enough to see across a battlefield made it 450 ms a tick at 300 a side. Turned round, every
    /// agent looks at the posts near it, bucketed by side, and stops at the first one whose leash it is in -
    /// in a fight that is almost always the first bucket it looks at.
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

        /// <summary>
        /// Cells per side of a post bucket. Large against a body and small against a leash: a leash of ten
        /// looks at a handful of buckets, and a crowded bucket is still only the posts that stand in it.
        /// </summary>
        private const float POST_BUCKET_SIZE = 8f;

        private ComponentLookup<Health> _health;
        private ComponentLookup<OnBridge> _onBridge;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<OrderBook>();

            _health = state.GetComponentLookup<Health>(isReadOnly: true);
            _onBridge = state.GetComponentLookup<OnBridge>(isReadOnly: true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            OrderBook book = SystemAPI.GetSingleton<OrderBook>();
            double now = SystemAPI.Time.ElapsedTime;

            _health.Update(ref state);
            _onBridge.Update(ref state);

            // Seeded with whatever is already being fought, so a threat somebody is walking at is neither
            // posted again nor retired. Without this the order goes up afresh every tick, a second soldier
            // takes it, then a third - which is precisely the pile-on that posting orders at all was for.
            NativeHashSet<Entity> threats = CollectEngaged(ref state);
            PostIndex posts = CollectPosts(ref state);

            if (!posts.IsEmpty)
            {
                // Every body on the map, as the agent hash sees them: enabled AgentMove (not inside a
                // building) and not carried over a bridge. A threat is something that can be walked at.
                foreach ((RefRO<AgentMove> move, RefRO<Faction> faction, Entity entity)
                         in SystemAPI.Query<RefRO<AgentMove>, RefRO<Faction>>().WithEntityAccess())
                {
                    if (threats.Contains(entity) || !IsCandidate(entity))
                    {
                        continue;
                    }

                    if (posts.IsInsideEnemyLeash(move.ValueRO.Position, faction.ValueRO))
                    {
                        threats.Add(entity);
                        PostThreat(book, entity, now);
                    }
                }
            }

            Retire(book, threats);

            posts.Dispose();
            threats.Dispose();
        }

        /// <summary>
        /// What is already somebody's job. A fight order is claimed and then emptied, so the only record that
        /// a raider is being dealt with is the soldier walking at it.
        /// </summary>
        private NativeHashSet<Entity> CollectEngaged(ref SystemState state)
        {
            var engaged = new NativeHashSet<Entity>(64, Allocator.Temp);

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
        /// Every post worth watching: a soldier that is armed and standing on the map. Looked for from the
        /// *post* rather than from the soldier, because a garrison guards a place - a soldier drawn out to the
        /// edge of its leash must not be able to see, and then be sent at, something further out still.
        /// </summary>
        private PostIndex CollectPosts(ref SystemState state)
        {
            var posts = new PostIndex(64, Allocator.Temp);

            foreach ((RefRO<Post> post, RefRO<Faction> faction)
                     in SystemAPI.Query<RefRO<Post>, RefRO<Faction>>().WithAll<Weapon, AgentMove>())
            {
                if (post.ValueRO.Leash > 0f)
                {
                    posts.Add(post.ValueRO, faction.ValueRO);
                }
            }

            return posts;
        }

        /// <summary>
        /// On the map, alive, and not over a bridge. Health is asked about because the applier writes it
        /// immediately and destroys later, so an entity can be at zero and still be standing here this tick.
        /// </summary>
        private bool IsCandidate(Entity entity)
        {
            if (_onBridge.HasComponent(entity) && _onBridge.IsComponentEnabled(entity))
            {
                return false;
            }

            return !_health.HasComponent(entity) || _health[entity].IsAlive;
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

        /// <summary>
        /// Posts bucketed by place and by side, so "is this point inside some enemy's leash" looks only at the
        /// buckets of the other sides that a leash could reach from.
        /// </summary>
        private struct PostIndex
        {
            private NativeParallelMultiHashMap<int3, Watch> _buckets;
            private NativeList<Faction> _sides;
            private float _longestLeash;

            public PostIndex(int capacity, Allocator allocator)
            {
                _buckets = new NativeParallelMultiHashMap<int3, Watch>(capacity, allocator);
                _sides = new NativeList<Faction>(4, allocator);
                _longestLeash = 0f;
            }

            public bool IsEmpty => _sides.IsEmpty;

            public void Add(in Post post, Faction side)
            {
                _buckets.Add(Key(BucketOf(post.Home), side), new Watch
                {
                    Home = post.Home,
                    LeashSq = post.Leash * post.Leash,
                });

                _longestLeash = math.max(_longestLeash, post.Leash);

                if (!_sides.Contains(side))
                {
                    _sides.Add(side);
                }
            }

            /// <summary>
            /// Whether any post of a side this one is at war with has <paramref name="position"/> inside its
            /// leash. Stops at the first one found: one is enough to make it a threat.
            /// </summary>
            public readonly bool IsInsideEnemyLeash(float2 position, Faction mine)
            {
                int2 min = BucketOf(position - _longestLeash);
                int2 max = BucketOf(position + _longestLeash);

                foreach (Faction side in _sides)
                {
                    if (!Faction.AreEnemies(side, mine))
                    {
                        continue;
                    }

                    for (int y = min.y; y <= max.y; y++)
                    for (int x = min.x; x <= max.x; x++)
                    {
                        if (!_buckets.TryGetFirstValue(Key(new int2(x, y), side), out Watch watch,
                                                       out NativeParallelMultiHashMapIterator<int3> it))
                        {
                            continue;
                        }

                        do
                        {
                            if (math.distancesq(position, watch.Home) <= watch.LeashSq)
                            {
                                return true;
                            }
                        }
                        while (_buckets.TryGetNextValue(out watch, ref it));
                    }
                }

                return false;
            }

            public void Dispose()
            {
                _buckets.Dispose();
                _sides.Dispose();
            }

            private static int2 BucketOf(float2 position) => (int2)math.floor(position / POST_BUCKET_SIZE);

            private static int3 Key(int2 bucket, Faction side) => new(bucket, side.Id);
        }

        private struct Watch
        {
            public float2 Home;
            public float LeashSq;
        }
    }
}
