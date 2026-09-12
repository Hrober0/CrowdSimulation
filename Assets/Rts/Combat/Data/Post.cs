using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// Where a soldier belongs, and how far it will go from there (design §14 step 13).
    ///
    /// **The leash is not a tuning number, it is what makes pursuit safe to have at all.** A soldier that
    /// chases without limit can be walked away from what it was guarding by one expendable scout, and a
    /// defence that can be emptied by running past it is not a defence. So a soldier is dispatched only at
    /// threats near its post, and abandons the chase and goes home the moment it strays further than this.
    ///
    /// The post is the last place the soldier was *told* to be: where it was armed, or where the player last
    /// sent it. That makes "move the garrison" and "change what the garrison guards" the same gesture, which
    /// is the behaviour every RTS has and none of them explains.
    /// </summary>
    public struct Post : IComponentData
    {
        public float2 Home;

        /// <summary>In cells, from <see cref="Home"/>. Zero means it never leaves.</summary>
        public float Leash;

        public bool IsWithinLeash(float2 position) =>
            math.lengthsq(position - Home) <= Leash * Leash;
    }
}
