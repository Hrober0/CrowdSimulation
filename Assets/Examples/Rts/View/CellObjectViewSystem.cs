using System.Collections.Generic;
using GridNav;
using Rts;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Examples.Rts
{
    /// <summary>
    /// Gives every world object - a tree, a rock - a GameObject, once (design §10, §5).
    ///
    /// The same shape as <see cref="BuildingViewSystem"/>, and for the same reason: objects do not move, so
    /// there is nothing to sync per frame and no pool worth having. One instance when the object appears, and
    /// it is destroyed when the object is chopped.
    ///
    /// Until this existed a tree was 255 of cost and nothing else - invisible unless the grid overlay was on,
    /// where it showed as a red square with no explanation. A blocked cell the player cannot see is a bug they
    /// cannot report: the pathfinder is behaving perfectly and the map appears empty.
    /// </summary>
    [UpdateInGroup(typeof(RtsViewGroup))]
    public partial class CellObjectViewSystem : SystemBase
    {
        private readonly Dictionary<Entity, GameObject> _views = new();

        private GameObject _prefab;
        private Transform _root;

        /// <summary>Hands the system the prefab to build from. The scene owns it; the system borrows it.</summary>
        public void Bind(GameObject prefab, Transform root)
        {
            _prefab = prefab;
            _root = root;
        }

        public void Unbind()
        {
            foreach (GameObject view in _views.Values)
            {
                Destroy(view);
            }

            _views.Clear();
            _prefab = null;
            _root = null;
        }

        protected override void OnUpdate()
        {
            if (_prefab == null)
            {
                return;
            }

            var commands = new EntityCommandBuffer(Allocator.Temp);

            foreach ((RefRO<CellObject> cellObject, Entity entity)
                     in SystemAPI.Query<RefRO<CellObject>>()
                                 .WithNone<CellObjectViewMade>()
                                 .WithEntityAccess())
            {
                Create(entity, cellObject.ValueRO);
                commands.AddComponent<CellObjectViewMade>(entity);
            }

            // Chopped: the CellObject is gone and the cleanup tag is what is left to find the view by. The
            // registration system holds a cleanup component of its own on the same entity and drops it in the
            // same frame, so the entity is gone for good once both have had their turn.
            foreach (Entity gone in SystemAPI.QueryBuilder()
                                             .WithAll<CellObjectViewMade>()
                                             .WithNone<CellObject>()
                                             .Build()
                                             .ToEntityArray(Allocator.Temp))
            {
                Remove(gone);
                commands.RemoveComponent<CellObjectViewMade>(gone);
            }

            commands.Playback(EntityManager);
            commands.Dispose();
        }

        protected override void OnDestroy() => Unbind();

        private void Create(Entity entity, in CellObject cellObject)
        {
            float size = WorldObjectCatalog.Size(cellObject.Kind);

            GameObject view = Object.Instantiate(_prefab, _root);
            view.name = $"{cellObject.Kind} {entity.Index}";
            view.transform.position = SimToWorld.Position(GridCoords.CellCenter(cellObject.Cell), depth: 0f);
            view.transform.localScale = SimToWorld.Direction(new float2(size, size)) + Vector3.forward;

            Tint(view, WorldObjectCatalog.Colour(cellObject.Kind));
            _views[entity] = view;
        }

        private void Remove(Entity entity)
        {
            if (_views.Remove(entity, out GameObject view) && view != null)
            {
                Destroy(view);
            }
        }

        private static void Tint(GameObject view, Color colour)
        {
            if (view.TryGetComponent(out SpriteRenderer sprite))
            {
                sprite.color = colour;
                return;
            }

            if (view.TryGetComponent(out Renderer renderer))
            {
                // A property block rather than renderer.material, which would leak a material instance per
                // tree and defeat batching for no gain.
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", colour);
                block.SetColor("_Color", colour);
                renderer.SetPropertyBlock(block);
            }
        }

        private static void Destroy(GameObject view)
        {
            if (Application.isPlaying)
            {
                Object.Destroy(view);
            }
            else
            {
                Object.DestroyImmediate(view);
            }
        }
    }

    /// <summary>
    /// Marks a world object that has been given a view. Cleanup data, so it outlives the entity: a chopped
    /// tree leaves this behind for one frame, which is how the system learns there is a GameObject to destroy.
    /// </summary>
    public struct CellObjectViewMade : ICleanupComponentData
    {
    }
}
