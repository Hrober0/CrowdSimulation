using System;

namespace GridNav
{
    /// <summary>
    /// How hard a seeker can hit a structure (design §14 step 13, amended).
    ///
    /// Three classes and not a damage number, and the difference is the whole design. Cost that varied with
    /// the individual seeker would make a flow field a *per-agent* thing, and a field shared by everyone
    /// heading to one destination is the entire reason §1 gives for the grid replacing the navmesh. Buckets
    /// keep the sharing: every unit in a wave asks the same question, so they all read one field and all
    /// arrive at the same breach.
    ///
    /// <see cref="None"/> is not a special case bolted on beside the other two - it is the civilian rule
    /// falling out of the same arithmetic. A hauler does no damage, so every structure prices as infinite to
    /// it, so it walks round. There is no "civilian field" and "assault field"; there is one cost function.
    /// </summary>
    public enum BreachClass : byte
    {
        /// <summary>Cannot break anything. Every structure is a wall. Haulers, workers, everyone unarmed.</summary>
        None = 0,

        Low = 1,

        High = 2,
    }

    /// <summary>
    /// Everything about a seeker that changes what the map costs it: how hard it hits, and whose buildings
    /// are its own.
    ///
    /// This is the cache key for a gate graph and for a flow field. Keeping it to two bytes is not thrift -
    /// it is the budget. Each distinct value in play is a separate derived structure to build and keep in
    /// step, so the set has to stay small enough to enumerate, which is what <see cref="BreachClass"/> being
    /// three values rather than a damage figure buys.
    /// </summary>
    public readonly struct Traversal : IEquatable<Traversal>
    {
        /// <summary>How many distinct traversals may exist at once. Sized for the gate graphs behind them.</summary>
        public const int MAX_CLASSES = 16;

        public readonly BreachClass Breach;

        /// <summary>
        /// Whose side the seeker is on. Only read when <see cref="Breach"/> is not
        /// <see cref="BreachClass.None"/> - a seeker that cannot break anything is not helped by knowing who
        /// owns the wall, so every faction's civilians share one traversal and one set of fields.
        /// </summary>
        public readonly byte Faction;

        public Traversal(BreachClass breach, byte faction)
        {
            Breach = breach;
            Faction = breach == BreachClass.None ? (byte)0 : faction;
        }

        /// <summary>Anything unarmed: structures are walls, whoever owns them.</summary>
        public static Traversal Civilian => default;

        public bool CanBreach => Breach != BreachClass.None;

        /// <summary>
        /// A dense index, so the derived structures can live in an array rather than a hash map. Zero is
        /// always the civilian view, which is what nearly everything asks for.
        /// </summary>
        public int Id => Breach == BreachClass.None ? 0 : 1 + Faction * 2 + ((int)Breach - 1);

        public bool Equals(Traversal other) => Breach == other.Breach && Faction == other.Faction;

        public override bool Equals(object obj) => obj is Traversal other && Equals(other);

        public override int GetHashCode() => Id;

        public override string ToString() =>
            Breach == BreachClass.None ? "Traversal(civilian)" : $"Traversal({Breach}, faction {Faction})";
    }
}
