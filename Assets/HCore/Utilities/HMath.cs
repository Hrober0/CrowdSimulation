using System.Collections.Generic;
using System;
using System.Runtime.CompilerServices;
using HCore.Extensions;
using UnityEngine;

namespace HCore
{
    public static class HMath
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Map(float v, float fMin, float fMax, float tMin, float tMax) => (v - fMin) / (fMax - fMin) * (tMax - tMin) + tMin;
        public static float Map(float v, MinMax<float> f, MinMax<float> t) => Map(v, f.Min, f.Max, t.Min, t.Max);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Inc(ref float value, float inc, float maxValue)
        {
            value += inc;
            if (value > maxValue)
                value = maxValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Dec(ref float value, float dec, float minValue)
        {
            value -= dec;
            if (value < minValue)
                value = minValue;
        }

        /// <summary>
        /// Get value after coma of float
        /// </summary>
        public static float Fraction(float value)
        {
            return value - Mathf.Floor(value);
        }


        public static (Vector2 center, Vector2 size) CenterSizeFrom2V2(Vector2 v1, Vector2 v2)
        {
            var (min, max) = MinMaxFrom2V2(v1, v2);
            var size = max - min;
            return (min + size * 0.5f, size);
        }
        public static (Vector2 min, Vector2 max) MinMaxFrom2V2(Vector2 v1, Vector2 v2) => (Vector2.Min(v1, v2), Vector2.Max(v1, v2));
        public static (Vector2 min, Vector2 size) MinSizeFrom2V2(Vector2 v1, Vector2 v2)
        {
            var min = Vector2.Min(v1, v2);
            var max = Vector2.Max(v1, v2);
            return (min, max - min);
        }


        public static Vector2[] ArrangePoints(Vector2 min, Vector2 max, Vector2 space)
        {
            var size = max - min;
            var numOfPoints = (size / space).Floor() + Vector2Int.one;
            var offset = (size - Vector2.Scale(space, numOfPoints)) * 0.5f;
            var points = new Vector2[numOfPoints.x * numOfPoints.y];
            for (int y = 0; y < numOfPoints.y; y++)
            {
                var iy = y * numOfPoints.x;
                var py = min + offset + y * space.y * Vector2.up;
                for (int x = 0; x < numOfPoints.x; x++)
                {
                    points[iy + x] = py + space.x * x * Vector2.right;
                }
            }
            return points;
        } 


        /// <summary>
        /// It will return point even if ray is pointing in another direction, becouse ditance can be negative
        /// </summary>
        public static Vector3 GetPlaneCursorPosition(Ray ray, float y)
        {
            float a = Vector3.Dot(ray.direction, Vector3.up);
            if (Mathf.Approximately(a, 0.0f))
            {
                return Vector3.zero; // Ray is parallel to the plane
            }

            float distance = (y - ray.origin.y) / a;
            return ray.GetPoint(distance);
        }

        public static T Max<T>(IEnumerable<T> list) where T : IComparable<T>
        {
            T max = default;
            foreach (var item in list)
            {
                if (item.CompareTo(max) > 0)
                {
                    max = item;
                }
            }
            return max;
        }

        public static float[] ScaleLength(List<int> inList, int targetLength)
        {
            var outArray = new float[targetLength];
            if (inList.Count == 0)
            {
                Array.Fill(outArray, 0);
                return outArray;
            }

            float pointerIncrement = (float)inList.Count / outArray.Length;
            float pointer = 0;
            int indexEndIncrement = Mathf.Max(Mathf.FloorToInt(pointerIncrement), 1);
            for (int i = 0; i < outArray.Length; i++)
            {
                int start = Mathf.FloorToInt(pointer);
                int end = (i == outArray.Length - 1) ? inList.Count : start + indexEndIncrement;

                float sum = 0;
                for (int j = start; j < end; j++)
                {
                    sum += inList[j];
                }

                float average = sum / (end - start);
                outArray[i] = average;

                pointer += pointerIncrement;
            }

            return outArray;
        }
    }
}