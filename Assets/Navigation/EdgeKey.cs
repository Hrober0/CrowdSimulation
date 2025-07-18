﻿using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HCore.Shapes;
using Unity.Mathematics;
using UnityEngine;

namespace Navigation
{
    public readonly struct EdgeKey : IEquatable<EdgeKey>, IOutline
    {
        public readonly float2 A;
        public readonly float2 B;

        public EdgeKey(float2 a, float2 b)
        {
            // Lexicographical sort: first by x, then by y
            if (a.x < b.x || (a.x == b.x && a.y < b.y))
            {
                A = a;
                B = b;
            }
            else
            {
                A = b;
                B = a;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(EdgeKey other) => A.Equals(other.A) && B.Equals(other.B);
        
        public override int GetHashCode() => A.GetHashCode() ^ B.GetHashCode(); // XOR for 

        public override string ToString() => $"({A.x}, {A.y})({B.x}, {B.y})";

        public IEnumerable<Vector2> GetBorderPoints()
        {
            yield return A;
            yield return B;
        }
    }
}