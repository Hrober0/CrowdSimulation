using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// How long an agent has been trying to walk without getting anywhere (design §8, §13.3 #21).
    ///
    /// <see cref="LastProgressPosition"/> is not last frame's position - it is the last place the agent
    /// actually got to. Comparing against that rather than against the previous frame is what makes the
    /// watchdog immune to an agent shuffling on the spot: RVO jitter moves it every frame while getting it
    /// nowhere, and a frame-to-frame test would read that as progress forever.
    /// </summary>
    public struct MovementWatchdog : IComponentData
    {
        public float2 LastProgressPosition;

        public float StalledSeconds;
    }
}
