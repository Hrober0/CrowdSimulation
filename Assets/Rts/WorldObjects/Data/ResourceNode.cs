using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// Marks a <see cref="CellObject"/> whose <see cref="StorageSlot"/> is a deposit to be harvested rather
    /// than a building's store (design §14 step 9).
    ///
    /// A node needs no machinery of its own, and that is the point of it being a storage slot at all. A haul
    /// source is *any* entity holding stock somebody may take - neither <c>OrderAssignSystem</c> nor
    /// <c>InteractionSystem</c> knows what a building is - so an ore seam with a slot on it is a source, and
    /// the reservations that stop two haulers emptying one warehouse stop two miners emptying one seam
    /// without anything being written twice.
    ///
    /// What the tag is *for* is exclusion. The economy's request pass walks every storage entity there is
    /// looking for one that is asking for something, and a node is a pure source that never asks: twenty
    /// thousand trees in that scan is the whole economy budget spent to conclude nothing. The tag is what
    /// lets those queries say <c>.WithNone&lt;ResourceNode&gt;()</c> and stay proportional to the number of
    /// buildings rather than to the number of trees.
    /// </summary>
    public struct ResourceNode : IComponentData
    {
    }
}
