using Unity.Entities;
using UnityEngine;

namespace Examples.Rts
{
    /// <summary>
    /// The scene half of the combat view: it owns the soldier, weapon and bullet prefabs and the
    /// <see cref="CombatView"/> built from them, and lends it to <see cref="CombatViewSystem"/> while enabled.
    ///
    /// Optional. A scene without one still fights; it just shows the fight as agents vanishing.
    /// </summary>
    public class CombatViewSettings : MonoBehaviour
    {
        [SerializeField] private CombatViewPrefabs _prefabs;

        [SerializeField, Min(0f)]
        [Tooltip("Weapons reaching this many cells or less are drawn as swords, anything longer as guns.")]
        private float _swordReach = 2f;

        [SerializeField, Tooltip("Sorting offset of soldiers towards the camera. Must clear the agent view.")]
        private float _depth = 0.1f;

        [SerializeField, Tooltip("Sorting offset of bullets. Above the soldiers, so a shot is never hidden.")]
        private float _bulletDepth = 0.3f;

        [SerializeField, Min(1f), Tooltip("Cells a second. Presentation only - the hit has already landed.")]
        private float _bulletSpeed = 24f;

        [SerializeField, Min(0f)]
        [Tooltip("Soldiers further than this from the focus get no view. 0 shows every soldier.")]
        private float _cullRadius;

        [SerializeField, Tooltip("What the cull radius is measured from. Falls back to the main camera.")]
        private Transform _focus;

        private CombatView _view;
        private CombatViewSystem _system;
        private Transform _resolvedFocus;

        private void OnEnable()
        {
            if (_prefabs.Sword == null || _prefabs.Gun == null || _prefabs.Bullet == null)
            {
                Debug.LogError($"{nameof(CombatViewSettings)} is missing a prefab; soldiers will be drawn unarmed.", this);
                return;
            }

            World world = World.DefaultGameObjectInjectionWorld;
            _system = world is { IsCreated: true } ? world.GetExistingSystemManaged<CombatViewSystem>() : null;
            if (_system == null)
            {
                Debug.LogError($"No {nameof(CombatViewSystem)} in the default world.", this);
                return;
            }

            _resolvedFocus = _focus != null ? _focus : Camera.main != null ? Camera.main.transform : null;

            _view = new CombatView(_prefabs, transform);
            _system.Bind(_view);
        }

        /// <summary>Pushed every frame, before the view group runs, so the inspector can tune it live.</summary>
        private void Update()
        {
            if (_system == null || _view == null)
            {
                return;
            }

            _view.Depth = _depth;
            _view.BulletDepth = _bulletDepth;
            _view.BulletSpeed = _bulletSpeed;

            _system.SwordReach = _swordReach;
            _system.Culling = _resolvedFocus != null
                ? new ViewCulling(SimToWorld.ToSim(_resolvedFocus.position), _cullRadius)
                : default;
        }

        private void OnDisable()
        {
            _system?.Unbind(_view);
            _view?.Dispose();

            _view = null;
            _system = null;
            _resolvedFocus = null;
        }
    }
}
