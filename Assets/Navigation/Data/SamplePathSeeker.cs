namespace Navigation
{
    public struct SamplePathSeeker : IPathSeeker<SamplePathSeeker, IdAttribute>
    {
        public float CalculateMultiplier(IdAttribute attribute)
        {
            return attribute.Entries > 0 ? 100000 : 1;
        }
    }
}