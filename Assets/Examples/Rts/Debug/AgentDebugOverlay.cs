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

        /// <summary>
        /// Sampled in <c>Update</c> and only read in <c>OnDrawGizmos</c>: gizmos are drawn once per camera and
        /// not at all when the view is hidden, so sampling there would space the dots by draw rate instead of
        /// by time and destroy the one thing the trail is for.
        /// </summary>
        private readonly Dictionary<Entity, Trail> _trails = new();

        private readonly HashSet<Entity> _sampled = new();

        private readonly List<Entity> _forgotten = new();

        private float _nextSample;

        private void OnDisable()
        {
            _trails.Clear();
            _nextSample = 0f;
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

            foreach (Entity agent in agents)
            {
                AgentMove move = entities.GetComponentData<AgentMove>(agent);
                if (!InWindow(move.Position))
                {
                    continue;
                }

                if (!_trails.TryGetValue(agent, out Trail trail) || trail.Capacity != _trailLength)
                {
                    trail = new Trail(_trailLength);
                    _trails[agent] = trail;
                }

                trail.Add(move.Position);
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
                    Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
                    Gizmos.DrawWireSphere(position, move.Radius);
                }

                if (_drawIntent && math.distancesq(move.PrefVelocity, move.Velocity) > 0.01f)
                {
                    Gizmos.color = Color.cyan;
                    DrawRay(move.Position, move.PrefVelocity, move.MaxSpeed);
                }

                if (_drawVelocity)
                {
                    Gizmos.color = Color.white;
                    DrawRay(move.Position, move.Velocity, move.MaxSpeed);
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
        }

        /// <summary>
        /// A disc whose colour is the speed as a fraction of the agent's maximum: red stopped, yellow halfway,
        /// green flat out. A crowd that strobes red-green in step is a crowd braking for something.
        /// </summary>
        private static void DrawSpeed(Vector3 position, in AgentMove move)
        {
            float fraction = move.MaxSpeed > 0f ? math.saturate(math.length(move.Velocity) / move.MaxSpeed) : 0f;

            Color colour = fraction < 0.5f
                ? Color.Lerp(Color.red, Color.yellow, fraction * 2f)
                : Color.Lerp(Color.yellow, Color.green, (fraction - 0.5f) * 2f);

            colour.a = 0.55f;
            Gizmos.color = colour;
            Gizmos.DrawSphere(position, math.max(move.Radius * 0.6f, 0.1f));
        }

        /// <summary>A velocity, drawn a cell long at full speed so the length reads as a fraction of it.</summary>
        private static void DrawRay(float2 from, float2 velocity, float maxSpeed)
        {
            if (maxSpeed <= 0f || math.lengthsq(velocity) < math.EPSILON)
            {
                return;
            }

            Gizmos.DrawRay(SimToWorld.Position(from), SimToWorld.Direction(velocity / maxSpeed));
        }

        private static void DrawGoal(in AgentMove move, in PathFollow path)
        {
            Vector3 goal = SimToWorld.Position(GridCoords.CellCenter(path.GoalCell));

            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.6f);
            Gizmos.DrawWireCube(goal, SimToWorld.Direction(new float2(0.6f, 0.6f)));
            Gizmos.DrawLine(SimToWorld.Position(move.Position), goal);

            // A waypoint that is not the goal is a gate on the way there, and worth telling apart: it is the
            // one case where the agent is deliberately walking at something other than where it is going.
            if (path.WaypointCell.Equals(path.GoalCell))
            {
                return;
            }

            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.6f);
            Gizmos.DrawWireCube(
                SimToWorld.Position(GridCoords.CellCenter(path.WaypointCell)),
                SimToWorld.Direction(new float2(0.4f, 0.4f))
            );
        }

        /// <summary>
        /// An agent waiting its turn, drawn as the ring it has been told not to cross.
        ///
        /// The ring is the readable part. A queue is a set of distances from one cell (see
        /// <c>ArrivalQueueSystem</c>), so concentric rings around a busy door *are* the queue - and an agent
        /// sitting on its own ring is waiting correctly, while one drifting inside somebody else's is the bug.
        /// </summary>
        private static void DrawHold(in AgentMove move, in PathFollow path)
        {
            Vector3 goal = SimToWorld.Position(GridCoords.CellCenter(path.GoalCell));

            Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.7f);
            Gizmos.DrawWireSphere(goal, path.HoldDistance);
            Gizmos.DrawLine(SimToWorld.Position(move.Position), goal);
        }

        /// <summary>
        /// An agent in a doorway: a box on the entrance cell that shrinks as it goes through, green on the way
        /// in and blue on the way out. Two boxes on one cell would be the one-agent-at-a-time rule broken.
        /// </summary>
        private static void DrawDoorUse(EntityManager entities, Entity agent, in AgentMove move)
        {
            if (!entities.HasComponent<DoorUse>(agent) || !entities.IsComponentEnabled<DoorUse>(agent))
            {
                return;
            }

            DoorUse door = entities.GetComponentData<DoorUse>(agent);

            Gizmos.color = door.Kind == DoorUseKind.Enter
                ? new Color(0.3f, 1f, 0.5f, 0.8f)
                : new Color(0.4f, 0.7f, 1f, 0.8f);

            float size = math.lerp(0.9f, 0.15f, door.Inside);
            Vector3 cell = SimToWorld.Position(GridCoords.CellCenter(door.Cell));

            Gizmos.DrawWireCube(cell, SimToWorld.Direction(new float2(size, size)));
            Gizmos.DrawLine(SimToWorld.Position(move.Position), cell);
        }

        private void DrawTrail(Trail trail)
        {
            for (int i = 0; i < trail.Count; i++)
            {
                // Oldest dots fade out, so the direction of travel is readable from a still frame.
                float age = trail.Count > 1 ? i / (float)(trail.Count - 1) : 1f;
                Gizmos.color = new Color(0.6f, 0.8f, 1f, 0.15f + 0.55f * age);

                Vector3 dot = SimToWorld.Position(trail[i]);
                Gizmos.DrawSphere(dot, 0.05f);

                if (i > 0)
                {
                    Gizmos.DrawLine(SimToWorld.Position(trail[i - 1]), dot);
                }
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
