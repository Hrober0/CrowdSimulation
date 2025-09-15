using System.Collections.Generic;
using Unity.Mathematics;

namespace Navigation
{
    public readonly struct PointDistanceComparer : IComparer<float2>
    {
        private readonly float2 _reference;

        public PointDistanceComparer(float2 reference)
        {
            _reference = reference;
        }

        public int Compare(float2 a, float2 b)
        {
            float da = math.lengthsq(a - _reference);
            float db = math.lengthsq(b - _reference);
            return da.CompareTo(db); // ascending order
        }
    }
}