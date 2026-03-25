using Unity.Entities;

namespace Examples.Storage
{
    public struct ConnectionEdit
    {
        public Entity       FromEntity;
        public Entity       ToEntity;
        public ResourceType Resource;
        public byte         Priority;
        public bool         Active;
    }
}