using Unity.Entities;
using UnityEngine;

namespace Examples.Rts
{
    /// <summary>
    /// The scene half of the building view (design §10): it owns the prefab and lends it to
    /// <see cref="BuildingViewSystem"/> for as long as it is enabled.
    ///
    /// Buildings are few and never move, so unlike <see cref="AgentViewSettings"/> there is no pool and
    /// nothing to prewarm - the whole component is a prefab reference and a parent to keep the hierarchy
    /// tidy. Parenting is fine here precisely because nothing writes these transforms from a job.
    /// </summary>
    public class BuildingViewSettings : MonoBehaviour
    {
        [SerializeField, Tooltip("Instanced once per building, scaled to its footprint and tinted.")]
        private GameObject _buildingPrefab;

        private BuildingViewSystem _system;

        private void OnEnable()
        {
            if (_buildingPrefab == null)
            {
                Debug.LogError($"{nameof(BuildingViewSettings)} has no prefab; buildings will be invisible.", this);
                return;
            }

            World world = World.DefaultGameObjectInjectionWorld;
            _system = world is { IsCreated: true } ? world.GetExistingSystemManaged<BuildingViewSystem>() : null;
            if (_system == null)
            {
                Debug.LogError($"No {nameof(BuildingViewSystem)} in the default world.", this);
                return;
            }

            _system.Bind(_buildingPrefab, transform);
        }

        private void OnDisable()
        {
            _system?.Unbind();
            _system = null;
        }
    }
}
