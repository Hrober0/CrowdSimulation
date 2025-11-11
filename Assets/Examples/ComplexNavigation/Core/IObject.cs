using HCore.Shapes;

namespace Objects
{
    public interface IObject : IBEntity
    {
        public T GetModule<T>() where T : IMComponent;
        public bool TryGetModule<T>(out T module) where T : IMComponent;
    }
}