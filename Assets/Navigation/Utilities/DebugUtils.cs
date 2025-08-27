using System;
using HCore.Shapes;
using Unity.Collections;
using UnityEngine;

namespace Navigation
{
    public static class DebugUtils
    {
        public static void Draw<TKey, TValue>(this NativeParallelMultiHashMap<TKey, TValue> map, Color color, float? duration = null)
            where TValue : unmanaged, IOutline where TKey : unmanaged, IEquatable<TKey>
        {
            using NativeArray<TKey> keys = map.GetKeyArray(Allocator.Temp);
            foreach (var key in keys)
            {
                foreach (var value in map.GetValuesForKey(key))
                {
                    value.DrawBorder(color, duration);
                }
            }
        }
    }
}