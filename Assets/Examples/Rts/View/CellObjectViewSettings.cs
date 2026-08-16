using Unity.Entities;
using UnityEngine;

namespace Examples.Rts
{
    /// <summary>
    /// The scene half of the world-object view: it owns the prefab and lends it to
    /// <see cref="CellObjectViewSystem"/> for as long as it is enabled.
    ///
    /// Same shape as <see cref="BuildingViewSettings"/> - trees never move, so there is no pool and nothing to
    /// prewarm, and parenting them under this object is fine precisely because nothing writes these transforms
    /// from a job.
    /// </summary>
    public class CellObjectViewSettings : MonoBehaviour
    {
        [SerializeField, Tooltip("Instanced once per tree or rock, scaled and tinted by its kind.")]
        private GameObject _objectPrefab;

        private CellObjectViewSystem _system;

        private void OnEnable()
        {
            if (_objectPrefab == null)
            {
                Debug.LogError($"{nameof(CellObjectViewSettings)} has no prefab; trees will be invisible.", this);
                return;
            }

            World world = World.DefaultGameObjectInjectionWorld;
            _system = world is { IsCreated: true } ? world.GetExistingSystemManaged<CellObjectViewSystem>() : null;
            if (_system == null)
            {
                Debug.LogError($"No {nameof(CellObjectViewSystem)} in the default world.", this);
                return;
            }

            _system.Bind(_objectPrefab, transform);
        }

        private void OnDisable()
        {
            _system?.Unbind();
            _system = null;
        }
    }
}
