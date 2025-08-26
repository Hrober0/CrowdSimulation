using System;
using System.Collections.Generic;
using HCore.Shapes;
using Unity.Mathematics;
using UnityEngine;
using EdgeId = Navigation.NavNode.EdgeId;

namespace Navigation
{
    public struct NavNode
    {
        public enum EdgeId
        {
            AB,
            BC,
            CA,
        }

        public const int NULL_INDEX = -1;
    }

    public struct NavNode<T> : IOutline where T : unmanaged, INodeAttributes<T>
    {
        public static readonly NavNode<T> Empty = new();

        public readonly float2 CornerA;
        public readonly float2 CornerB;
        public readonly float2 CornerC;

        public int ConnectionAB;
        public int ConnectionBC;
        public int ConnectionCA;

        public readonly float2 Center;

        public readonly T Attributes;
        private readonly bool _wasSet;

        public NavNode(float2 cornerA, float2 cornerB, float2 cornerC, int connectionAB, int connectionBC, int connectionCA, T attributes)
        {
            CornerA = cornerA;
            CornerB = cornerB;
            CornerC = cornerC;

            ConnectionAB = connectionAB;
            ConnectionBC = connectionBC;
            ConnectionCA = connectionCA;
            Attributes = attributes;

            Center = Triangle.Center(cornerA, cornerB, cornerC);

            _wasSet = true;
        }

        public bool IsEmpty => !_wasSet;

        public Triangle Triangle => new(CornerA, CornerB, CornerC);

        public IEnumerable<Vector2> GetBorderPoints()
        {
            yield return CornerA;
            yield return CornerB;
            yield return CornerC;
        }

        public Edge GetEdge(EdgeId id) => id switch
        {
            EdgeId.AB => new(CornerA, CornerB),
            EdgeId.BC => new(CornerB, CornerC),
            EdgeId.CA => new(CornerC, CornerA),
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, null)
        };

        public int GetConnectionIndex(EdgeId id) => id switch
        {
            EdgeId.AB => ConnectionAB,
            EdgeId.BC => ConnectionBC,
            EdgeId.CA => ConnectionCA,
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, null)
        };

        public override string ToString() =>
            $"Node({CornerA}, {CornerB}, {CornerC}) connectionsAB: {ConnectionAB}, connectionBC: {ConnectionBC}, connectionCA: {ConnectionCA}";
    }
}