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
    /// Gives every building a GameObject, once (design §10).
    ///
    /// Buildings do not move, so there is nothing to sync per frame and no pool worth having: one instance is
    /// made when the building is placed and destroyed when it is demolished. That is the whole difference
    /// from the agent view - and the reason §10 says only agents need per-frame work.
    ///
    /// The view is sized and placed from <see cref="BuildingFootprintCell"/>, which is the resolved,
    /// already-rotated set of cells the building actually stands on. So an L-shape gets a box around its
    /// extent rather than anything having to understand its shape.
    /// </summary>
    [UpdateInGroup(typeof(RtsViewGroup))]
    public partial class BuildingViewSystem : SystemBase
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

            foreach ((RefRO<BuildingVisual> visual, DynamicBuffer<BuildingFootprintCell> footprint, Entity building)
                     in SystemAPI.Query<RefRO<BuildingVisual>, DynamicBuffer<BuildingFootprintCell>>()
                                 .WithNone<BuildingViewMade>()
                                 .WithEntityAccess())
            {
                if (footprint.IsEmpty)
                {
                    continue;
                }

                Create(building, visual.ValueRO, footprint);
                commands.AddComponent<BuildingViewMade>(building);
            }

            // Demolished: the placement and the visual are gone, the cleanup tag is what is left to find it by.
            foreach (Entity building in SystemAPI.QueryBuilder()
                                                 .WithAll<BuildingViewMade>()
                                                 .WithNone<BuildingVisual>()
                                                 .Build()
                                                 .ToEntityArray(Allocator.Temp))
            {
                Remove(building);
                commands.RemoveComponent<BuildingViewMade>(building);
            }

            commands.Playback(EntityManager);
            commands.Dispose();
        }

        protected override void OnDestroy() => Unbind();

        private void Create(Entity building, in BuildingVisual visual, in DynamicBuffer<BuildingFootprintCell> footprint)
        {
            int2 min = footprint[0].Cell;
            int2 max = min;
            foreach (BuildingFootprintCell cell in footprint)
            {
                min = math.min(min, cell.Cell);
                max = math.max(max, cell.Cell);
            }

            float2 centre = (GridCoords.CellMin(min) + GridCoords.CellMax(max)) * 0.5f;
            float2 size = GridCoords.CellMax(max) - GridCoords.CellMin(min);

            GameObject view = Object.Instantiate(_prefab, _root);
            view.name = $"Building {building.Index}";
            view.transform.position = SimToWorld.Position(centre, depth: 0f);
            view.transform.localScale = SimToWorld.Direction(size) + Vector3.forward;

            Tint(view, visual.Tint);
            _views[building] = view;
        }

        private void Remove(Entity building)
        {
            if (_views.Remove(building, out GameObject view) && view != null)
            {
                Destroy(view);
            }
        }

        private static void Tint(GameObject view, float4 tint)
        {
            var colour = new Color(tint.x, tint.y, tint.z, tint.w);

            if (view.TryGetComponent(out SpriteRenderer sprite))
            {
                sprite.color = colour;
                return;
            }

            if (view.TryGetComponent(out Renderer renderer))
            {
                // A property block rather than renderer.material, which would leak a material instance per
                // building and defeat batching for no gain.
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
}
