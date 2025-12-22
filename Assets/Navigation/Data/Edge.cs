using System.Collections.Generic;
using HCore.Shapes;
using Unity.Mathematics;
using UnityEngine;

namespace Navigation
{
    public readonly struct Edge : IOutline
    {
        public readonly float2 A;
        public readonly float2 B;

        public Edge(float2 a, float2 b)
        {
            A = a;
            B = b;
        }

        public float2 Center => (A + B) / 2f;

        public IEnumerable<Vector2> GetBorderPoints()
        {
            yield return A;
            yield return B;
        }

        public override string ToString() => $"({A.x}, {A.y})({B.x}, {B.y})";
    }
}