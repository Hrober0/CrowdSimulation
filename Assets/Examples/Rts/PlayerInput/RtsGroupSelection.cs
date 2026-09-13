using System.Collections.Generic;
using HCore;
using Unity.Entities;

namespace Examples.Rts
{
    /// <summary>
    /// Raised when the player drags a box over the map and picks up more than one unit.
    ///
    /// Separate from <see cref="ISelectionHandler"/> rather than folded into it, because the two answer
    /// different questions and want different panels. One selection is "what is this and what is it doing",
    /// and the inspector shows everything it knows. A group is "where are these and where shall they go",
    /// and a list of stat blocks would be unreadable at twenty units.
    ///
    /// The list belongs to the sender and is valid for the call only. A handler that wants to keep it copies
    /// it - passing the live list would make the panel's display depend on what the cursor did since.
    /// </summary>
    public interface IGroupSelectionHandler : IEventHandler
    {
        void OnGroupSelected(IReadOnlyList<Entity> agents);
    }
}
