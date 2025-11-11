using HCore.Systems;
using Objects.GenericModules;
using Objects.GenericSystems;
using UnityEngine;

namespace Objects
{
    public class ObjectInitializer  : MonoBehaviour, ISystem
    {
        void IInitializable.Initialize(ISystemManager systems)
        {
            Init(systems);
        }

        void IInitializable.Deinitialize(){}

        async Awaitable Init(ISystemManager systems)
        {
            var objectsSystem = systems.Get<ObjectsSystem>();
            foreach (MapObject obj in FindObjectsByType<MapObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                obj.Init(systems);
                objectsSystem.RegisterObject(obj);
                while (!Input.GetKeyDown(KeyCode.Space))
                {
                    await Awaitable.NextFrameAsync();
                }
                await Awaitable.NextFrameAsync();
            }
        }
    }
}