using System;
using System.Collections.Generic;
using Rts;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

namespace Examples.Rts
{
    /// <summary>
    /// The pool of agent GameObjects and the viewIndex &lt;-&gt; entity mapping behind it (design §10).
    ///
    /// The simulation owns every position in native memory; a view is a borrowed prefab instance that happens
    /// to be standing where an agent is. Instances are never destroyed while the pool lives - an agent that
    /// walks into a building or out of the culling radius hands its instance back, and the next agent that
    /// needs one takes it. So the number of GameObjects tracks the number of *visible* agents, not the number
    /// of agents.
    ///
    /// A frame is <see cref="BeginFrame"/>, a <see cref="Show"/> per agent that deserves a view, then
    /// <see cref="EndFrame"/>: anything not shown is released. Stating what should be visible is cheaper to
    /// get right than tracking every event that could make something appear or disappear, and it cannot drift.
    ///
    /// The three views of the same list stay in lockstep because only <see cref="Acquire"/> and
    /// <see cref="Release"/> ever change its shape, and both use swap-back on all of them.
    /// </summary>
    public sealed class AgentViewPool : IDisposable
    {
        private readonly GameObject _prefab;

        private readonly Stack<GameObject> _idle = new();
        private readonly List<GameObject> _created = new();
        private readonly List<ViewSlot> _slots = new();
        private readonly Dictionary<Entity, int> _viewOf = new();

        private TransformAccessArray _transforms;
        private NativeList<AgentMove> _frameData;

        private int _tick;

        public AgentViewPool(GameObject prefab, int prewarm)
        {
            _prefab = prefab != null ? prefab : throw new ArgumentNullException(nameof(prefab));

            int capacity = math.max(prewarm, 1);
            _transforms = new TransformAccessArray(capacity);
            _frameData = new NativeList<AgentMove>(capacity, Allocator.Persistent);

            for (int i = 0; i < prewarm; i++)
            {
                _idle.Push(Create());
            }
        }

        /// <summary>How many agents are being shown right now.</summary>
        public int ActiveCount => _slots.Count;

        /// <summary>How many instances are parked, waiting to be handed out again.</summary>
        public int IdleCount => _idle.Count;

        /// <summary>Instances ever made. Its high-water mark is the real cost of the view layer.</summary>
        public int InstanceCount => _created.Count;

        /// <summary>Opens a frame. Every view is unwanted until a <see cref="Show"/> claims it.</summary>
        public void BeginFrame() => _tick++;

        /// <summary>Claims a view for this agent, taking one from the pool if it does not have one yet.</summary>
        public void Show(Entity entity, in AgentMove move)
        {
            if (!_viewOf.TryGetValue(entity, out int view))
            {
                view = Acquire(entity);
            }

            _slots[view] = new ViewSlot
            {
                Entity = entity,
                Move = move,
                SeenTick = _tick,
            };
        }

        /// <summary>Closes a frame, returning every view no <see cref="Show"/> claimed.</summary>
        public void EndFrame()
        {
            // Backwards, because releasing swaps the last slot into the hole. Everything above the cursor has
            // already been judged, so the slot that lands there needs no second look.
            for (int view = _slots.Count - 1; view >= 0; view--)
            {
                if (_slots[view].SeenTick != _tick)
                {
                    Release(view);
                }
            }
        }

        /// <summary>
        /// Writes this frame's positions onto the transforms. The only way to move a thousand
        /// <see cref="Transform"/>s without a main-thread stall (§10).
        /// </summary>
        /// <param name="depth">Sorting offset towards the camera, in world units.</param>
        public JobHandle Schedule(float depth, JobHandle dependency = default)
        {
            _frameData.ResizeUninitialized(_slots.Count);
            for (int view = 0; view < _slots.Count; view++)
            {
                _frameData[view] = _slots[view].Move;
            }

            return new WriteTransformsJob
            {
                Views = _frameData.AsArray(),
                Depth = depth,
            }.Schedule(_transforms, dependency);
        }

        public void Dispose()
        {
            if (_transforms.isCreated)
            {
                _transforms.Dispose();
            }

            if (_frameData.IsCreated)
            {
                _frameData.Dispose();
            }

            foreach (GameObject instance in _created)
            {
                if (instance != null)
                {
                    Destroy(instance);
                }
            }

            _created.Clear();
            _idle.Clear();
            _slots.Clear();
            _viewOf.Clear();
        }

        private int Acquire(Entity entity)
        {
            GameObject instance = _idle.Count > 0 ? _idle.Pop() : Create();
            instance.SetActive(true);

            int view = _slots.Count;
            _transforms.Add(instance.transform);
            _slots.Add(new ViewSlot { Entity = entity });
            _viewOf[entity] = view;

            return view;
        }

        private void Release(int view)
        {
            GameObject instance = _transforms[view].gameObject;
            instance.SetActive(false);
            _idle.Push(instance);

            _viewOf.Remove(_slots[view].Entity);

            int last = _slots.Count - 1;
            _transforms.RemoveAtSwapBack(view);
            _slots[view] = _slots[last];
            _slots.RemoveAt(last);

            if (view < _slots.Count)
            {
                _viewOf[_slots[view].Entity] = view;
            }
        }

        /// <summary>
        /// Instances are left as scene roots on purpose: <see cref="IJobParallelForTransform"/> only runs in
        /// parallel over transforms that are roots, so parenting the pool under one tidy container would
        /// quietly serialise the whole sync.
        /// </summary>
        private GameObject Create()
        {
            GameObject instance = UnityEngine.Object.Instantiate(_prefab);
            instance.SetActive(false);
            _created.Add(instance);
            return instance;
        }

        private static void Destroy(GameObject instance)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(instance);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private struct ViewSlot
        {
            public Entity Entity;
            public AgentMove Move;

            /// <summary>The frame this view was last claimed on. Anything older is handed back.</summary>
            public int SeenTick;
        }

        [BurstCompile]
        private struct WriteTransformsJob : IJobParallelForTransform
        {
            [ReadOnly] public NativeArray<AgentMove> Views;

            public float Depth;

            public void Execute(int index, TransformAccess transform)
            {
                AgentMove move = Views[index];

                transform.position = SimToWorld.Position(move.Position, Depth);

                // Only while actually moving: a stopped agent keeps facing wherever it last walked, instead
                // of snapping back to a default heading the moment its velocity hits zero.
                if (math.lengthsq(move.Velocity) > math.EPSILON)
                {
                    transform.rotation = SimToWorld.Rotation(move.Velocity);
                }
            }
        }
    }
}
