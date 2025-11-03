using Unity.Mathematics;

namespace Navigation
{
    public struct SamplePathSeeker : IPathSeeker<SamplePathSeeker, IdAttribute>, IPlaceSeeker<SamplePathSeeker, IdAttribute>
    {
        public readonly float CalculateCost(in IdAttribute attribute, float2 from, float2 to) => attribute.Entries > 0 ? float.MaxValue : math.distance(from, to);
        public readonly bool IsValid(in IdAttribute attribute) => attribute.Entries == 0;
    }
}