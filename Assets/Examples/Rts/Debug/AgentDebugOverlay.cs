using System.Collections.Generic;
using GridNav;
using Rts;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Examples.Rts
{
    /// <summary>
    /// Draws what the agents near this object are doing: how fast each one is going, where it wants to go,
    /// where avoidance is letting it go, and the ground it has covered.
    ///
    /// The trail is the point of the thing. Its dots are dropped on a timer rather than per frame, so their
    /// spacing *is* the speed: evenly spaced dots are an agent walking, dots that bunch up and spread out
    /// again are an agent stopping and starting. A stop-start walk passes every position assertion in the test
    /// suite and is unmistakable here.
    ///
    /// The two velocity rays are the other half. <see cref="AgentMove.PrefVelocity"/> is intent, written by
    /// <c>PathFollowSystem</c> from the flow field; <see cref="AgentMove.Velocity"/> is what RVO allowed. When
    /// they point the same way the agent has a clear run; when they diverge it is giving way to someone.
    ///
    /// Read-only and windowed, same as <see cref="GridDebugOverlay"/> - gizmos are not free, and a thousand
    /// agents would be a thousand of everything below (design §13.3 #23).
    /// </summary>
    public class AgentDebugOverlay : MonoBehaviour
    {
        [SerializeField, Tooltip("Filled disc under each agent, coloured by speed: red stopped, green full.")]
        private bool _drawSpeed = true;

        [SerializeField, Tooltip("What avoidance allowed. White.")]
        private bool _drawVelocity = true;

        [SerializeField, Tooltip("What path following asked for. Cyan, and hidden when it matches the above.")]
        private bool _drawIntent = true;

        [SerializeField, Tooltip("The agent's body circle, for reading how close a crowd really is.")]
        private bool _drawRadius;

        [SerializeField, Tooltip("Goal cell, and the waypoint being steered at when it is not the goal.")]
        private bool _drawGoal = true;

        [SerializeField, Tooltip("For an agent waiting its turn: the ring around the destination it may not cross.")]
        private bool _drawQueue = true;

        [SerializeField, Tooltip("Agents in a doorway: a box on the entrance cell that shrinks as they go through.")]
        private bool _drawDoors = true;

        [SerializeField, Tooltip("Dots dropped on a timer. Even spacing means an even speed.")]
        private bool _drawTrail = true;

        [SerializeField, Min(1), Tooltip("Half-size, in cells, of the window drawn around this object.")]
        private int _windowRadius = 24;

        [SerializeField, Range(0.02f, 1f), Tooltip("Seconds between trail dots. Shorter reads finer stutters.")]
        private float _trailInterval = 0.1f;

        [SerializeField, Range(2, 200), Tooltip("Dots kept per agent.")]
        private int _trailLength = 40;

        [SerializeField, Min(1)]
        [Tooltip("Trails are kept for this many agents at most, the nearest to this object. A trail is for "
                 + "reading one agent's stutter; hundreds of them are a frame-time cost and an unreadable smear.")]
        private int _maxTrails = 100;

        /// <summary>
        /// Sampled in <c>Update</c> and only read in <c>OnDrawGizmos</c>: gizmos are drawn once per camera and
        /// not at all when the view is hidden, so sampling there would space the dots by draw rate instead of
        /// by time and destroy the one thing the trail is for.
        /// </summary>
        private readonly Dictionary<Entity, Trail> _trails = new();

        private readonly HashSet<Entity> _sampled = new();

        private readonly List<Entity> _forgotten = new();

        private readonly List<(float DistanceSq, Entity Agent, float2 Position)> _nearest = new();

        /// <summary>Everything one draw puts on screen, flushed in a few calls. See <see cref="GizmoBatch"/>.</summary>
        private GizmoBatch _batch;

        private float _nextSample;

        private void OnDisable()
        {
            _trails.Clear();
            _nextSample = 0f;

            _batch?.Dispose();
            _batch = null;
        }

        private void Update()
        {
            if (!_drawTrail)
            {
                _trails.Clear();
                return;
            }

            if (Time.time < _nextSample)
            {
                return;
            }

            _nextSample = Time.time + _trailInterval;
            SampleTrails();
        }

        private void SampleTrails()
        {
            if (!TryGetWorld(out EntityManager entities))
            {
                return;
            }

            using NativeArray<Entity> agents = QueryAgents(entities);

            _sampled.Clear();
            _nearest.Clear();

            float2 centre = SimToWorld.ToSim(transform.position);
            foreach (Entity agent in agents)
            {
                AgentMove move = entities.GetComponentData<AgentMove>(agent);
                if (InWindow(move.Position))
                {
                    _nearest.Add((math.distancesq(move.Position, centre), agent, move.Position));
                }
            }

            // Only the nearest few keep a trail (see _maxTrails). Ten times a second, so a sort is cheap.
            if (_nearest.Count > _maxTrails)
            {
                _nearest.Sort((a, b) => a.DistanceSq.CompareTo(b.DistanceSq));
                _nearest.RemoveRange(_maxTrails, _nearest.Count - _maxTrails);
            }

            foreach ((float _, Entity agent, float2 position) in _nearest)
            {
                if (!_trails.TryGetValue(agent, out Trail trail) || trail.Capacity != _trailLength)
                {
                    trail = new Trail(_trailLength);
                    _trails[agent] = trail;
                }

                trail.Add(position);
                _sampled.Add(agent);
            }

            Forget();
        }

        /// <summary>
        /// Drops the trails of agents this pass did not sample: dead, gone indoors, or simply out of the
        /// window. Keeping one would draw a line straight from wherever the agent left to wherever it came
        /// back, which is a journey it never made.
        /// </summary>
        private void Forget()
        {
            _forgotten.Clear();

            foreach (Entity remembered in _trails.Keys)
            {
                if (!_sampled.Contains(remembered))
                {
                    _forgotten.Add(remembered);
                }
            }

            foreach (Entity gone in _forgotten)
            {
                _trails.Remove(gone);
            }
        }

        private void OnDrawGizmos()
        {
            if (!TryGetWorld(out EntityManager entities))
            {
                return;
            }

            using NativeArray<Entity> agents = QueryAgents(entities);

            _batch ??= new GizmoBatch();

            // Asked once for the whole draw - see UiGizmos.ClearRect.
            Rect clear = UiGizmos.ClearRect();

            foreach (Entity agent in agents)
            {
                AgentMove move = entities.GetComponentData<AgentMove>(agent);
                if (!InWindow(move.Position))
                {
                    continue;
                }

                Vector3 position = SimToWorld.Position(move.Position);
                if (UiGizmos.Hides(clear, position))
                {
                    continue;
                }

                if (_drawTrail && _trails.TryGetValue(agent, out Trail trail))
                {
                    DrawTrail(trail);
                }

                if (_drawSpeed)
                {
                    DrawSpeed(position, move);
                }

                if (_drawRadius)
                {
                    _batch.WireCircle(new Color(1f, 1f, 1f, 0.25f), position, move.Radius);
                }

                if (_drawIntent && math.distancesq(move.PrefVelocity, move.Velocity) > 0.01f)
                {
                    DrawRay(Color.cyan, move.Position, move.PrefVelocity, move.MaxSpeed);
                }

                if (_drawVelocity)
                {
                    DrawRay(Color.white, move.Position, move.Velocity, move.MaxSpeed);
                }

                if (entities.IsComponentEnabled<PathFollow>(agent))
                {
                    PathFollow path = entities.GetComponentData<PathFollow>(agent);

                    if (_drawGoal)
                    {
                        DrawGoal(move, path);
                    }

                    if (_drawQueue && path.Holding)
                    {
                        DrawHold(move, path);
                    }
                }

                if (_drawDoors)
                {
                    DrawDoorUse(entities, agent, move);
                }
            }

            _batch.Flush();
        }

        /// <summary>
        /// A disc whose colour is the speed as a fraction of the agent's maximum: red stopped, yellow halfway,
        /// green flat out. A crowd that strobes red-green in step is a crowd braking for something.
        ///
        /// In eight steps rather than continuously, so that a crowd is eight batches rather than one per agent.
        /// </summary>
        private void DrawSpeed(Vector3 position, in AgentMove move)
        {
            float fraction = move.MaxSpeed > 0f ? math.saturate(math.length(move.Velocity) / move.MaxSpeed) : 0f;
            fraction = GizmoBatch.Step(fraction, 8);

            Color colour = fraction < 0.5f
                ? Color.Lerp(Color.red, Color.yellow, fraction * 2f)
                : Color.Lerp(Color.yellow, Color.green, (fraction - 0.5f) * 2f);

            colour.a = 0.55f;
            _batch.FillDisc(colour, position, math.max(move.Radius * 0.6f, 0.1f));
        }

        /// <summary>A velocity, drawn a cell long at full speed so the length reads as a fraction of it.</summary>
        private void DrawRay(Color colour, float2 from, float2 velocity, float maxSpeed)
        {
            if (maxSpeed <= 0f || math.lengthsq(velocity) < math.EPSILON)
            {
                return;
            }

            _batch.Line(colour, SimToWorld.Position(from), SimToWorld.Position(from + velocity / maxSpeed));
        }

        private void DrawGoal(in AgentMove move, in PathFollow path)
        {
            Vector3 goal = SimToWorld.Position(GridCoords.CellCenter(path.GoalCell));
            var green = new Color(0.2f, 1f, 0.4f, 0.6f);

            _batch.WireRect(green, goal, new Vector2(0.6f, 0.6f));
            _batch.Line(green, SimToWorld.Position(move.Position), goal);

            // A waypoint that is not the goal is a gate on the way there, and worth telling apart: it is the
            // one case where the agent is deliberately walking at something other than where it is going.
            if (path.WaypointCell.Equals(path.GoalCell))
            {
                return;
            }

            _batch.WireRect(new Color(1f, 0.6f, 0.1f, 0.6f),
                            SimToWorld.Position(GridCoords.CellCenter(path.WaypointCell)),
                            new Vector2(0.4f, 0.4f));
        }

        /// <summary>
        /// An agent waiting its turn, drawn as the ring it has been told not to cross.
        ///
        /// The ring is the readable part. A queue is a set of distances from one cell (see
        /// <c>ArrivalQueueSystem</c>), so concentric rings around a busy door *are* the queue - and an agent
        /// sitting on its own ring is waiting correctly, while one drifting inside somebody else's is the bug.
        /// </summary>
        private void DrawHold(in AgentMove move, in PathFollow path)
        {
            Vector3 goal = SimToWorld.Position(GridCoords.CellCenter(path.GoalCell));
            var amber = new Color(1f, 0.75f, 0.2f, 0.7f);

            _batch.WireCircle(amber, goal, path.HoldDistance);
            _batch.Line(amber, SimToWorld.Position(move.Position), goal);
        }

        /// <summary>
        /// An agent in a doorway: a box on the entrance cell that shrinks as it goes through, green on the way
        /// in and blue on the way out. Two boxes on one cell would be the one-agent-at-a-time rule broken.
        /// </summary>
        private void DrawDoorUse(EntityManager entities, Entity agent, in AgentMove move)
        {
            if (!entities.HasComponent<DoorUse>(agent) || !entities.IsComponentEnabled<DoorUse>(agent))
            {
                return;
            }

            DoorUse door = entities.GetComponentData<DoorUse>(agent);

            Color colour = door.Kind == DoorUseKind.Enter
                ? new Color(0.3f, 1f, 0.5f, 0.8f)
                : new Color(0.4f, 0.7f, 1f, 0.8f);

            float size = math.lerp(0.9f, 0.15f, door.Inside);
            Vector3 cell = SimToWorld.Position(GridCoords.CellCenter(door.Cell));

            _batch.WireRect(colour, cell, new Vector2(size, size));
            _batch.Line(colour, SimToWorld.Position(move.Position), cell);
        }

        /// <summary>
        /// The dots are drawn as tiny crosses in the line batch rather than as filled discs. At a twentieth of a
        /// cell they read the same, and a disc is nine vertices to rebuild every frame: forty dots on eight
        /// hundred agents made the trails alone 23 ms of every frame.
        /// </summary>
        private void DrawTrail(Trail trail)
        {
            const float dotSize = 0.05f;
            Vector3 previous = default;

            for (int i = 0; i < trail.Count; i++)
            {
                // Oldest dots fade out, so the direction of travel is readable from a still frame. In four
                // steps of fade, so that every trail on the map shares four batches.
                float age = trail.Count > 1 ? GizmoBatch.Step(i / (float)(trail.Count - 1), 4) : 1f;
                var colour = new Color(0.6f, 0.8f, 1f, 0.15f + 0.55f * age);

                Vector3 dot = SimToWorld.Position(trail[i]);
                _batch.Line(colour, dot + new Vector3(-dotSize, -dotSize, 0f), dot + new Vector3(dotSize, dotSize, 0f));
                _batch.Line(colour, dot + new Vector3(-dotSize, dotSize, 0f), dot + new Vector3(dotSize, -dotSize, 0f));

                if (i > 0)
                {
                    _batch.Line(colour, previous, dot);
                }

                previous = dot;
            }
        }

        private bool InWindow(float2 simPosition) =>
            math.all(math.abs(simPosition - SimToWorld.ToSim(transform.position)) <= (float)_windowRadius);

        private static bool TryGetWorld(out EntityManager entities)
        {
            entities = default;

            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                return false;
            }

            entities = world.EntityManager;
            return true;
        }

        /// <summary>
        /// The agents worth drawing. <see cref="AgentMove"/> is enableable and disabled on agents that are
        /// inside a building (§6), so the query leaves those out for free - which is right, they are not on
        /// the map to be drawn.
        /// </summary>
        private static NativeArray<Entity> QueryAgents(EntityManager entities)
        {
            using EntityQuery query = new EntityQueryBuilder(Allocator.Temp)
                                      .WithAll<AgentMove>()
                                      .Build(entities);

            return query.ToEntityArray(Allocator.Temp);
        }

        /// <summary>A fixed-length ring of the last positions sampled, oldest readable first.</summary>
        private sealed class Trail
        {
            private readonly float2[] _points;
            private int _next;

            public Trail(int capacity) => _points = new float2[capacity];

            public int Capacity => _points.Length;

            public int Count { get; private set; }

            public float2 this[int index] =>
                _points[(_next - Count + index + _points.Length) % _points.Length];

            public void Add(float2 point)
            {
                _points[_next] = point;
                _next = (_next + 1) % _points.Length;
                Count = math.min(Count + 1, _points.Length);
            }
        }
    }
}
