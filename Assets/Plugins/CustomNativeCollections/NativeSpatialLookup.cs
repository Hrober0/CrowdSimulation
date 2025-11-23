using System;
using Unity.Collections;
using Unity.Mathematics;

namespace CustomNativeCollections
{
    public struct NativeSpatialLookup<T> : IDisposable where T : unmanaged
    {
        public NativeParallelMultiHashMap<int, int> Lookup;
        public NativeList<T> Values;

        public readonly float InvCellSize;

        public bool IsCreated => Lookup.IsCreated;

        public NativeSpatialLookup(int capacity, float cellSize, Allocator allocator)
        {
            Lookup = new(capacity, allocator);
            Values = new(capacity, allocator);
            InvCellSize = 1f / cellSize;
        }

        public void Dispose()
        {
            Lookup.Dispose();
            Values.Dispose();
        }

        public void Clear()
        {
            Lookup.Clear();
            Values.Clear();
        }

        public readonly void QueryCell(int2 cell, NativeList<T> results)
        {
            int key = SpatialHashMethods.Hash(cell);
            foreach (var index in Lookup.GetValuesForKey(key))
            {
                results.Add(Values[index]);
            }
        }

        public readonly void QueryPoint(float2 p, NativeList<T> results)
        {
            QueryCell(SpatialHashMethods.CellOf(p, InvCellSize), results);
        }

        public readonly void QueryAABB(float2 min, float2 max, NativeList<T> results)
        {
            (int2 cMin, int2 cMax) = SpatialHashMethods.ToMinMax(min, max, InvCellSize);
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
            (int2 cMin, int2 cMax) = SpatialHashMethods.ToMinMax(min, max, InvCellSize);
            for (int y = cMin.y; y <= cMax.y; y++)
            for (int x = cMin.x; x <= cMax.x; x++)
            {
                int key = SpatialHashMethods.Hash(new int2(x, y));
                foreach (var index in Lookup.GetValuesForKey(key))
                {
                    processor.Process(Values[index]);
                }
            }
        }
    }

    // public interface ILookupBuildProcessor<T>
    // {
    //     LookupMinMax GetMinMax(T item);
    // }
    //
    // public struct LookupMinMax
    // {
    //     public float2 Min;
    //     public float2 Max;
    // }
    //
    // [BurstCompile]
    // public struct BuildLookupJob<T> : IJobParallelFor where T : unmanaged
    // {
    //     [ReadOnly] public ILookupBuildProcessor<T> Processor;
    //     [ReadOnly] public NativeList<T> Vertices;
    //     [ReadOnly] public float InvCell;
    //
    //     [WriteOnly] public NativeParallelMultiHashMap<int, int>.ParallelWriter Lookup;
    //
    //     public void Execute(int itemIndex)
    //     {
    //         var minMax = Processor.GetMinMax(Vertices[itemIndex]);
    //         var min = SpatialHashMethod.CellOf(minMax.Min, InvCell);
    //         var max = SpatialHashMethod.CellOf(minMax.Max, InvCell);
    //
    //         for (int y = min.y; y <= max.y; y++)
    //         {
    //             for (int x = min.x; x <= max.x; x++)
    //             {
    //                 Lookup.Add(SpatialHashMethod.Hash(new(x, y)), itemIndex);
    //             }
    //         }
    //     }
    // }
}