using Unity.Entities;
using UnityEngine;

namespace Examples.Storage
{
    /// <summary>
    /// Place this on a scene GameObject alongside the SubScene or bootstrap.
    /// Assign the Storage prefab in the Inspector.
    /// The baker converts it to a <see cref="StoragePrefabsSingleton"/> entity
    /// that can be queried at runtime to instantiate new storages.
    /// </summary>
    public class StoragePrefabsAuthoring : MonoBehaviour
    {
        public GameObject StoragePrefab;

        public class Baker : Baker<StoragePrefabsAuthoring>
        {
            public override void Bake(StoragePrefabsAuthoring a)
            {
                var e = GetEntity(TransformUsageFlags.None);
                AddComponent(e, new StoragePrefabsSingleton
                {
                    Storage = GetEntity(a.StoragePrefab, TransformUsageFlags.None),
                });
            }
        }
    }

    public struct StoragePrefabsSingleton : IComponentData
    {
        public Entity Storage;
    }
}
