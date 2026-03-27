using Unity.Entities;

namespace Examples.Storage
{
    public struct ConnectionEdit
    {
        public Entity         StorageA;
        public Entity         StorageB;
        public ResourceType   Resource;
        public byte           Priority;
        public ConnectionMode Mode;
    }
}
