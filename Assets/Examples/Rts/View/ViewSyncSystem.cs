using Rts;
using Unity.Entities;
using Unity.Jobs;

namespace Examples.Rts
{
    /// <summary>
    /// One-way sync of agent state onto the pooled GameObjects (design §10, §13.3 #22). Nothing here writes
    /// simulation state - it reads <see cref="AgentMove"/> and writes transforms, and that is all.
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

        /// <summary>Sorting offset towards the camera, in world units. Presentation only.</summary>
        public float Depth { get; set; }

        /// <summary>How fast a view may swing round to face where it is going. Presentation only.</summary>
        public float TurnDegreesPerSecond { get; set; } = 540f;

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

        protected override void OnUpdate()
        {
            // Last frame's transform writes must be done before the pool can add or remove a transform.
            CompleteSync();

            if (_pool == null)
            {
                return;
            }

            ViewCulling culling = Culling;

            _pool.BeginFrame();

            foreach ((RefRO<AgentMove> agent, Entity entity)
                     in SystemAPI.Query<RefRO<AgentMove>>().WithAll<ViewVisible>().WithEntityAccess())
            {
                if (culling.IsVisible(agent.ValueRO.Position))
                {
                    _pool.Show(entity, agent.ValueRO);
                }
            }

            _pool.EndFrame();

            _sync = _pool.Schedule(Depth, TurnDegreesPerSecond, SystemAPI.Time.DeltaTime);
        }

        protected override void OnDestroy() => CompleteSync();

        private void CompleteSync()
        {
            _sync.Complete();
            _sync = default;
        }
    }
}
