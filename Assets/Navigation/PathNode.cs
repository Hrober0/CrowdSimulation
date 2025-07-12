using System.Collections.Generic;
using HCore.Shapes;
using Unity.Mathematics;
using UnityEngine;

namespace Navigation
{
    public readonly struct PathNode : IOutline
    {
        public static readonly PathNode Empty = new PathNode();

        public readonly float2 CornerA;
        public readonly float2 CornerB;
        public readonly float2 CornerC;

        public readonly int ConnectionAB;
        public readonly float EdgeAB;

        public readonly int ConnectionAC;
        public readonly float EdgeAC;

        public readonly int ConnectionBC;
        public readonly float EdgeBC;

        public readonly float2 Center;
        
        public readonly int ConfigIndex;

        public PathNode(float2 cornerA, float2 cornerB, float2 cornerC, int connectionAB, int connectionAC, int connectionBC, int configIndex)
        {
            CornerA = cornerA;
            CornerB = cornerB;
            CornerC = cornerC;

            ConnectionAB = connectionAB;
            EdgeAB = math.length(cornerA - cornerB);

            ConnectionAC = connectionAC;
            EdgeAC = math.length(cornerA - cornerC);

            ConnectionBC = connectionBC;
            EdgeBC = math.length(cornerB - cornerC);

            Center = (cornerA + cornerB + cornerC) * 0.33333f;
            
            ConfigIndex = configIndex;
        }

        public bool IsEmpty => EdgeAB != 0;

        public Triangle Triangle => new(CornerA, CornerB, CornerC);
            
        public IEnumerable<Vector2> GetBorderPoints()
        {
            yield return CornerA;
            yield return CornerB;
            yield return CornerC;
        }

        public override string ToString() =>
            $"Node({CornerA}, {CornerB}, {CornerC}) connectionsAB: {ConnectionAB}, connectionAC: {ConnectionAC}, connectionBC: {ConnectionBC}";
    }
}