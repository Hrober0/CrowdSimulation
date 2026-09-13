using System;
using Unity.Mathematics;

namespace GridNav
{
    /// <summary>
    /// What a cached flow field is: a destination, and who is walking to it (design §14.4).
    ///
    /// The destination alone was the key until structures could be broken through. It stopped being enough
    /// the moment cost depended on the seeker - an army and a hauler heading for the same cell disagree about
    /// every wall between here and there, so one field cannot answer both.
    ///
    /// It stays a *small* key on purpose. Every distinct value is a field to build and evict, so the seeker's
    /// half is a <see cref="Traversal"/> - three breach classes and a faction - rather than anything
    /// per-agent, which is what keeps a whole wave sharing one field.
    /// </summary>
    public readonly struct FieldKey : IEquatable<FieldKey>
    {
        public readonly int2 Goal;

        public readonly Traversal Traversal;

        public FieldKey(int2 goal, Traversal traversal)
        {
            Goal = goal;
            Traversal = traversal;
        }

        public bool Equals(FieldKey other) => Goal.Equals(other.Goal) && Traversal.Equals(other.Traversal);

        public override bool Equals(object obj) => obj is FieldKey other && Equals(other);

        public override int GetHashCode() => (int)math.hash(new int3(Goal, Traversal.Id));

        public override string ToString() => $"Field({Goal} for {Traversal})";
    }
}
