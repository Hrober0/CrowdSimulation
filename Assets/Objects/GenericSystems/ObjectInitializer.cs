using HCore.Systems;
using Objects.GenericModules;
using UnityEngine;

namespace Objects.GenericSystems
{
    public class ObjectInitializer  : MonoBehaviour, ISystem
    {
        void IInitializable.Initialize(ISystemManager systems)
        {
            var objectsSystem = systems.Get<ObjectsSystem>();
            foreach (MapObject obj in FindObjectsByType<MapObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                objectsSystem.RegisterObject(obj);
            }
        }

        void IInitializable.Deinitialize(){}
    }
}