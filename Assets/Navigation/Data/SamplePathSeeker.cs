namespace Navigation
{
    public struct SamplePathSeeker : IPathSeeker<SamplePathSeeker, IdAttribute>, IPlaceSeeker<SamplePathSeeker, IdAttribute>
    {
        public readonly float CalculateCost(in IdAttribute attribute, float distance) => attribute.Entries > 0 ? float.MaxValue : distance;
        public readonly bool IsValid(in IdAttribute attribute) => attribute.Entries == 0;
    }
}