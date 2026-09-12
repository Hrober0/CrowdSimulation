using System;
using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// Which side something is on (design §14 step 13).
    ///
    /// A byte rather than a tag, because the template is built for two *or more* sides fighting each other
    /// rather than for a player and one scripted enemy. Everything faction-shaped below has to stay correct
    /// as the count grows, which rules out any mechanism whose cost is per-faction-squared.
    ///
    /// Carried by agents and by buildings. Anything without one - a tree, an ore seam, a bridge - is nobody's
    /// and is never a target: <see cref="AreEnemies"/> is asked about two components, and the absence of the
    /// component is the absence of a quarrel.
    ///
    /// Which id is the player's is not `Rts`' business. The simulation only ever asks whether two ids differ;
    /// the game layer decides that zero is the side the mouse belongs to.
    /// </summary>
    public struct Faction : IComponentData, IEquatable<Faction>
    {
        public byte Id;

        /// <summary>
        /// Everyone who is not us. There are no alliances and no neutrals-that-can-be-provoked yet, and
        /// adding either means changing this one function rather than every place that fights.
        /// </summary>
        public static bool AreEnemies(Faction a, Faction b) => a.Id != b.Id;

        /// <summary>
        /// Whose that entity is, treating a missing component as the default side rather than as a third
        /// answer.
        ///
        /// Deliberate: it keeps every building and agent that predates factions - the whole test suite, and
        /// anything an older scene bakes - on one side and working, instead of making "has no faction" a
        /// state every caller has to handle. A thing that genuinely belongs to nobody, a tree or a bridge,
        /// is never asked about, because nothing posts orders for it or shoots at it.
        /// </summary>
        public static Faction Of(in ComponentLookup<Faction> factions, Entity entity) =>
            factions.HasComponent(entity) ? factions[entity] : default;

        public bool Equals(Faction other) => Id == other.Id;

        public override bool Equals(object obj) => obj is Faction other && Equals(other);

        public override int GetHashCode() => Id;
    }
}
