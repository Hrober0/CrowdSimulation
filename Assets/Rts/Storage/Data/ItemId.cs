using System;

namespace Rts
{
    /// <summary>
    /// What is being stored, hauled or crafted. An opaque id rather than an enum, so the set of items is a
    /// content decision made in the game layer and `Rts` never has to be recompiled to add one.
    ///
    /// Zero is "no item", which is what an empty slot and an empty pair of hands both read as.
    /// </summary>
    public readonly struct ItemId : IEquatable<ItemId>
    {
        public readonly ushort Value;

        public ItemId(ushort value)
        {
            Value = value;
        }

        public static ItemId None => default;

        public bool IsNone => Value == 0;

        public bool Equals(ItemId other) => Value == other.Value;

        public override bool Equals(object obj) => obj is ItemId other && Equals(other);

        public override int GetHashCode() => Value;

        public static bool operator ==(ItemId left, ItemId right) => left.Equals(right);

        public static bool operator !=(ItemId left, ItemId right) => !left.Equals(right);

        public override string ToString() => $"Item({Value})";
    }
}
