using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// A crafter whose batch changes the worker rather than filling a shelf (design §14 step 13).
    ///
    /// Beside <see cref="Recipe"/>, never instead of it. A camp has inputs on a shelf, benches inside, a work
    /// order and a worker doing a shift at it exactly as a bakery does - the recipe is what says a batch takes
    /// four seconds and eats a loaf, and this is what says the worker walks out of the door as a soldier. The
    /// split is what makes a camp cost nothing anywhere else: <c>WorkRequestSystem</c>, the order market and
    /// the interior claim have no idea it is not a bakery.
    ///
    /// A recipe with no <see cref="RecipeOutput"/> is not an error, then, and that is the one thing this makes
    /// true: <see cref="RecipeUtils.CanCraft"/> asks for room for the outputs, and a batch with no outputs
    /// needs none, so a camp is never blocked by a full shelf it does not have.
    ///
    /// **It converts rather than creates**, and that is a design decision rather than an implementation one.
    /// A camp that produced a second body would make soldiers free in the only currency that matters - people -
    /// and it would owe an entity creation per batch. Taking the worker costs the economy a pair of hands,
    /// which is what an army should cost, and costs the simulation nothing at all: arming is an enableable bit
    /// on an archetype that already carries the weapon (<see cref="AgentFactory.Arm"/>).
    /// </summary>
    public struct Trains : IComponentData
    {
        /// <summary>What the worker is armed with when the shift ends.</summary>
        public Weapon Gives;

        /// <summary>
        /// How far the new soldier will chase, in cells from wherever it walks out. Zero takes the default.
        /// See <see cref="Post"/> for why a leash is not optional.
        /// </summary>
        public float Leash;
    }
}
