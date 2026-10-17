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
    ///
    /// **Buildings are threats too.** An enemy structure inside a leash is posted the same way, a step below
    /// an enemy body and for a few attackers rather than one: a body shoots back and a wall does not, so the
    /// body comes first, but a lone raider chipping at a 300-health hut is a siege nobody would watch. Its
    /// distance is to the nearest edge of the footprint, like everything that reaches for a building
    /// (<see cref="FootprintBounds"/>).
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

        /// <summary>Under a body, over every economic job: see the summary.</summary>
        private const byte SIEGE_PRIORITY = 11;

        /// <summary>
        /// How many soldiers one building is handed out to. More than one, because a building is slow to
        /// knock down alone; few enough that a camp next door is not emptied of its whole garrison for it.
        /// </summary>
        private const int SIEGE_ATTACKERS = 4;

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

                    var body = new FootprintBounds(move.ValueRO.Position, move.ValueRO.Position);
                    if (posts.IsInsideEnemyLeash(body, faction.ValueRO))
                    {
                        threats.Add(entity);
                        PostThreat(book, entity, FIGHT_PRIORITY, amount: 1, now);
                    }
                }

                // Buildings: anything with a side and something to lose. A bridge has neither.
                foreach ((RefRO<Faction> faction, RefRO<Health> health,
                          DynamicBuffer<BuildingFootprintCell> footprint, Entity entity)
                         in SystemAPI.Query<RefRO<Faction>, RefRO<Health>, DynamicBuffer<BuildingFootprintCell>>()
                                     .WithEntityAccess())
                {
                    if (threats.Contains(entity) || !health.ValueRO.IsAlive
                        || !FootprintBounds.TryOf(footprint, out FootprintBounds bounds))
                    {
                        continue;
                    }

                    if (posts.IsInsideEnemyLeash(bounds, faction.ValueRO))
                    {
                        threats.Add(entity);
                        PostThreat(book, entity, SIEGE_PRIORITY, SIEGE_ATTACKERS, now);
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
        /// collide: a work order always names the item it wants, and a fight order never does - even when its
        /// target is a building.
        /// </summary>
        private static void PostThreat(OrderBook book, Entity threat, byte priority, int amount, double now)
        {
            if (book.TryFind(threat, ItemId.None, OrderKind.Fight, out int index))
            {
                Order existing = book[index];
                existing.Amount = amount;
                existing.Priority = priority;
                book[index] = existing;
                return;
            }

            book.Add(new Order
            {
                Kind = OrderKind.Fight,
                Target = threat,
                Item = ItemId.None,

                // One at a time for a body. A second soldier on the same raider is not forbidden - it is
                // simply not *asked for*, and what stops the pile-on is that the order goes away once it is
                // claimed. A building asks for a few (see SIEGE_ATTACKERS).
                Amount = amount,
                Priority = priority,
                PostedTime = now,
                LastClaimedTime = now,
                Effective = priority,
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
            /// Whether any post of a side this one is at war with has some of <paramref name="area"/> inside
            /// its leash - a body is an area with no size. Stops at the first one found: one is enough to make
            /// it a threat.
            /// </summary>
            public readonly bool IsInsideEnemyLeash(in FootprintBounds area, Faction mine)
            {
                int2 min = BucketOf(area.Min - _longestLeash);
                int2 max = BucketOf(area.Max + _longestLeash);

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
                            if (area.DistanceSq(watch.Home) <= watch.LeashSq)
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
