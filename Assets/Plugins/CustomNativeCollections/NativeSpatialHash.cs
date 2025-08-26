using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace CustomNativeCollections
{
    /// <summary>
    /// IEquatable is use only for remove
    /// </summary>
    [BurstCompile]
    public struct NativeSpatialHash<T> : System.IDisposable where T : unmanaged, System.IEquatable<T>
    {
        public NativeParallelMultiHashMap<int, T> Map;

        private readonly float _invCell;
        private readonly int _capacityAddition;

        public bool IsCreated => Map.IsCreated;
        public void Clear() => Map.Clear();
        public int Capacity => Map.Capacity;
        public int Count => Map.Count();

        public NativeSpatialHash(int capacity, float chunkSize, Allocator allocator, int capacityAddition = 100)
        {
            chunkSize = math.max(1e-6f, chunkSize);
            _invCell = 1f / chunkSize;
            _capacityAddition = capacityAddition;
            Map = new(math.max(1, capacity), allocator);
        }

        public void Dispose()
        {
            if (Map.IsCreated)
            {
                Map.Dispose();
            }
        }

        #region Modification

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddPoint(float2 p, T value)
        {
            EnsureCapacity(1);
            var key = Hash(CellOf(p, _invCell));
            Map.Add(key, value);
        }

        public void AddAABB(float2 min, float2 max, T value)
        {
            (int2 cMin, int2 cMax) = ToMinMax(min, max, _invCell);
            EnsureCapacity((cMax.x - cMin.x + 1) * (cMax.y - cMin.y + 1));
            for (int cy = cMin.y; cy <= cMax.y; cy++)
            for (int cx = cMin.x; cx <= cMax.x; cx++)
            {
                var key = Hash(new int2(cx, cy));
                Map.Add(key, value);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemovePoint(float2 p, T value)
        {
            int key = Hash(CellOf(p, _invCell));
            Map.Remove(key, value);
        }

        public void RemoveAABB(float2 min, float2 max, T value)
        {
            (int2 cMin, int2 cMax) = ToMinMax(min, max, _invCell);
            for (int cy = cMin.y; cy <= cMax.y; cy++)
            for (int cx = cMin.x; cx <= cMax.x; cx++)
            {
                Map.Remove(Hash(new int2(cx, cy)), value);
            }
        }

        #endregion

        #region Query

        public readonly void QueryPoint(float2 p, NativeList<T> results)
        {
            int2 cell = CellOf(p, _invCell);
            QueryCell(cell, results);
        }

        public readonly void QueryAABB(float2 min, float2 max, NativeList<T> results)
        {
            (int2 cMin, int2 cMax) = ToMinMax(min, max, _invCell);
            for (int cy = cMin.y; cy <= cMax.y; cy++)
            for (int cx = cMin.x; cx <= cMax.x; cx++)
            {
                QueryCell(new int2(cx, cy), results);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void QueryCell(int2 cell, NativeList<T> results)
        {
            int key = Hash(cell);
            NativeParallelMultiHashMapIterator<int> it;
            T value;
            if (Map.TryGetFirstValue(key, out value, out it))
            {
                do results.Add(value);
                while (Map.TryGetNextValue(out value, ref it));
            }
        }

        #endregion

        #region Helpers

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int2 CellOf(float2 p, float invCell) => new int2((int)math.floor(p.x * invCell), (int)math.floor(p.y * invCell));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Hash(int2 cell) => (int)math.hash(cell);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static (int2 cMin, int2 cMax) ToMinMax(float2 min, float2 max, float invCell)
        {
            int2 cMin = CellOf(min, invCell);
            int2 cMax = CellOf(max, invCell);
            if (cMax.x < cMin.x)
            {
                (cMin.x, cMax.x) = (cMax.x, cMin.x);
            }

            if (cMax.y < cMin.y)
            {
                (cMin.y, cMax.y) = (cMax.y, cMin.y);
            }

            return (cMin, cMax);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureCapacity(int additional)
        {
            if (Map.Count() + additional > Map.Capacity)
            {
                Map.Capacity = math.max(Map.Capacity + _capacityAddition, Map.Count() + additional);
            }
        }

        #endregion
    }
}