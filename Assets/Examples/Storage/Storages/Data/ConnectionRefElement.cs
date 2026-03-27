using Unity.Entities;

namespace Examples.Storage
{
    /// <summary>
    /// Buffer element on every storage entity.
    /// Points to a connection entity that carries the full <see cref="StorageConnectionComponent"/>.
    /// Both ends of a connection hold a ref to the same entity.
    /// </summary>
    public struct ConnectionRefElement : IBufferElementData
    {
        public Entity Value; // connection entity
    }
}
