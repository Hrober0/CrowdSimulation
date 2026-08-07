using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// Room inside a building (design §6). One mechanism serves all four uses of "an agent is in here rather
    /// than on the map": a worker working, a hauler picking up or depositing, an idle hauler resting in a
    /// hut, a soldier garrisoned.
    ///
    /// The design lists two fields; there are three, because "the interior slot is claimed *before* the walk
    /// begins" needs somewhere to record a claim that is not yet an occupant. <see cref="Occupied"/> is who
    /// is physically inside and is what production and display want; <see cref="Claimed"/> is inside plus
    /// walking here, and it is the one that must never exceed <see cref="Capacity"/> - capping occupants
    /// instead would let ten agents walk to a hut with two beds and eight of them arrive to be turned away
    /// at the door, which is the entrance pile-up §8 exists to prevent.
    /// </summary>
    public struct Interior : IComponentData
    {
        public int Capacity;

        /// <summary>Agents inside right now. Never above <see cref="Claimed"/>.</summary>
        public int Occupied;

        /// <summary>Agents inside or on their way with a slot held for them. Never above <see cref="Capacity"/>.</summary>
        public int Claimed;

        public int FreeSlots => Capacity - Claimed;

        public bool HasRoom => Claimed < Capacity;
    }
}
