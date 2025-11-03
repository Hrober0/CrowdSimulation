using Unity.Mathematics;

namespace Navigation
{
    public interface IPathSeeker<T, TAttribute>
        where T : unmanaged, IPathSeeker<T, TAttribute>
        where TAttribute : unmanaged
    {
        float CalculateCost(in TAttribute attribute, float2 from, float2 to);
    }
}