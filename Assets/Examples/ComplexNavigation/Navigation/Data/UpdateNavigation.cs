using Unity.Entities;

namespace ComplexNavigation
{
    public struct UpdateNavigation : IComponentData, IEnableableComponent
    {
        public enum UpdateType : byte
        {
            Add = 0,
            Update = 1,
            Remove = 2,
        };
        
        public UpdateType Type;
    }
}