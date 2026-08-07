using System;
using GridNav;
using Rts;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Examples.Rts
{
    /// <summary>
    /// A building with an arbitrary footprint. Offsets are authored unrotated, around the object's own cell;
    /// leaving gaps or making a ring is fine, nothing downstream assumes a rectangle.
    /// </summary>
    public class BuildingAuthoring : MonoBehaviour
    {
        [Serializable]
        private struct Entrance
        {
            [Tooltip("The footprint cell the door is cut into, before rotation.")]
            public Vector2Int WallCell;

            [Tooltip("Which wall of that cell the door is on. Agents stand on the neighbour that side.")]
            public Direction Side;
        }

        [SerializeField, Tooltip("Footprint cells as offsets from this object's cell, before rotation.")]
        private Vector2Int[] _footprintOffsets =
        {
            new(0, 0), new(1, 0),
            new(0, 1), new(1, 1),
        };

        [SerializeField] private GridRotation _rotation;

        [SerializeField, Tooltip("Doors. A building with none can never be entered, which is fine for a wall.")]
        private Entrance[] _entrances =
        {
            new() { WallCell = new Vector2Int(0, 0), Side = Direction.South },
        };

        [SerializeField, Min(0)]
        [Tooltip("How many agents fit inside at once. 0 means the building cannot be entered.")]
        private int _interiorCapacity;

        [SerializeField, Tooltip("Agents with nothing to do come and rest here - a haulers' hut.")]
        private bool _idleShelter;

        public int2 OriginCell => GridCoords.CellOf(SimToWorld.ToSim(transform.position));

        private void OnDrawGizmos()
        {
            int2 origin = OriginCell;
            Vector3 cellSize = SimToWorld.Direction(new float2(1f, 1f));

            Gizmos.color = new Color(0.2f, 0.5f, 0.9f);
            foreach (Vector2Int offset in _footprintOffsets)
            {
                int2 cell = origin + RotationUtils.Rotate(new int2(offset.x, offset.y), _rotation);
                Gizmos.DrawWireCube(SimToWorld.Position(GridCoords.CellCenter(cell)), cellSize);
            }

            Gizmos.color = new Color(0.95f, 0.8f, 0.2f);
            foreach (Entrance entrance in _entrances)
            {
                int2 doorstep = Doorstep(origin, entrance, out int2 wall);
                Gizmos.DrawWireCube(SimToWorld.Position(GridCoords.CellCenter(doorstep)), cellSize * 0.8f);
                Gizmos.DrawLine(
                    SimToWorld.Position(GridCoords.CellCenter(wall)),
                    SimToWorld.Position(GridCoords.CellCenter(doorstep))
                );
            }
        }

        /// <summary>The cell an agent stands on to use this door, and the wall cell it belongs to.</summary>
        private int2 Doorstep(int2 origin, in Entrance entrance, out int2 wall)
        {
            wall = origin + RotationUtils.Rotate(new int2(entrance.WallCell.x, entrance.WallCell.y), _rotation);
            return wall + DirectionUtils.Offset(RotationUtils.Rotate(entrance.Side, _rotation));
        }

        private class BuildingBaker : Baker<BuildingAuthoring>
        {
            public override void Bake(BuildingAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new BuildingPlacement
                {
                    OriginCell = authoring.OriginCell,
                    Rotation = authoring._rotation,
                });

                DynamicBuffer<BuildingFootprintOffset> footprint = AddBuffer<BuildingFootprintOffset>(entity);
                foreach (Vector2Int offset in authoring._footprintOffsets)
                {
                    footprint.Add(new BuildingFootprintOffset { Offset = new int2(offset.x, offset.y) });
                }

                DynamicBuffer<BuildingEntranceOffset> entrances = AddBuffer<BuildingEntranceOffset>(entity);
                foreach (Entrance entrance in authoring._entrances)
                {
                    entrances.Add(new BuildingEntranceOffset
                    {
                        Offset = new int2(entrance.WallCell.x, entrance.WallCell.y),
                        Side = entrance.Side,
                    });
                }

                if (authoring._interiorCapacity > 0)
                {
                    AddComponent(entity, new Interior { Capacity = authoring._interiorCapacity });
                }

                // A shelter with no room is a building nobody can rest in, so the tag without a capacity
                // would be a silently dead setting rather than a half-working one.
                if (authoring._idleShelter && authoring._interiorCapacity > 0)
                {
                    AddComponent<IdleShelter>(entity);
                }
            }
        }
    }
}
