using GridNav;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Examples.Rts
{
    /// <summary>
    /// A crowd of agents, scattered over a box around this object and all walking to one goal. Enough to see
    /// the grid, the flow fields and the pooled views doing their jobs at a realistic head count.
    /// </summary>
    public class AgentSpawnerAuthoring : MonoBehaviour
    {
        [SerializeField, Min(0)] private int _count = 200;

        [SerializeField, Tooltip("Box the agents are scattered over, in cells, centred on this object.")]
        private Vector2 _areaSize = new(24f, 24f);

        [SerializeField, Tooltip("Where they all walk to. Falls back to this object's own cell.")]
        private Transform _goal;

        [SerializeField, Min(0f)] private float _maxSpeed = 3f;

        [SerializeField, Range(0.05f, 0.45f)]
        [Tooltip("Must stay below 0.45 of a cell, or agents clip the corners of blocked cells.")]
        private float _radius = 0.42f;

        [SerializeField, Min(0), Tooltip("Units carried per haul trip. 0 makes them incapable of hauling.")]
        private int _carryCapacity = 10;

        [SerializeField, Tooltip("Changing it re-scatters the crowd without moving anything.")]
        private uint _seed = 1;

        public float2 Center => SimToWorld.ToSim(transform.position);

        public int2 GoalCell => GridCoords.CellOf(SimToWorld.ToSim(_goal != null ? _goal.position : transform.position));

        private void OnDrawGizmosSelected()
        {
            float2 center = Center;
            var size = new float2(_areaSize.x, _areaSize.y);

            Gizmos.color = new Color(0.3f, 0.9f, 0.4f);
            Gizmos.DrawWireCube(SimToWorld.Position(center), SimToWorld.Direction(size));

            float2 goal = GridCoords.CellCenter(GoalCell);
            Gizmos.DrawLine(SimToWorld.Position(center), SimToWorld.Position(goal));
            Gizmos.DrawWireCube(SimToWorld.Position(goal), SimToWorld.Direction(new float2(1f, 1f)));
        }

        private class AgentSpawnerBaker : Baker<AgentSpawnerAuthoring>
        {
            public override void Bake(AgentSpawnerAuthoring authoring)
            {
                if (authoring._goal != null)
                {
                    // Another object's transform, so the bake has to be told to re-run when it moves.
                    DependsOn(authoring._goal);
                }

                Entity entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new AgentSpawn
                {
                    Count = authoring._count,
                    Center = authoring.Center,
                    Size = new float2(authoring._areaSize.x, authoring._areaSize.y),
                    GoalCell = authoring.GoalCell,
                    MaxSpeed = authoring._maxSpeed,
                    Radius = authoring._radius,
                    CarryCapacity = authoring._carryCapacity,

                    // Zero is the one seed Unity.Mathematics.Random rejects, and an inspector default of 0 is
                    // exactly what someone will leave it at.
                    Seed = math.max(authoring._seed, 1u),
                });
            }
        }
    }
}
