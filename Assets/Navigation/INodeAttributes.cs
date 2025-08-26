namespace Navigation
{
    public interface INodeAttributes<T> where T : unmanaged
    {
        T Empty();
        void Merge(T other);
    }
}