using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace CustomNativeCollections
{
    [NativeContainer]
    public struct NativeListHash<T> : IDisposable where T : unmanaged, IEquatable<T>, IComparable<T>
    {
        // Each unique list gets an id = index in Ranges
        private NativeParallelMultiHashMap<ulong, int> _keyToIds;     // key -> id (may be multiple per key)
        private NativeList<T> _pool;                                  // concatenated ints of all canonical lists
        private NativeList<int2> _ranges;                             // (start, length) per id

        public int Length => _pool.Length;
        
        public NativeListHash(int capacity, Allocator allocator, int expectedListSize = 2)
        {
            _keyToIds = new(capacity, allocator);
            _pool     = new(capacity * expectedListSize, allocator);
            _ranges   = new(capacity, allocator);
        }

        public void Dispose()
        {
            if (_keyToIds.IsCreated) _keyToIds.Dispose();
            if (_pool.IsCreated)     _pool.Dispose();
            if (_ranges.IsCreated)   _ranges.Dispose();
        }

        /// <summary>
        /// Add or find an id for the given list (order-independent).
        /// </summary>
        /// <returns>
        /// Returns the stable id (index into Ranges).
        /// </returns>>
        public int AddOrGetId(NativeArray<T> values)
        {
            NativeArray<T> sortedTemp = TempCopyAndSort(values);
            var key = GetKey(sortedTemp);
            
            if (_keyToIds.TryGetFirstValue(key, out var candidateId, out var it))
            {
                do
                {
                    int2 range = _ranges[candidateId];
                    if (range.y == values.Length) // quick length filter
                    {
                        if (SequenceEquals(sortedTemp, _pool.AsArray().GetSubArray(range.x, range.y)))
                        {
                            sortedTemp.Dispose();
                            return candidateId; // exact match found
                        }
                    }
                }
                while (_keyToIds.TryGetNextValue(out candidateId, ref it));
            }

            if (_keyToIds.Count() >= _keyToIds.Capacity)
            {
                _keyToIds.Capacity += 100;
            }
            
            int start = _pool.Length;
            _pool.AddRange(sortedTemp);
            int id = _ranges.Length;
            _ranges.Add(new int2(start, sortedTemp.Length));
            _keyToIds.Add(key, id);

            sortedTemp.Dispose();
            return id;
        }
        
        /// <summary>
        /// Check if an equivalent list already exists, without inserting.
        /// </summary>
        public bool TryGetId(NativeArray<T> values, out int id)
        {
            var sortedTemp = TempCopyAndSort(values);
            var key = GetKey(sortedTemp);

            if (_keyToIds.TryGetFirstValue(key, out var candidateId, out var it))
            {
                do
                {
                    int2 range = _ranges[candidateId];
                    if (range.y == values.Length &&
                        SequenceEquals(sortedTemp, _pool.AsArray().GetSubArray(range.x, range.y)))
                    {
                        sortedTemp.Dispose();
                        id = candidateId;
                        return true;
                    }
                }
                while (_keyToIds.TryGetNextValue(out candidateId, ref it));
                sortedTemp.Dispose();
            }

            id = -1;
            return false;
        }
        
        /// <summary>
        /// Returns a copy of the elements for the given id.
        /// </summary>
        public NativeArray<T> GetElements(int id, Allocator allocator)
        {
            if ((uint)id >= (uint)_ranges.Length)
                throw new ArgumentOutOfRangeException(nameof(id));

            int2 range = _ranges[id];
            var result = new NativeArray<T>(range.y, allocator);
            NativeArray<T>.Copy(_pool.AsArray(), range.x, result, 0, range.y);
            return result;
        }
        
        [BurstCompile]
        private static ulong GetKey(NativeArray<T> values)
        {
            ulong hash = 1469598103934665603UL; // FNV offset
            ulong len = (ulong)values.Length;

            for (int i = 0; i < values.Length; i++)
            {
                ulong h = (ulong)values[i].GetHashCode();
                hash ^= (h + 0x9E3779B97F4A7C15UL + (hash << 6) + (hash >> 2));
            }

            hash ^= len * 0xBF58476D1CE4E5B9UL;
            return hash;
        }
        
        [BurstCompile]
        private static NativeArray<T> TempCopyAndSort(NativeArray<T> src)
        {
            var dst = new NativeArray<T>(src.Length, Allocator.Temp);
            dst.CopyFrom(src);
            NativeSort.QuickSort(dst, 0, dst.Length - 1);
            return dst;
        }
        
        [BurstCompile]
        private static bool SequenceEquals(NativeArray<T> a, NativeArray<T> b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (!a[i].Equals(b[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}