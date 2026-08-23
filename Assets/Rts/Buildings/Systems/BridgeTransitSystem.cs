using CustomNativeCollections;
using GridNav;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// Carries agents over bridges: admits them at the near bank, moves them along the deck at their own pace,
    /// and puts them down on the far one (design §6).
    ///
    /// **Nothing here decides that an agent wants to cross.** The flow field does, and it does it by pricing the
    /// mouth cell - if the cheapest way on from that cell is the <see cref="NavLink"/>, the field stores
    /// <see cref="FlowField.LINK_STEP"/> there and this system finds an agent standing on it with nothing to
    /// follow. That separation is what makes a bridge work for every task at once: a hauler, a worker walking to
    /// a shift and an idle agent going to bed all cross the same bridge without any of them having a step in
    /// their task that mentions it, because crossing is a property of the route and not of the job.
    ///
    /// **A crossing is a walk with the logic taken out, not an absence.** <see cref="PathFollow"/> goes off, so
    /// nothing steers the agent, nothing integrates it, and <see cref="WatchdogSystem"/> does not watch it -
    /// which is the same three-way consequence <see cref="InteriorTransitionSystem"/> gets from the same flag.
    /// <see cref="AgentMove"/> stays *on*, deliberately: the agent is still on the map, still drawn, still in
    /// the spatial hash and still something the crowd at either mouth has to avoid. An agent that vanished for
    /// the length of the crossing would reappear in the middle of whatever had gathered on the far bank, which
    /// is the problem <see cref="DoorUse"/> exists to avoid at a door.
    ///
    /// **Single file, and that is the whole of the blocking rule.** Occupants are kept front first and none may
    /// advance past the one ahead of it. So an agent that cannot step off the far bank - because somebody is
    /// standing on it - stops, the one behind it stops behind it, the deck fills back to the mouth, and
    /// admission stops because there is no room at the near end. Nothing counts blocked agents or decides when
    /// a bridge is "full": being full is what a queue that cannot drain *is*.
    ///
    /// Single-threaded, after <see cref="TaskStepSystem"/> and before anything that reads a route, because each
    /// admission writes two entities at once - the agent's flags and the bridge's queue - which is the shape
    /// §13.2 invariant 4 keeps off the job system. Running before the routing systems is what lets it take an
    /// agent out of the walking set for the frame in which it was admitted, rather than a frame later with a
    /// preferred velocity already computed for a walk onto a blocked deck.
    /// </summary>
    [UpdateInGroup(typeof(RtsAgentGroup))]
    [UpdateAfter(typeof(TaskStepSystem))]
    [UpdateBefore(typeof(PathRouteSystem))]
    public partial struct BridgeTransitSystem : ISystem
    {
        /// <summary>
        /// How far apart occupants are kept, in cells.
        ///
        /// One cell, because that is what an agent takes up everywhere else on the map. It is also what makes
        /// <see cref="Bridge.Capacity"/> the span rather than a number somebody chose: a deck of N cells holds
        /// N agents a cell apart, and both halves of that follow from this constant.
        /// </summary>
        private const float SPACING = 1f;

        /// <summary>
        /// How clear the far bank has to be before the agent at the head may step onto it.
        ///
        /// The same question <see cref="IdleAssignSystem"/> asks of a parking spot, and for the same reason -
        /// two agents on one cell is a pair that cannot resolve each other - but with a consequence a parking
        /// spot does not have: refusing here is what backs the deck up and stops the mouth admitting. This is
        /// the cell where "agents that were not able to get off" comes from.
        /// </summary>
        private const float EXIT_CLEARANCE = 0.8f;

        /// <summary>
        /// How long the head of the deck waits for a clear bank before stepping onto it regardless.
        ///
        /// It is the one wait in the feature that nothing else bounds - see <see cref="BridgeOccupant.Waiting"/>
        /// - and three seconds is chosen the same way <see cref="WatchdogSystem"/>'s stall window is: long
        /// enough that ordinary traffic on the bank clears first, short enough that nobody watching the bridge
        /// concludes it has broken.
        /// </summary>
        private const float MAX_WAIT_AT_FAR_END = 3f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridWorld>();
            state.RequireForUpdate<FlowFieldCache>();
            state.RequireForUpdate<AgentSpatialHash>();
        }

        public void OnUpdate(ref SystemState state)
        {
            GridMap map = SystemAPI.GetSingleton<GridWorld>().Map;
            FlowFieldCache cache = SystemAPI.GetSingleton<FlowFieldCache>();
            NativeSpatialHash<AgentMove> crowd = SystemAPI.GetSingleton<AgentSpatialHash>().Hash;
            float deltaTime = SystemAPI.Time.DeltaTime;

            // Orphans first: an agent whose bridge has gone has to be back on the map before anything else
            // this frame looks at it, or it spends a frame as neither walking nor carried.
            ReleaseOrphans(ref state, map);

            RunBridges(ref state, crowd, deltaTime);
            Admit(ref state, cache);
        }

        /// <summary>
        /// Puts down every agent whose bridge no longer exists, or no longer says it is carrying it.
        ///
        /// This is the rule that keeps the two records honest (see <see cref="OnBridge"/>). A bridge destroyed
        /// with agents on it takes its queue with it, and without this they would keep their steering disabled
        /// for good - stood on a deck that is not there, with nothing anywhere that knows to give them back.
        /// It is the stale-claim rule of §14 step 8 one layer down, and it is here rather than in each of the
        /// several ways a bridge can stop existing for the same reason: one rule cannot be forgotten in one
        /// of them.
        /// </summary>
        private void ReleaseOrphans(ref SystemState state, in GridMap map)
        {
            EntityManager entities = state.EntityManager;
            var stranded = new NativeList<Entity>(4, Allocator.Temp);

            foreach ((RefRO<OnBridge> carried, Entity agent) in SystemAPI.Query<RefRO<OnBridge>>()
                                                                          .WithEntityAccess())
            {
                if (!IsStillCarried(entities, carried.ValueRO.Bridge, agent))
                {
                    stranded.Add(agent);
                }
            }

            foreach (Entity agent in stranded)
            {
                var carried = entities.GetComponentData<OnBridge>(agent);

                // The near bank, because it is where the agent came from and therefore somewhere it could
                // reach. Falling back to standing still is not a worse answer than being moved to a cell that
                // may since have been built over.
                int2 down = map.IsPassable(carried.Entry) ? carried.Entry : carried.Exit;

                PutDown(entities, agent, map.IsPassable(down)
                    ? GridCoords.CellCenter(down)
                    : entities.GetComponentData<AgentMove>(agent).Position);
            }

            stranded.Dispose();
        }

        private static bool IsStillCarried(in EntityManager entities, Entity bridge, Entity agent)
        {
            if (!entities.Exists(bridge) || !entities.HasBuffer<BridgeOccupant>(bridge))
            {
                return false;
            }

            foreach (BridgeOccupant occupant in entities.GetBuffer<BridgeOccupant>(bridge))
            {
                if (occupant.Agent == agent)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Runs every bridge: drops what is no longer on it, lets the head off if it can go, then moves the
        /// rest up.
        ///
        /// The order is what makes the queue drain in one frame rather than one place per frame. Releasing
        /// before advancing means the gap the head leaves is there for the next agent to move into
        /// immediately; advancing first would have everyone shuffle up behind an agent that was about to leave
        /// anyway, and a deck of eight would take eight frames to notice the far bank had cleared.
        /// </summary>
        private void RunBridges(ref SystemState state, in NativeSpatialHash<AgentMove> crowd, float deltaTime)
        {
            EntityManager entities = state.EntityManager;

            foreach ((RefRO<Bridge> bridge, DynamicBuffer<BridgeOccupant> occupants)
                     in SystemAPI.Query<RefRO<Bridge>, DynamicBuffer<BridgeOccupant>>())
            {
                DropDeparted(entities, occupants);
                TryReleaseHead(entities, crowd, bridge.ValueRO, occupants, deltaTime);
                Advance(entities, bridge.ValueRO, occupants, deltaTime);
            }
        }

        /// <summary>Agents that have died, or that something else has already taken off this bridge.</summary>
        private static void DropDeparted(in EntityManager entities, DynamicBuffer<BridgeOccupant> occupants)
        {
            for (int i = occupants.Length - 1; i >= 0; i--)
            {
                Entity agent = occupants[i].Agent;

                bool carried = entities.Exists(agent)
                               && entities.HasComponent<OnBridge>(agent)
                               && entities.IsComponentEnabled<OnBridge>(agent);

                if (!carried)
                {
                    occupants.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Lets the agent at the head off, if it has reached the far end and the bank is free - or if it has
        /// been waiting for one long enough that waiting is worse than the overlap.
        ///
        /// One agent per frame at most, which needs no rule of its own: the second one cannot be at the far end
        /// too, because it is being kept <see cref="SPACING"/> behind the first.
        /// </summary>
        private static void TryReleaseHead(in EntityManager entities, in NativeSpatialHash<AgentMove> crowd,
                                           in Bridge bridge, DynamicBuffer<BridgeOccupant> occupants,
                                           float deltaTime)
        {
            if (occupants.IsEmpty || occupants[0].Distance < bridge.Span)
            {
                return;
            }

            Entity agent = occupants[0].Agent;
            float2 bank = GridCoords.CellCenter(bridge.Exit);

            if (!IsClear(crowd, agent, bank))
            {
                BridgeOccupant head = occupants[0];
                head.Waiting += deltaTime;
                occupants[0] = head;

                if (head.Waiting < MAX_WAIT_AT_FAR_END)
                {
                    return;
                }
            }

            occupants.RemoveAt(0);
            PutDown(entities, agent, bank);

            // Nothing is done to the task. The agent's GoTo is still at the head of its buffer naming the same
            // destination it always did, so TaskStepSystem starts a fresh walk from the far bank next frame -
            // which re-routes from where the agent now is, rather than from wherever it was when it set off.
        }

        /// <summary>
        /// Moves every occupant along the deck, each at its own speed and none past the one in front.
        ///
        /// The limit walks down the queue with the loop, so the rule is stated once and holds for the whole
        /// line: the head may reach the far end, and everybody else may reach a cell short of whoever is ahead.
        /// A faster agent behind a slower one simply closes up and then matches it, which is what a queue on a
        /// footbridge looks like and needed no code of its own.
        /// </summary>
        private static void Advance(in EntityManager entities, in Bridge bridge,
                                    DynamicBuffer<BridgeOccupant> occupants, float deltaTime)
        {
            float2 from = GridCoords.CellCenter(bridge.Entry);
            float2 to = GridCoords.CellCenter(bridge.Exit);
            float2 heading = bridge.Heading;

            float limit = bridge.Span;

            for (int i = 0; i < occupants.Length; i++)
            {
                BridgeOccupant occupant = occupants[i];
                var move = entities.GetComponentData<AgentMove>(occupant.Agent);

                float step = move.MaxSpeed * deltaTime;

                float wanted = occupant.Distance + step;
                float reached = math.clamp(math.min(wanted, limit), 0f, bridge.Span);
                float travelled = math.max(reached - occupant.Distance, 0f);

                occupant.Distance = reached;

                // Walked off at the agent's own pace rather than cancelled, so boarding is a step onto the
                // bridge instead of a jump to it. It closes within a cell, and once closed it costs nothing.
                occupant.Offset = MoveTowardsZero(occupant.Offset, step);

                occupants[i] = occupant;

                float2 onDeck = math.lerp(from, to, bridge.Span > 0 ? reached / bridge.Span : 1f);
                move.Position = onDeck + occupant.Offset;

                // An honest velocity, not a decoration: the crowd at either mouth reads its neighbours'
                // velocities out of the spatial hash, and an agent that is moving while claiming to be still is
                // one that avoidance plans around wrongly.
                move.Velocity = deltaTime > 0f ? heading * (travelled / deltaTime) : float2.zero;
                move.PrefVelocity = move.Velocity;

                entities.SetComponentData(occupant.Agent, move);

                // Re-asserted every frame because TaskStepSystem turns it back on every frame: the agent's
                // task still says "walk to that cell", and it is right to. Cheaper than teaching the task
                // machine about a state it has no business knowing.
                entities.SetComponentEnabled<PathFollow>(occupant.Agent, false);

                limit = reached - SPACING;
            }
        }

        /// <summary>
        /// Admits agents that are standing on a mouth and whose route says to cross.
        ///
        /// Both halves are needed and neither implies the other. Standing on the mouth without the route is an
        /// agent walking past a bridge it does not want, and wanting the route without standing on the mouth is
        /// an agent still on its way to it. The route half is one array read, because the field that answers it
        /// is the same one the agent is already following.
        /// </summary>
        private void Admit(ref SystemState state, in FlowFieldCache cache)
        {
            EntityManager entities = state.EntityManager;

            NativeHashMap<int2, Entity> mouths = CollectOpenMouths(ref state);
            if (mouths.IsEmpty)
            {
                mouths.Dispose();
                return;
            }

            // Collected before anything is applied: admitting disables the component this query filters on,
            // and changing that under the iteration is asking the enumerator to skip agents at random.
            var admitted = new NativeList<Admission>(4, Allocator.Temp);

            foreach ((RefRO<AgentMove> agent, RefRO<PathFollow> path, Entity entity)
                     in SystemAPI.Query<RefRO<AgentMove>, RefRO<PathFollow>>().WithEntityAccess())
            {
                int2 cell = GridCoords.CellOf(agent.ValueRO.Position);

                if (!mouths.TryGetValue(cell, out Entity bridge))
                {
                    continue;
                }

                if (!cache.TryGetSlot(path.ValueRO.WaypointCell, out int slot)
                    || !cache.IsLinkStep(slot, cell))
                {
                    continue;
                }

                admitted.Add(new Admission { Agent = entity, Bridge = bridge });
            }

            foreach (Admission admission in admitted)
            {
                Board(entities, admission);
            }

            admitted.Dispose();
            mouths.Dispose();
        }

        /// <summary>
        /// The mouth cell of every bridge with room for one more.
        ///
        /// Room is two conditions and both are about the near end: the deck may not hold more than its span,
        /// and the last agent on it has to be clear of the mouth. The second is what turns a jam at the far
        /// bank into a closed entrance - the queue backs up until the rearmost occupant is standing at the
        /// mouth, and then nobody else gets on.
        /// </summary>
        private NativeHashMap<int2, Entity> CollectOpenMouths(ref SystemState state)
        {
            var mouths = new NativeHashMap<int2, Entity>(4, Allocator.Temp);

            foreach ((RefRO<Bridge> bridge, DynamicBuffer<BridgeOccupant> occupants, Entity entity)
                     in SystemAPI.Query<RefRO<Bridge>, DynamicBuffer<BridgeOccupant>>().WithEntityAccess())
            {
                if (occupants.Length >= bridge.ValueRO.Capacity)
                {
                    continue;
                }

                if (!occupants.IsEmpty && occupants[occupants.Length - 1].Distance < SPACING)
                {
                    continue;
                }

                mouths.TryAdd(bridge.ValueRO.Entry, entity);
            }

            return mouths;
        }

        private static void Board(in EntityManager entities, in Admission admission)
        {
            if (!entities.Exists(admission.Bridge) || !entities.HasComponent<Bridge>(admission.Bridge))
            {
                return;
            }

            var bridge = entities.GetComponentData<Bridge>(admission.Bridge);
            DynamicBuffer<BridgeOccupant> occupants = entities.GetBuffer<BridgeOccupant>(admission.Bridge);

            // Re-checked here rather than trusted from the collection pass, because several agents can be
            // standing on one mouth and the first of them to board is what fills the place the rest were told
            // about.
            if (occupants.Length >= bridge.Capacity
                || (!occupants.IsEmpty && occupants[occupants.Length - 1].Distance < SPACING))
            {
                return;
            }

            // Boarded from where the agent actually stands, not from the mouth's centre. The along-deck part
            // becomes distance already travelled and the rest becomes an offset to walk off, so nothing about
            // getting on the bridge moves the agent other than its own legs.
            float2 position = entities.GetComponentData<AgentMove>(admission.Agent).Position;
            float2 mouth = GridCoords.CellCenter(bridge.Entry);
            float2 heading = bridge.Heading;

            float along = math.clamp(math.dot(position - mouth, heading), 0f, bridge.Span);

            occupants.Add(new BridgeOccupant
            {
                Agent = admission.Agent,
                Distance = along,
                Offset = position - (mouth + heading * along),
            });

            entities.SetComponentData(admission.Agent, new OnBridge
            {
                Bridge = admission.Bridge,
                Entry = bridge.Entry,
                Exit = bridge.Exit,
            });

            entities.SetComponentEnabled<OnBridge>(admission.Agent, true);
            entities.SetComponentEnabled<PathFollow>(admission.Agent, false);
        }

        /// <summary>
        /// Back on the map at a point, standing, with a clean watchdog.
        ///
        /// Resetting the stall timer is not tidiness. Progress is measured against the last place the agent
        /// actually got to, and the crossing moved it without the watchdog watching - so a timer left alone
        /// would be comparing the far bank against a position on the other side of the deck. It happens to
        /// read as progress today, which is the wrong reason for it to be right.
        ///
        /// <see cref="PathFollow"/> is left off. The agent's task still names its destination, so
        /// <see cref="TaskStepSystem"/> starts the walk again next frame and works out a route from where the
        /// agent now is - one frame of standing on the bank, and no stale route to unpick.
        /// </summary>
        private static void PutDown(in EntityManager entities, Entity agent, float2 position)
        {
            if (!entities.Exists(agent))
            {
                return;
            }

            if (entities.HasComponent<AgentMove>(agent))
            {
                var move = entities.GetComponentData<AgentMove>(agent);
                move.Position = position;
                move.Velocity = float2.zero;
                move.PrefVelocity = float2.zero;
                entities.SetComponentData(agent, move);
            }

            if (entities.HasComponent<MovementWatchdog>(agent))
            {
                entities.SetComponentData(agent, new MovementWatchdog { LastProgressPosition = position });
            }

            if (entities.HasComponent<OnBridge>(agent))
            {
                entities.SetComponentEnabled<OnBridge>(agent, false);
            }
        }

        /// <summary>Shortens a vector by <paramref name="step"/>, stopping at zero rather than overshooting.</summary>
        private static float2 MoveTowardsZero(float2 offset, float step)
        {
            float length = math.length(offset);
            return length <= step || length < math.EPSILON ? float2.zero : offset * (1f - step / length);
        }

        /// <summary>Whether anybody other than this agent is standing on a point.</summary>
        private static bool IsClear(in NativeSpatialHash<AgentMove> crowd, Entity agent, float2 point)
        {
            var probe = new CrowdProbe
            {
                Self = agent,
                Point = point,
                RadiusSq = EXIT_CLEARANCE * EXIT_CLEARANCE,
            };

            crowd.ForEachInAABB(point - EXIT_CLEARANCE, point + EXIT_CLEARANCE, ref probe);
            return !probe.Taken;
        }

        private struct Admission
        {
            public Entity Agent;
            public Entity Bridge;
        }

        private struct CrowdProbe : ISpatialQueryProcessor<AgentMove>
        {
            public Entity Self;
            public float2 Point;
            public float RadiusSq;
            public bool Taken;

            public void Process(AgentMove agent)
            {
                if (agent.Entity == Self)
                {
                    return;
                }

                Taken |= math.distancesq(agent.Position, Point) < RadiusSq;
            }
        }
    }
}
