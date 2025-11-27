using Navigation;
using Unity.Entities;

namespace ComplexNavigation
{
    public struct PathBuffer : IBufferElementData
    {
        public PathPortal Portal;
    }
}