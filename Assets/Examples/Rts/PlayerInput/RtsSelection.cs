using HCore;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Examples.Rts
{
    public enum SelectionKind
    {
        None,
        Building,
        Agent,

        /// <summary>Bare ground, or a road. Always available, so a click never selects nothing.</summary>
        Cell,
    }

    /// <summary>What the player last clicked on.</summary>
    public readonly struct RtsSelection
    {
        public readonly SelectionKind Kind;
        public readonly Entity Entity;
        public readonly int2 Cell;

        public RtsSelection(SelectionKind kind, Entity entity, int2 cell)
        {
            Kind = kind;
            Entity = entity;
            Cell = cell;
        }

        public static RtsSelection Nothing => default;

        public static RtsSelection Of(Entity building, int2 cell) =>
            new(SelectionKind.Building, building, cell);

        public static RtsSelection Agent(Entity agent, int2 cell) =>
            new(SelectionKind.Agent, agent, cell);

        public static RtsSelection Ground(int2 cell) =>
            new(SelectionKind.Cell, Entity.Null, cell);
    }

    /// <summary>
    /// Raised when the player clicks something. Goes through the <see cref="EventBus"/> rather than a static
    /// event on the input script, per the project's rule about global state: the input tool knows nothing
    /// about the panel, and a second listener - a highlight, a sound - costs nothing to add.
    /// </summary>
    public interface ISelectionHandler : IEventHandler
    {
        void OnSelectionChanged(RtsSelection selection);
    }

    /// <summary>
    /// Asked by the input tool before it acts on a click, so a click that lands on the panel does not also
    /// place a building behind it. The UI is the one thing that knows where it is.
    /// </summary>
    public interface IPointerOverUiQuery : ISingleEventHandler
    {
        bool IsPointerOverUi();
    }

    /// <summary>
    /// Where the panel is on screen, in pixels from the bottom left, or an empty rect when no panel is up.
    ///
    /// Asked by the gizmo drawers. The editor composites gizmos after the camera and after the UI, so no
    /// sorting order will put the panel on top of them: the only side of it that can move is the gizmos,
    /// which is why they need to know which rectangle to leave alone.
    /// </summary>
    public interface IUiScreenRectQuery : ISingleEventHandler
    {
        Rect UiScreenRect();
    }
}
