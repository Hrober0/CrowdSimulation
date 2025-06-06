using System;
using UnityEngine;

namespace HCore
{
    [Serializable]
    public struct MinMax<T>
    {
        [SerializeField]
        private T min;
        [SerializeField]
        private T max;

        public T Min { readonly get => min; set => min = value; }
        public T Max { readonly get => max; set => max = value; }

        public MinMax(T min, T max)
        {
            this.min = min;
            this.max = max;
        }

        public readonly override string ToString() => $"{min}:{max}";
    }

    public static class MinMaxExtensions
    {
        public static float Clamp(this MinMax<float> minMax, float value) => Mathf.Clamp(value, minMax.Min, minMax.Max);
        public static int Clamp(this MinMax<int> minMax, int value) => Mathf.Clamp(value, minMax.Min, minMax.Max);
        public static Vector3 Clamp(this MinMax<Vector3> minMax, Vector3 value) => new(
                Mathf.Clamp(value.x, minMax.Min.x, minMax.Max.x),
                Mathf.Clamp(value.y, minMax.Min.y, minMax.Max.y),
                Mathf.Clamp(value.z, minMax.Min.z, minMax.Max.z));
        public static Vector2 Clamp(this MinMax<Vector2> minMax, Vector2 value) => new(
                Mathf.Clamp(value.x, minMax.Min.x, minMax.Max.x),
                Mathf.Clamp(value.y, minMax.Min.y, minMax.Max.y));

        public static bool IsBetween(this MinMax<int> minMax, int value) => value >= minMax.Min && value <= minMax.Max;
        public static bool IsBetween(this MinMax<float> minMax, float value) => value >= minMax.Min && value <= minMax.Max;

        public static float Random(this MinMax<float> minMax) => UnityEngine.Random.Range(minMax.Min, minMax.Max);
        public static int Random(this MinMax<int> minMax) => UnityEngine.Random.Range(minMax.Min, minMax.Max);
        public static Vector2 Random(this MinMax<Vector2> minMax) => new(
            UnityEngine.Random.Range(minMax.Min.x, minMax.Max.x),
            UnityEngine.Random.Range(minMax.Min.y, minMax.Max.y)
            );
    }
}