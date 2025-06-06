namespace HCore.Systems
{
    public interface IInitializable
    {
        void Initialize(ISystemManager systems);
        void Deinitialize();
    }
}