using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;

namespace CustomNativeCollections
{
    public static class NativeSort
    {
        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void QuickSort<T>(NativeArray<T> a) where T : unmanaged, IComparable<T> => QuickSort(a, 0, a.Length - 1);
        
        [BurstCompile]
        public static void QuickSort<T>(NativeArray<T> a, int lo, int hi) where T : unmanaged, IComparable<T>
        {
            while (lo < hi)
            {
                int i = lo, j = hi;
                T pivot = a[(lo + hi) >> 1];

                while (i <= j)
                {
                    while (a[i].CompareTo(pivot) < 0) i++;
                    while (a[j].CompareTo(pivot) > 0) j--;
                    if (i <= j)
                    {
                        if (i != j)
                        {
                            (a[i], a[j]) = (a[j], a[i]);
                        }
                        i++; j--;
                    }
                }

                // recurse into smaller partition to keep depth small
                if (j - lo < hi - i)
                {
                    if (lo < j) QuickSort(a, lo, j);
                    lo = i;
                }
                else
                {
                    if (i < hi) QuickSort(a, i, hi);
                    hi = j;
                }
            }
        }
    }
}