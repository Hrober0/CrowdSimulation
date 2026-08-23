using GridNav;
using Rts;
using Unity.Entities;
using Unity.Jobs;

namespace Examples.Rts
{
    /// <summary>
    /// One-way sync of agent state onto the pooled GameObjects (design §10, §13.3 #22). Nothing here writes
    /// simulation state - it reads <see cref="AgentMove"/> and <see cref="DoorUse"/> and writes transforms,
    /// and that is all.
    ///
    /// The pool belongs to the scene rather than to the system: a prefab is a scene decision, and a system
    /// that outlives play mode should not be holding GameObjects. <see cref="AgentViewSettings"/> hands one
    /// over while it is enabled and takes it back afterwards; with no pool bound this system does nothing.
    /// </summary>
    [UpdateInGroup(typeof(RtsViewGroup))]
    public partial class ViewSyncSystem : SystemBase
    {
        private AgentViewPool _pool;
        private JobHandle _sync;
        private ComponentLookup<DoorUse> _doors;
        private ComponentLookup<OnBridge> _bridges;

        /// <summary>Sorting offset towards the camera, in world units. Presentation only.</summary>
        public float Depth { get; set; }

        /// <summary>
        /// How much further towards the camera an agent goes while it is crossing a bridge, in world units.
        ///
        /// It has to clear the front face of the building quad the bridge is drawn as, which stands half its
        /// thickness in front of its own centre - so this is a fact about the building prefab rather than a
        /// taste, and it is a field so a different prefab can say a different number.
        /// </summary>
        public float BridgeLift { get; set; } = 1f;

        /// <summary>How fast a view may swing round to face where it is going. Presentation only.</summary>
        public float TurnDegreesPerSecond { get; set; } = 270f;

        /// <summary>How far from the camera a view is still worth having. Default is "no culling".</summary>
        public ViewCulling Culling { get; set; }

        public void Bind(AgentViewPool pool)
        {
            CompleteSync();
            _pool = pool;
        }

        /// <summary>
        /// Takes the pool back. Guarded on identity so a late unbind from an old owner cannot detach the
        /// pool a new one has already bound - which is exactly what happens on a scene reload, where the
        /// incoming object's <c>OnEnable</c> runs before the outgoing object's <c>OnDisable</c>.
        /// </summary>
        public void Unbind(AgentViewPool pool)
        {
            if (_pool != pool)
            {
                return;
            }

            CompleteSync();
            _pool = null;
        }

        protected override void OnCreate()
        {
            _doors = GetComponentLookup<DoorUse>(isReadOnly: true);
            _bridges = GetComponentLookup<OnBridge>(isReadOnly: true);
        }

        protected override void OnUpdate()
        {
            // Last frame's transform writes must be done before the pool can add or remove a transform.
            CompleteSync();

            if (_pool == null)
            {
                return;
            }

            ViewCulling culling = Culling;
            _doors.Update(this);
            _bridges.Update(this);

            _pool.BeginFrame();

            foreach ((RefRO<AgentMove> agent, Entity entity)
                     in SystemAPI.Query<RefRO<AgentMove>>().WithAll<ViewVisible>().WithEntityAccess())
            {
                if (culling.IsVisible(agent.ValueRO.Position))
                {
                    _pool.Show(entity, FrameOf(entity, agent.ValueRO));
                }
            }

            _pool.EndFrame();

            _sync = _pool.Schedule(Depth, TurnDegreesPerSecond, SystemAPI.Time.DeltaTime);
        }

        /// <summary>
        /// Adds the door transition and the bridge lift, if either applies. Both are asked per agent rather
        /// than filtered on, because they are optional data on something the view already draws - and an agent
        /// whose archetype carries neither component must still be shown rather than quietly disappear.
        /// </summary>
        private AgentViewFrame FrameOf(Entity entity, in AgentMove move)
        {
            var frame = new AgentViewFrame { Move = move };

            // Over the deck rather than under it. Only while it is actually being carried: an agent standing on
            // the mouth waiting its turn is on the ground like anything else, and lifting it there would draw it
            // in front of a bridge it has not got onto.
            if (_bridges.HasComponent(entity) && _bridges.IsComponentEnabled(entity))
            {
                frame.Lift = BridgeLift;
            }

            if (_doors.HasComponent(entity) && _doors.IsComponentEnabled(entity))
            {
                DoorUse door = _doors[entity];
                frame.DoorPoint = GridCoords.CellCenter(door.Cell);
                frame.DoorBlend = door.Inside;
            }

            return frame;
        }

        protected override void OnDestroy() => CompleteSync();

        private void CompleteSync()
        {
            _sync.Complete();
            _sync = default;
        }
    }
}
