using System;
using UnityEngine;

namespace Examples.Storage.UI
{
    /// <summary>
    /// Owns all world-space mouse input. Add this component to the same GameObject as Main.
    /// Fires <see cref="WorldClicked"/> when the user left-clicks outside the UI panel.
    /// <para>
    /// <see cref="MouseOverUI"/> is set externally by the UI layer (Main) via pointer
    /// enter/leave callbacks — the only coupling point between UI and input.
    /// </para>
    /// </summary>
    public class WorldInputHandler : MonoBehaviour
    {
        public static WorldInputHandler Instance { get; private set; }

        /// <summary>Set to true while the pointer is over the UI panel.</summary>
        public static bool MouseOverUI { get; set; }

        /// <summary>Fired with the world-space position of each valid left-click.</summary>
        public static event Action<Vector3> WorldClicked;

        void Awake()
        {
            Instance = this;
        }

        void Update()
        {
            if (!Input.GetMouseButtonDown(0) || MouseOverUI) return;

            var cam = Camera.main;
            var mp  = Input.mousePosition;
            var wp  = cam.ScreenToWorldPoint(new Vector3(mp.x, mp.y, -cam.transform.position.z));
            wp.z = 0;
            WorldClicked?.Invoke(wp);
        }
    }
}
