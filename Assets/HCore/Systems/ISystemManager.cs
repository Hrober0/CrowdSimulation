namespace HCore.Systems
{
    public interface ISystemManager
    {
        T Get<T>() where T : ISystem;
    }
}

