using System;
using Unity.Mathematics;

namespace Avoidance
{
    public struct ObstacleVertex : IEquatable<ObstacleVertex>
    {
        public int VertexIndex;
        public int ObjectId;

        public int Next;
        public int Previous;

        public float2 Direction;
        public float2 Point;
        public bool Convex;

        public readonly bool Equals(ObstacleVertex other) => Next == other.Next;

        public readonly override bool Equals(object obj) => obj is ObstacleVertex t && Equals(t);

        public readonly override int GetHashCode() => VertexIndex;

        public static bool operator ==(ObstacleVertex a, ObstacleVertex b) => a.Next == b.Next;

        public static bool operator !=(ObstacleVertex a, ObstacleVertex b) => a.Next != b.Next;
    }
}