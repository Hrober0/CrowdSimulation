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

        /// <summary>
        /// Used by buildings that do not set their own <see cref="HaulLimit"/> (§15: tuning).
        ///
        /// Raised from the four §15 said to start at, once the door became a resource with a known service
        /// time and the queue for it formed along the road in (§15, <see cref="ArrivalQueueSystem"/>). The cap
        /// exists to stop a crowd converging on one step, and what a crowd does at a step is now a line: the
        /// eighth hauler waits its turn a few cells back instead of pressing into the seven in front. Keeping
        /// it at four with all of that in place means haulers wait in a hut for a door that is standing idle.
        /// </summary>
        private const int DEFAULT_MAX_CONCURRENT_HAULERS = 8;

        private const float PICKUP_SECONDS = 0.5f;
        private const float DEPOSIT_SECONDS = 0.5f;

        private BufferLookup<StorageSlot> _slots;
        private BufferLookup<BuildingEntranceCell> _entrances;
        private BufferLookup<TaskStep> _steps;
        private ComponentLookup<AssignedOrder> _orders;
        private ComponentLookup<HaulLimit> _limits;
        private ComponentLookup<Interior> _interiors;
        private ComponentLookup<InteriorClaim> _claims;
        private ComponentLookup<Recipe> _recipes;
        private ComponentLookup<Reaps> _reaps;
        private ComponentLookup<CellObject> _cellObjects;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<OrderBook>();
            state.RequireForUpdate<GridWorld>();
            state.RequireForUpdate<FlowFieldCache>();
            state.RequireForUpdate<CellObjectMap>();

            _slots = state.GetBufferLookup<StorageSlot>();
            _entrances = state.GetBufferLookup<BuildingEntranceCell>(isReadOnly: true);
            _steps = state.GetBufferLookup<TaskStep>();
            _orders = state.GetComponentLookup<AssignedOrder>();
            _limits = state.GetComponentLookup<HaulLimit>(isReadOnly: true);
            _interiors = state.GetComponentLookup<Interior>();
            _claims = state.GetComponentLookup<InteriorClaim>();
            _recipes = state.GetComponentLookup<Recipe>(isReadOnly: true);
            _reaps = state.GetComponentLookup<Reaps>(isReadOnly: true);
            _cellObjects = state.GetComponentLookup<CellObject>(isReadOnly: true);
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
            _interiors.Update(ref state);
            _claims.Update(ref state);
            _recipes.Update(ref state);
            _reaps.Update(ref state);
            _cellObjects.Update(ref state);

            NativeList<FreeAgent> agents = CollectFreeAgents(ref state);
            if (agents.Length == 0)
            {
                agents.Dispose();
                return;
            }

            NativeList<StorageSite> sites = CollectStorageSites(ref state);
            NativeHashMap<Entity, int> busy = CountHaulersPerTarget(ref state);
            NativeArray<OrderRank> ranked = RankOrders(book);

            var reach = new Reachability(
                SystemAPI.GetSingleton<FlowFieldCache>(),
                SystemAPI.GetSingleton<GridWorld>().Map
            );

            CellObjectMap objects = SystemAPI.GetSingleton<CellObjectMap>();

            double now = SystemAPI.Time.ElapsedTime;

            int claims = 0;
            for (int i = 0; i < ranked.Length && claims < MAX_CLAIMS_PER_TICK && agents.Length > 0; i++)
            {
                if (TryAssign(book, ranked[i].Index, sites, agents, busy, reach, objects, now))
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
            NativeHashMap<Entity, int> busy,
            in Reachability reach,
            in CellObjectMap objects,
            double now)
        {
            Order order = book[orderIndex];

            return order.Kind switch
            {
                OrderKind.Haul => TryAssignHaul(book, orderIndex, sites, agents, busy, reach, now),

                // A gatherer's work order looks like a crafter's and is told apart by what the building is:
                // one that harvests wants somebody sent *out*, so the two part company here rather than in
                // the request systems, which post the identical order for the identical reason.
                OrderKind.Work => _reaps.HasComponent(order.Target)
                    ? TryAssignGather(book, orderIndex, agents, reach, objects, now)
                    : TryAssignWork(book, orderIndex, agents, reach, now),
                _ => false,
            };
        }

        /// <summary>
        /// Sends a worker to a building that has asked for one. The claim is taken here - before the walk -
        /// exactly as a bed in a hut is, which is what keeps six agents from converging on a workshop with
        /// two benches (§6, §8).
        /// </summary>
        private bool TryAssignWork(
            OrderBook book,
            int orderIndex,
            NativeList<FreeAgent> agents,
            in Reachability reach,
            double now)
        {
            Order order = book[orderIndex];
            if (order.Amount <= 0 || !TryEntranceOf(order.Target, out int2 door))
            {
                return false;
            }

            if (!_interiors.TryGetComponent(order.Target, out Interior interior) || !interior.HasRoom)
            {
                return false;
            }

            if (!TryNearestAgent(agents, GridCoords.CellCenter(door), reach, door, out int agentIndex))
            {
                return false;
            }

            FreeAgent agent = agents[agentIndex];

            interior.Claimed++;
            _interiors[order.Target] = interior;

            _claims[agent.Entity] = new InteriorClaim { Building = order.Target };
            _claims.SetComponentEnabled(agent.Entity, true);

            float craftSeconds = _recipes.TryGetComponent(order.Target, out Recipe recipe)
                ? recipe.CraftSeconds
                : 1f;

            DynamicBuffer<TaskStep> steps = _steps[agent.Entity];
            steps.Clear();

            if (agent.Inside != Entity.Null && TryEntranceOf(agent.Inside, out int2 homeDoor))
            {
                steps.Add(TaskStep.Exit(agent.Inside, homeDoor));
            }

            steps.Add(TaskStep.GoToDoor(door));
            steps.Add(TaskStep.Enter(order.Target, door));

            // No Exit at the end. The worker stays and repeats while there is work, which is §9's
            // Interact(inf); InteractionSystem is what eventually sends it back out of the door.
            steps.Add(TaskStep.Work(order.Target, craftSeconds));

            _orders[agent.Entity] = new AssignedOrder
            {
                Kind = OrderKind.Work,
                Target = order.Target,
            };
            _orders.SetComponentEnabled(agent.Entity, true);

            order.Amount = 0;
            order.LastClaimedTime = now;
            book[orderIndex] = order;

            agents.RemoveAtSwapBack(agentIndex);
            return true;
        }

        /// <summary>
        /// Sends a gatherer out to the nearest thing worth harvesting, and home again (design §14 step 10).
        ///
        /// The trip is written as an ordinary haul - source, pickup, destination, deposit - and the assigned
        /// order says so, which is what lets `InteractionSystem` move the goods and `OrderCompletionSystem`
        /// unwind them without either being taught that mining exists. A seam is a shelf that happens to be
        /// standing in a field.
        ///
        /// **The bench is claimed here, before the walk**, exactly as a crafter's worker claims one - and
        /// that is what makes the mine own its miners rather than the whole map's haulers taking turns at it.
        /// The claim is given back when the trip's steps run out, and the same agent is very nearly always
        /// hired again next tick, because it is standing on the doorstep it just delivered to and is
        /// therefore the nearest free pair of hands to the door. Employment falls out of geometry, and needs
        /// no state of its own to leak.
        ///
        /// Step 6's warning about holding a claim at one building while walking to another does not reach
        /// here: the seam end claims nothing, so there is no second claim for a cycle to close on.
        /// </summary>
        private bool TryAssignGather(
            OrderBook book,
            int orderIndex,
            NativeList<FreeAgent> agents,
            in Reachability reach,
            in CellObjectMap objects,
            double now)
        {
            Order order = book[orderIndex];
            if (order.Amount <= 0 || !TryEntranceOf(order.Target, out int2 door))
            {
                return false;
            }

            if (!_interiors.TryGetComponent(order.Target, out Interior interior) || !interior.HasRoom)
            {
                return false;
            }

            Reaps reaps = _reaps[order.Target];

            if (!NodeSearch.TryFindNearest(objects, _cellObjects, _slots, reach, door, reaps,
                                           out Entity node, out int2 nodeCell))
            {
                return false;
            }

            // Hands required: a gatherer carries its load home, so an agent with no capacity is no use here
            // however good a worker it would be at a bench.
            if (!TryNearestAgent(agents, GridCoords.CellCenter(door), reach, door, out int agentIndex,
                                 mustCarry: true))
            {
                return false;
            }

            FreeAgent agent = agents[agentIndex];

            DynamicBuffer<StorageSlot> nodeSlots = _slots[node];
            DynamicBuffer<StorageSlot> homeSlots = _slots[order.Target];

            int amount = AmountToFetch(nodeSlots, homeSlots, reaps.Yields, agent.CarryCapacity);
            if (amount <= 0)
            {
                return false;
            }

            // Both ends held before anyone sets off, so a second gatherer sent to the same seam on the same
            // tick is given what is left of it rather than the same ore twice.
            if (!StorageSlotUtils.TryReserveBoth(ref nodeSlots, ref homeSlots, reaps.Yields, amount))
            {
                return false;
            }

            interior.Claimed++;
            _interiors[order.Target] = interior;

            _claims[agent.Entity] = new InteriorClaim { Building = order.Target };
            _claims.SetComponentEnabled(agent.Entity, true);

            DynamicBuffer<TaskStep> steps = _steps[agent.Entity];
            steps.Clear();

            if (agent.Inside != Entity.Null && TryEntranceOf(agent.Inside, out int2 homeDoor))
            {
                steps.Add(TaskStep.Exit(agent.Inside, homeDoor));
            }

            // A doorway walk to the seam as well as to the door. Nothing about harvesting needs the exact
            // cell - the pickup works off the node's *entity* - and insisting on the centre is what makes
            // every gatherer bound for one tree steer at a single point.
            steps.Add(TaskStep.GoToDoor(nodeCell));
            steps.Add(TaskStep.Pickup(node, PICKUP_SECONDS));
            steps.Add(TaskStep.GoToDoor(door));
            steps.Add(TaskStep.Deposit(order.Target, DEPOSIT_SECONDS));

            _orders[agent.Entity] = new AssignedOrder
            {
                Kind = OrderKind.Haul,
                Source = node,
                Target = order.Target,
                Item = reaps.Yields,
                Amount = amount,
            };
            _orders.SetComponentEnabled(agent.Entity, true);

            order.Amount = 0;
            order.LastClaimedTime = now;
            book[orderIndex] = order;

            agents.RemoveAtSwapBack(agentIndex);
            return true;
        }

        /// <summary>
        /// A load: what is left in the seam, what will fit on the shelf at home, and what one pair of hands
        /// can carry - whichever runs out first. All three read past the reservations, so trips already
        /// underway are counted.
        /// </summary>
        private static int AmountToFetch(
            in DynamicBuffer<StorageSlot> source,
            in DynamicBuffer<StorageSlot> home,
            ItemId item,
            int carryCapacity)
        {
            if (!StorageSlotUtils.TryGetSlotIndex(source, item, out int from)
                || !StorageSlotUtils.TryGetSlotIndex(home, item, out int to))
            {
                return 0;
            }

            return math.min(math.min(source[from].AvailableOut, home[to].FreeCapacity), carryCapacity);
        }

        private bool TryAssignHaul(
            OrderBook book,
            int orderIndex,
            in NativeList<StorageSite> sites,
            NativeList<FreeAgent> agents,
            NativeHashMap<Entity, int> busy,
            in Reachability reach,
            double now)
        {
            Order order = book[orderIndex];
            if (order.Amount <= 0)
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

            // The loaded leg, checked before anyone is sent on the empty one. A source an agent can walk to
            // but cannot carry anything *from* is a whole round trip thrown away, and the trip after that
            // would be the same one again.
            if (!reach.CanTry(targetDoor, source.EntranceCell))
            {
                return false;
            }

            if (!TryNearestAgent(agents, source.Point, reach, source.EntranceCell, out int agentIndex,
                                 mustCarry: true))
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

            // What makes the queue take turns rather than freeze: this order has just been served, so it
            // starts banking age again from zero and its equals move ahead of it (see Order.LastClaimedTime).
            order.LastClaimedTime = now;
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

            // Doorway walks, both of them: a hauler hands goods over the threshold without going in, so it is
            // one of the agents standing on the doorstep that a busy warehouse has to keep moving.
            steps.Add(TaskStep.GoToDoor(source.EntranceCell));
            steps.Add(TaskStep.Pickup(source.Building, PICKUP_SECONDS));
            steps.Add(TaskStep.GoToDoor(targetDoor));
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

        /// <param name="mustCarry">
        /// Hauling needs hands; working does not. An agent with no carry capacity is still a perfectly good
        /// worker, so the check belongs to the kind of order rather than to who counts as free.
        /// </param>
        /// <param name="destination">
        /// Where this agent would have to walk first. An agent the field says cannot get there is passed over
        /// rather than the order being abandoned - the point is to give the job to somebody who *can*, and on
        /// a map cut in half by a one-way road that is usually the second-nearest agent rather than nobody.
        /// </param>
        /// <remarks>
        /// **No maximum distance.** There used to be one - sixty-four cells, on the reasoning that beyond it
        /// somebody nearer should take the job. But it never chose between two agents: the nearest reachable
        /// agent anywhere *is* the nearest reachable agent in range whenever one is in range, so the cap only
        /// ever fired when nobody was, and then it did not defer the work to somebody nearer, it refused the
        /// order outright. Refused identically on the next tick, and every tick after, because nothing about
        /// the geometry had changed - a permanent deadlock that looked exactly like an order patiently
        /// waiting its turn. A long walk is worse than a short one; it is much better than never going.
        ///
        /// The two tests are ordered cheap-first and the expensive one is gated on being a new leader.
        /// <see cref="Reachability.CanTry"/> is a hash probe and a pair of field reads, and asking it about
        /// an agent standing further away than one already found is work whose answer cannot change anything.
        /// That turns roughly one probe per agent into roughly one per new nearest, which is why dropping the
        /// cap leaves this loop cheaper than it was with it.
        /// </remarks>
        private static bool TryNearestAgent(
            in NativeList<FreeAgent> agents,
            float2 point,
            in Reachability reach,
            int2 destination,
            out int index,
            bool mustCarry = false)
        {
            index = -1;
            float best = float.MaxValue;

            for (int i = 0; i < agents.Length; i++)
            {
                if (mustCarry && agents[i].CarryCapacity <= 0)
                {
                    continue;
                }

                float distance = math.distancesq(agents[i].Position, point);
                if (distance >= best)
                {
                    continue;
                }

                if (!reach.CanTry(destination, agents[i].Position))
                {
                    continue;
                }

                best = distance;
                index = i;
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
                if (!steps.IsEmpty || !carry.ValueRO.IsEmpty)
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

            // Resource nodes are held out of the general market on purpose. A seam is not a warehouse with
            // an awkward door: it is reachable only to a building that harvests it, within the range that
            // building works (§14 step 10), and letting an ordinary hauler take wood off any tree on the map
            // would make forestry buildings decorative. Nodes carry no entrance buffer today, so the query
            // would miss them anyway - saying so is what keeps that an intention rather than an accident.
            foreach ((DynamicBuffer<BuildingEntranceCell> doors, Entity building)
                     in SystemAPI.Query<DynamicBuffer<BuildingEntranceCell>>()
                                 .WithAll<StorageSlot>()
                                 .WithNone<ResourceNode>()
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
                // Hauls only. A crafter is the target of both its input hauls and its own work order, and
                // counting the worker against the hauler cap would let one worker starve the building.
                if (assigned.ValueRO.Kind != OrderKind.Haul)
                {
                    continue;
                }

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
