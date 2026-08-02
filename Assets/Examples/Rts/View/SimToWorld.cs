using Unity.Mathematics;
using UnityEngine;

namespace Examples.Rts
{
    /// <summary>
    /// The single simulation-to-world mapping (design §2). The simulation is 2D <c>float2</c> throughout and
    /// knows nothing about presentation; every view, gizmo and mouse pick goes through here.
    ///
    /// Default is XY, matching the 2D URP scene. Switching the whole game to a 3D look is changing the four
    /// functions below to map onto XZ - and nothing else.
    /// </summary>
    public static class SimToWorld
    {
        public static Vector3 Position(float2 sim) => new(sim.x, sim.y, 0f);

        /// <param name="depth">Sorting offset towards the camera, in world units. Presentation only.</param>
        public static Vector3 Position(float2 sim, float depth) => new(sim.x, sim.y, -depth);

        public static Vector3 Direction(float2 sim) => new(sim.x, sim.y, 0f);

        public static float2 ToSim(Vector3 world) => new(world.x, world.y);

        /// <summary>Rotation of a view whose "forward" in simulation space is <paramref name="facing"/>.</summary>
        public static Quaternion Rotation(float2 facing) =>
            math.lengthsq(facing) < math.EPSILON
                ? Quaternion.identity
                : Quaternion.LookRotation(Vector3.forward, Direction(facing));
    }
}
