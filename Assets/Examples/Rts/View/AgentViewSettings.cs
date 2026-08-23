using Unity.Entities;
using UnityEngine;

namespace Examples.Rts
{
    /// <summary>
    /// The scene half of the view layer (design §10): it owns the agent prefab and the pool built from it,
    /// and lends both to <see cref="ViewSyncSystem"/> for as long as it is enabled.
    ///
    /// Put one in the scene - not in the subscene - next to the camera.
    /// </summary>
    public class AgentViewSettings : MonoBehaviour
    {
        [SerializeField, Tooltip("Instanced once per visible agent. Its transform is driven; nothing else is.")]
        private GameObject _agentPrefab;

        [SerializeField, Min(0)]
        [Tooltip("Instances made up front, so the first crowd does not pay for them mid-frame.")]
        private int _prewarm = 256;

        [SerializeField, Tooltip("Sorting offset towards the camera, in world units. Presentation only.")]
        private float _depth;

        [SerializeField, Min(0f)]
        [Tooltip("Extra depth while crossing a bridge, so the agent draws over the deck rather than under it. "
                 + "Must clear half the building prefab's thickness.")]
        private float _bridgeLift = 1f;

        [SerializeField, Min(1f)]
        [Tooltip("Degrees a second an agent may swing round. Lower reads heavier; high enough and it spins.")]
        private float _turnDegreesPerSecond = 270f;

        [SerializeField, Min(0f)]
        [Tooltip("Agents further than this from the focus get no view. 0 shows every agent.")]
        private float _cullRadius;

        [SerializeField, Tooltip("What the cull radius is measured from. Falls back to the main camera.")]
        private Transform _focus;

        private AgentViewPool _pool;
        private ViewSyncSystem _system;
        private Transform _resolvedFocus;

        /// <summary>The live pool, for its counts. Null while this object is disabled.</summary>
        public AgentViewPool Pool => _pool;

        private void OnEnable()
        {
            if (_agentPrefab == null)
            {
                Debug.LogError($"{nameof(AgentViewSettings)} has no agent prefab; agents will be invisible.", this);
                return;
            }

            World world = World.DefaultGameObjectInjectionWorld;
            _system = world is { IsCreated: true } ? world.GetExistingSystemManaged<ViewSyncSystem>() : null;
            if (_system == null)
            {
                Debug.LogError($"No {nameof(ViewSyncSystem)} in the default world; agents will be invisible.", this);
                return;
            }

            _resolvedFocus = _focus != null ? _focus : Camera.main != null ? Camera.main.transform : null;

            _pool = new AgentViewPool(_agentPrefab, _prewarm);
            _system.Bind(_pool);
        }

        /// <summary>
        /// MonoBehaviour updates run before <see cref="PresentationSystemGroup"/> in the player loop, so what
        /// is pushed here is what this frame's sync reads.
        /// </summary>
        private void Update()
        {
            if (_system == null)
            {
                return;
            }

            _system.Depth = _depth;
            _system.BridgeLift = _bridgeLift;
            _system.TurnDegreesPerSecond = _turnDegreesPerSecond;
            _system.Culling = _resolvedFocus != null
                ? new ViewCulling(SimToWorld.ToSim(_resolvedFocus.position), _cullRadius)
                : default;
        }

        private void OnDisable()
        {
            _system?.Unbind(_pool);
            _pool?.Dispose();

            _pool = null;
            _system = null;
            _resolvedFocus = null;
        }
    }
}
