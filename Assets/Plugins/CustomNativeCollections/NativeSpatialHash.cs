using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace CustomNativeCollections
{
    public struct Node<T>
    {
        public T Value;
        public int Next; // index of next node in chain, or NODE_END, or NODE_EMPTY
    }

    [BurstCompile]
    public struct NativeSpatialHash<T> : IDisposable
        where T : unmanaged, IEquatable<T>
    {
        public NativeHashMap<int, int> CellHeads;
        public NativeList<Node<T>> Nodes;
        private NativeList<int> _freeStack;

        private readonly float _invCell;

        public bool IsCreated => CellHeads.IsCreated && Nodes.IsCreated && _freeStack.IsCreated;

        public int Count { get; private set; } // number of active nodes (not equal to _nodes.Length)
        public int Capacity => Nodes.Capacity;

        public float CellSize => 1f / _invCell;
        public float InvCellSize => _invCell;

        public NativeSpatialHash(int capacity, float cellSize, Allocator allocator)
        {
            cellSize = math.max(1e-6f, cellSize);
            _invCell = 1f / cellSize;

            // we don't know number of unique keys; capacity/2 is a heuristic
            int mapCap = math.max(1, capacity / 2);

            CellHeads = new(mapCap, allocator);
            Nodes = new(capacity, allocator);
            _freeStack = new(capacity, allocator);

            Count = 0;
        }

        public void Dispose()
        {
            if (CellHeads.IsCreated) CellHeads.Dispose();
            if (Nodes.IsCreated) Nodes.Dispose();
            if (_freeStack.IsCreated) _freeStack.Dispose();
        }

        public void Clear()
        {
            CellHeads.Clear();
            Nodes.Clear();
            _freeStack.Clear();
            Count = 0;
        }

        #region Helpers

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int AllocateNode()
        {
            int index;
            int freeCount = _freeStack.Length;
            if (freeCount > 0)
            {
                // pop from free stack
                freeCount--;
                index = _freeStack[freeCount];
                _freeStack.RemoveAt(freeCount);
            }
            else
            {
                index = Nodes.Length;
                Nodes.Add(new() { Value = default, Next = SpatialHashMethod.NODE_END });
            }

            Count++;
            return index;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddToCell(int cellKey, T value)
        {
            int index = AllocateNode();

            if (CellHeads.TryGetValue(cellKey, out int head))
            {
                CellHeads[cellKey] = index;
            }
            else
            {
                head = SpatialHashMethod.NODE_END;
                CellHeads.Add(cellKey, index);
            }

            Nodes[index] = new()
            {
                Value = value,
                Next = head
            };
        }

        /// <summary>
        /// Remove a single value from a given cell, if present.
        /// Returns true if removed.
        /// </summary>
        private bool RemoveFromCell(int cellKey, T value)
        {
            if (!CellHeads.TryGetValue(cellKey, out int head) || head < 0)
            {
                return false;
            }

            int current = head;
            int prev = -1;

            while (current >= 0)
            {
                ref Node<T> node = ref Nodes.ElementAt(current);
                if (node.Value.Equals(value))
                {
                    int next = node.Next;

                    // unlink from list
                    if (prev < 0)
                    {
                        // removing head
                        if (next >= 0)
                        {
                            CellHeads[cellKey] = next;
                        }
                        else
                        {
                            // no more nodes in this cell
                            CellHeads.Remove(cellKey);
                        }
                    }
                    else
                    {
                        Node<T> prevNode = Nodes[prev];
                        prevNode.Next = next;
                        Nodes[prev] = prevNode;
                    }

                    node.Next = SpatialHashMethod.NODE_EMPTY;
                    _freeStack.Add(current);
                    Count--;
                    return true;
                }

                prev = current;
                current = node.Next;
            }

            return false;
        }

        #endregion

        #region Mofifications

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddPoint(float2 p, T value)
        {
            int key = SpatialHashMethod.Hash(SpatialHashMethod.CellOf(p, _invCell));
            AddToCell(key, value);
        }

        public void AddAABB(float2 min, float2 max, T value)
        {
            (int2 cMin, int2 cMax) = SpatialHashMethod.ToMinMax(min, max, _invCell);

            for (int y = cMin.y; y <= cMax.y; y++)
            for (int x = cMin.x; x <= cMax.x; x++)
            {
                int key = SpatialHashMethod.Hash(new int2(x, y));
                AddToCell(key, value);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemovePoint(float2 p, T value)
        {
            int key = SpatialHashMethod.Hash(SpatialHashMethod.CellOf(p, _invCell));
            RemoveFromCell(key, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveAABB(float2 min, float2 max, T value)
        {
            (int2 cMin, int2 cMax) = SpatialHashMethod.ToMinMax(min, max, _invCell);
            for (int y = cMin.y; y <= cMax.y; y++)
            for (int x = cMin.x; x <= cMax.x; x++)
            {
                int key = SpatialHashMethod.Hash(new int2(x, y));
                RemoveFromCell(key, value);
            }
        }

        #endregion

        #region Query

        public readonly void QueryCell(int2 cell, NativeList<T> results)
        {
            int key = SpatialHashMethod.Hash(cell);
            if (!CellHeads.TryGetValue(key, out int head) || head < 0)
            {
                return;
            }

            int current = head;
            while (current >= 0)
            {
                Node<T> node = Nodes[current];
                results.Add(node.Value);
                current = node.Next;
            }
        }

        public readonly void QueryPoint(float2 p, NativeList<T> results)
        {
            QueryCell(SpatialHashMethod.CellOf(p, _invCell), results);
        }

        public readonly void QueryAABB(float2 min, float2 max, NativeList<T> results)
        {
            (int2 cMin, int2 cMax) = SpatialHashMethod.ToMinMax(min, max, _invCell);
            for (int y = cMin.y; y <= cMax.y; y++)
            for (int x = cMin.x; x <= cMax.x; x++)
            {
                QueryCell(new int2(x, y), results);
            }
        }

        /// <summary>
        /// Iterate through objects at given area, values can be duplicated.
        /// </summary>
        public readonly void ForEachInAABB<TProcessor>(float2 min, float2 max, ref TProcessor processor)
            where TProcessor : struct, ISpatialQueryProcessor<T>
        {
            (int2 cMin, int2 cMax) = SpatialHashMethod.ToMinMax(min, max, _invCell);
            for (int y = cMin.y; y <= cMax.y; y++)
            for (int x = cMin.x; x <= cMax.x; x++)
            {
                int key = SpatialHashMethod.Hash(new int2(x, y));

                if (!CellHeads.TryGetValue(key, out int head) || head < 0)
                {
                    continue;
                }

                int current = head;
                while (current >= 0)
                {
                    Node<T> node = Nodes[current];
                    processor.Process(node.Value);
                    current = node.Next;
                }
            }
        }

        #endregion
    }

    public static class SpatialHashMethod
    {
        public const int NODE_END = -1;
        public const int NODE_EMPTY = -2;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 CellOf(float2 p, float invCell)
            => new int2((int)math.floor(p.x * invCell), (int)math.floor(p.y * invCell));

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