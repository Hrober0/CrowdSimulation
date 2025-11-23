using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;

namespace CustomNativeCollections
{
    public static class SpatialHashMethods
    {
        public const int NODE_END = -1;
        public const int NODE_EMPTY = -2;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 CellOf(float2 p, float invCell)
            => new int2((int)math.floor(p.x * invCell), (int)math.floor(p.y * invCell));
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 CellMin(int2 p, float invCell) => new float2(p.x, p.y) / invCell; 

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Hash(int2 cell)
        {
            unchecked
            {
                return (cell.y << 16) + cell.x;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (int2 cMin, int2 cMax) ToMinMax(float2 a, float2 b, float invCell)
        {
            var cA = CellOf(a, invCell);
            var cB = CellOf(b, invCell);
            int2 cMin = math.min(cA, cB);
            int2 cMax = math.max(cA, cB);
            return (cMin, cMax);
        }

        /// <summary>
        /// Iterate through objects at given area, values can be duplicated.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ForEachInAABB<TValue, TProcessor>(
            float2 min,
            float2 max,
            float invCell,
            in NativeHashMap<int, int> cellHeads,
            in NativeArray<Node<TValue>> nodes,
            ref TProcessor processor)
            where TValue : unmanaged, IEquatable<TValue>
            where TProcessor : struct, ISpatialQueryProcessor<TValue>
        {
            (int2 cMin, int2 cMax) = ToMinMax(min, max, invCell);
            for (int y = cMin.y; y <= cMax.y; y++)
            for (int x = cMin.x; x <= cMax.x; x++)
            {
                int key = Hash(new int2(x, y));

                if (!cellHeads.TryGetValue(key, out int head) || head < 0)
                {
                    continue;
                }

                int current = head;
                while (current >= 0)
                {
                    Node<TValue> node = nodes[current];
                    processor.Process(node.Value);
                    current = node.Next;
                }
            }
        }
    }
}