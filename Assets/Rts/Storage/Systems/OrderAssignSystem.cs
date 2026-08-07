using System.Collections.Generic;
using GridNav;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// Matches orders to sources to agents (design §7, §8, §13.3 #10).
    ///
    /// Single-threaded on purpose. Every claim reserves stock at one building and room at another and rolls
    /// both back if either fails, and reservation-with-rollback across two entities has no clean parallel
    /// form (§13.2 invariant 4). It is affordable because it runs at 10 Hz over a bounded candidate set,
    /// while the sixty-hertz work stays in the agent phase.
    ///
    /// The matching rule is §7 in one line: a source must hold the item, stay above its own
    /// <see cref="StorageSlot.DeliverOutDownTo"/> after giving, and have a **strictly lower** priority than
    /// the requester. Strict inequality is what makes the model provably free of ping-pong - two warehouses
    /// at the same priority can never trade, in either direction, without anything having to remember what
    /// moved last.
    ///
    /// Orders are taken in effective-priority order and given to the nearest free agent, which is how §8's
    /// "score by effectivePriority / travelCost" comes out in practice: priority decides *what* is done next,
    /// distance decides *who* does it.
    /// </summary>
    [UpdateInGroup(typeof(RtsEconomyGroup))]
    [UpdateAfter(typeof(OrderAgingSystem))]
    [UpdateBefore(typeof(IdleAssignSystem))]
    public partial struct OrderAssignSystem : ISystem
    {
        private const int MAX_CLAIMS_PER_TICK = 16;

        /// <summary>Used by buildings that do not set their own <see cref="HaulLimit"/> (§15: tuning).</summary>
        private const int DEFAULT_MAX_CONCURRENT_HAULERS = 4;

        /// <summary>How far an agent will walk to start a haul. Beyond this someone nearer should do it.</summary>
        private const float MAX_HAUL_RANGE = 64f;

        private const float PICKUP_SECONDS = 0.5f;
        private const float DEPOSIT_SECONDS = 0.5f;

        private BufferLookup<StorageSlot> _slots;
        private BufferLookup<BuildingEntranceCell> _entrances;
        private BufferLookup<TaskStep> _steps;
        private ComponentLookup<AssignedOrder> _orders;
        private ComponentLookup<HaulLimit> _limits;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<OrderBook>();

            _slots = state.GetBufferLookup<StorageSlot>();
            _entrances = state.GetBufferLookup<BuildingEntranceCell>(isReadOnly: true);
            _steps = state.GetBufferLookup<TaskStep>();
            _orders = state.GetComponentLookup<AssignedOrder>();
            _limits = state.GetComponentLookup<HaulLimit>(isReadOnly: true);
        }

        public void OnUpdate(ref SystemState state)
        {
            OrderBook book = SystemAPI.GetSingleton<OrderBook>();
            if (book.Length == 0)
            {
                return;
            }

            _slots.Update(ref state);
            _entrances.Update(ref state);
            _steps.Update(ref state);
            _orders.Update(ref state);
            _limits.Update(ref state);

            NativeList<FreeAgent> agents = CollectFreeAgents(ref state);
            if (agents.Length == 0)
            {
                agents.Dispose();
                return;
            }

            NativeList<StorageSite> sites = CollectStorageSites(ref state);
            NativeHashMap<Entity, int> busy = CountHaulersPerTarget(ref state);
            NativeArray<OrderRank> ranked = RankOrders(book);

            int claims = 0;
            for (int i = 0; i < ranked.Length && claims < MAX_CLAIMS_PER_TICK && agents.Length > 0; i++)
            {
                if (TryAssign(book, ranked[i].Index, sites, agents, busy))
                {
                    claims++;
                }
            }

            ranked.Dispose();
            busy.Dispose();
            sites.Dispose();
            agents.Dispose();

            PruneEmptyOrders(book);
        }

        private bool TryAssign(
            OrderBook book,
            int orderIndex,
            in NativeList<StorageSite> sites,
            NativeList<FreeAgent> agents,
            NativeHashMap<Entity, int> busy)
        {
            Order order = book[orderIndex];
            if (order.Kind != OrderKind.Haul || order.Amount <= 0)
            {
                return false;
            }

            if (!TryEntranceOf(order.Target, out int2 targetDoor))
            {
                return false;
            }

            busy.TryGetValue(order.Target, out int working);
            if (working >= MaxHaulersFor(order.Target))
            {
                return false;
            }

            float2 targetPoint = GridCoords.CellCenter(targetDoor);
            if (!TryFindSource(sites, order, targetPoint, out int siteIndex, out int sourceStock))
            {
                return false;
            }

            StorageSite source = sites[siteIndex];
            if (!TryNearestAgent(agents, source.Point, out int agentIndex))
            {
                return false;
            }

            FreeAgent agent = agents[agentIndex];
            int amount = math.min(math.min(order.Amount, sourceStock), agent.CarryCapacity);
            if (amount <= 0)
            {
                return false;
            }

            DynamicBuffer<StorageSlot> sourceSlots = _slots[source.Building];
            DynamicBuffer<StorageSlot> targetSlots = _slots[order.Target];
            if (!StorageSlotUtils.TryReserveBoth(ref sourceSlots, ref targetSlots, order.Item, amount))
            {
                return false;
            }

            WriteTask(agent, source, order, targetDoor);

            _orders[agent.Entity] = new AssignedOrder
            {
                Kind = OrderKind.Haul,
                Source = source.Building,
                Target = order.Target,
                Item = order.Item,
                Amount = amount,
            };
            _orders.SetComponentEnabled(agent.Entity, true);

            order.Amount -= amount;
            book[orderIndex] = order;

            agents.RemoveAtSwapBack(agentIndex);
            busy[order.Target] = working + 1;
            return true;
        }

        /// <summary>
        /// The hauler's whole task (§9). The <c>Exit</c> is only there when the agent was resting inside a
        /// hut - which is where idle agents are, so it is the common case rather than the exception.
        /// </summary>
        private void WriteTask(in FreeAgent agent, in StorageSite source, in Order order, int2 targetDoor)
        {
            DynamicBuffer<TaskStep> steps = _steps[agent.Entity];
            steps.Clear();

            if (agent.Inside != Entity.Null && TryEntranceOf(agent.Inside, out int2 homeDoor))
            {
                steps.Add(TaskStep.Exit(agent.Inside, homeDoor));
            }

            steps.Add(TaskStep.GoTo(source.EntranceCell));
            steps.Add(TaskStep.Pickup(source.Building, PICKUP_SECONDS));
            steps.Add(TaskStep.GoTo(targetDoor));
            steps.Add(TaskStep.Deposit(order.Target, DEPOSIT_SECONDS));
        }

        /// <summary>
        /// The nearest building that may give this item away. "May" is the whole of §7: it holds stock above
        /// its own give-away floor, and its priority is strictly below the requester's.
        /// </summary>
        private bool TryFindSource(
            in NativeList<StorageSite> sites,
            in Order order,
            float2 targetPoint,
            out int index,
            out int stock)
        {
            index = -1;
            stock = 0;
            float best = float.MaxValue;

            for (int i = 0; i < sites.Length; i++)
            {
                StorageSite site = sites[i];
                if (site.Building == order.Target)
                {
                    continue;
                }

                DynamicBuffer<StorageSlot> slots = _slots[site.Building];
                if (!StorageSlotUtils.TryGetSlotIndex(slots, order.Item, out int slotIndex))
                {
                    continue;
                }

                StorageSlot slot = slots[slotIndex];
                if (slot.AvailableOut <= 0 || slot.Priority >= order.Priority)
                {
                    continue;
                }

                float distance = math.distancesq(site.Point, targetPoint);
                if (distance >= best)
                {
                    continue;
                }

                best = distance;
                index = i;
                stock = slot.AvailableOut;
            }

            return index >= 0;
        }

        private static bool TryNearestAgent(in NativeList<FreeAgent> agents, float2 point, out int index)
        {
            index = -1;
            float best = MAX_HAUL_RANGE * MAX_HAUL_RANGE;

            for (int i = 0; i < agents.Length; i++)
            {
                float distance = math.distancesq(agents[i].Position, point);
                if (distance < best)
                {
                    best = distance;
                    index = i;
                }
            }

            return index >= 0;
        }

        private NativeList<FreeAgent> CollectFreeAgents(ref SystemState state)
        {
            var agents = new NativeList<FreeAgent>(64, Allocator.Temp);

            // WithPresent on both, because the agents most likely to be free are exactly the ones resting
            // inside a hut, and those have AgentMove disabled.
            foreach ((RefRO<AgentMove> move, RefRO<Carry> carry, RefRO<InsideBuilding> inside,
                      EnabledRefRO<InsideBuilding> isInside, DynamicBuffer<TaskStep> steps, Entity entity)
                     in SystemAPI.Query<RefRO<AgentMove>, RefRO<Carry>, RefRO<InsideBuilding>,
                                        EnabledRefRO<InsideBuilding>, DynamicBuffer<TaskStep>>()
                                 .WithPresent<AgentMove, InsideBuilding>()
                                 .WithDisabled<AssignedOrder>()
                                 .WithEntityAccess())
            {
                // Mid-task or holding something: not free, whatever its order component says.
                if (!steps.IsEmpty || !carry.ValueRO.IsEmpty || carry.ValueRO.Capacity <= 0)
                {
                    continue;
                }

                agents.Add(new FreeAgent
                {
                    Entity = entity,
                    Position = move.ValueRO.Position,
                    CarryCapacity = carry.ValueRO.Capacity,
                    Inside = isInside.ValueRO ? inside.ValueRO.Building : Entity.Null,
                });
            }

            return agents;
        }

        private NativeList<StorageSite> CollectStorageSites(ref SystemState state)
        {
            var sites = new NativeList<StorageSite>(32, Allocator.Temp);

            foreach ((DynamicBuffer<BuildingEntranceCell> doors, Entity building)
                     in SystemAPI.Query<DynamicBuffer<BuildingEntranceCell>>()
                                 .WithAll<StorageSlot>()
                                 .WithEntityAccess())
            {
                // A store nobody can walk up to cannot be a source, whatever is on its shelves.
                if (doors.IsEmpty)
                {
                    continue;
                }

                sites.Add(new StorageSite
                {
                    Building = building,
                    EntranceCell = doors[0].Cell,
                    Point = GridCoords.CellCenter(doors[0].Cell),
                });
            }

            return sites;
        }

        /// <summary>How many haulers are already working each building, for the concurrency cap of §8.</summary>
        private NativeHashMap<Entity, int> CountHaulersPerTarget(ref SystemState state)
        {
            var busy = new NativeHashMap<Entity, int>(32, Allocator.Temp);

            foreach (RefRO<AssignedOrder> assigned in SystemAPI.Query<RefRO<AssignedOrder>>())
            {
                Entity target = assigned.ValueRO.Target;
                busy.TryGetValue(target, out int count);
                busy[target] = count + 1;
            }

            return busy;
        }

        private static NativeArray<OrderRank> RankOrders(OrderBook book)
        {
            var ranked = new NativeArray<OrderRank>(book.Length, Allocator.Temp);
            for (int i = 0; i < book.Length; i++)
            {
                ranked[i] = new OrderRank { Index = i, Effective = book[i].Effective };
            }

            ranked.Sort(new HighestEffectiveFirst());
            return ranked;
        }

        /// <summary>
        /// An order whose demand has been fully claimed is done being advertised. It is not *finished* -
        /// nothing has moved yet - but there is nothing left of it to hand to anyone else.
        /// </summary>
        private static void PruneEmptyOrders(OrderBook book)
        {
            for (int i = book.Length - 1; i >= 0; i--)
            {
                if (book[i].Amount <= 0)
                {
                    book.RemoveAt(i);
                }
            }
        }

        private bool TryEntranceOf(Entity building, out int2 cell)
        {
            if (_entrances.TryGetBuffer(building, out DynamicBuffer<BuildingEntranceCell> doors)
                && !doors.IsEmpty)
            {
                cell = doors[0].Cell;
                return true;
            }

            cell = default;
            return false;
        }

        private int MaxHaulersFor(Entity building) =>
            _limits.TryGetComponent(building, out HaulLimit limit) && limit.MaxConcurrent > 0
                ? limit.MaxConcurrent
                : DEFAULT_MAX_CONCURRENT_HAULERS;

        private struct FreeAgent
        {
            public Entity Entity;
            public float2 Position;
            public int CarryCapacity;

            /// <summary>The building it is resting in, or <see cref="Entity.Null"/> if it is out on the map.</summary>
            public Entity Inside;
        }

        private struct StorageSite
        {
            public Entity Building;
            public int2 EntranceCell;
            public float2 Point;
        }

        private struct OrderRank
        {
            public int Index;
            public float Effective;
        }

        private struct HighestEffectiveFirst : IComparer<OrderRank>
        {
            public int Compare(OrderRank x, OrderRank y) => y.Effective.CompareTo(x.Effective);
        }
    }
}
