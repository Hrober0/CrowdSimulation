using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    public enum TaskStepKind : byte
    {
        /// <summary>Walk to <see cref="TaskStep.Cell"/>. Done when the agent arrives.</summary>
        GoTo,

        /// <summary>Step inside <see cref="TaskStep.Target"/>, leaving the map entirely.</summary>
        Enter,

        /// <summary>Stand still for <see cref="TaskStep.Duration"/>. What it *means* is the per-kind code.</summary>
        Interact,

        /// <summary>Step back out onto <see cref="TaskStep.Cell"/>.</summary>
        Exit,
    }

    /// <summary>
    /// One step of what an agent is doing (design §9). There is no per-type state machine: a worker, a hauler
    /// and a soldier are the same archetype running the same four steps in different orders.
    ///
    /// <code>
    /// worker  GoTo(slot)  -> Interact(inf)
    /// hauler  GoTo(src)   -> Interact(pickup) -> GoTo(dst) -> Interact(deposit)
    /// idle    GoTo(door)  -> Enter(hut)
    /// </code>
    ///
    /// Six entries inline. The design budgeted four, which is the length of the hauler task above; the two
    /// extra are for the <c>Exit</c> that has to go in front of it when the hauler is woken out of a hut -
    /// and idle agents are *in* huts, so that is the common case rather than the exception. Spilling to the
    /// heap would work, but not for the most frequently built task in the game.
    /// </summary>
    [InternalBufferCapacity(6)]
    public struct TaskStep : IBufferElementData
    {
        public TaskStepKind Kind;

        /// <summary>The building or object the step is about, if it is about one.</summary>
        public Entity Target;

        /// <summary>Where: the cell to walk to, enter through, or come back out onto.</summary>
        public int2 Cell;

        /// <summary>Seconds left of an <see cref="TaskStepKind.Interact"/>. Counted down in place.</summary>
        public float Duration;

        /// <summary>
        /// What the <see cref="TaskStepKind.Interact"/> settles when it finishes. The one field in the whole
        /// machine that is per-kind, which is exactly what §9 says it should be.
        /// </summary>
        public InteractionKind Interaction;

        public static TaskStep GoTo(int2 cell) => new()
        {
            Kind = TaskStepKind.GoTo,
            Cell = cell,
        };

        public static TaskStep Enter(Entity building, int2 entranceCell) => new()
        {
            Kind = TaskStepKind.Enter,
            Target = building,
            Cell = entranceCell,
        };

        public static TaskStep Interact(Entity target, float duration) => new()
        {
            Kind = TaskStepKind.Interact,
            Target = target,
            Duration = duration,
        };

        public static TaskStep Exit(Entity building, int2 entranceCell) => new()
        {
            Kind = TaskStepKind.Exit,
            Target = building,
            Cell = entranceCell,
        };

        public static TaskStep Pickup(Entity source, float duration) => new()
        {
            Kind = TaskStepKind.Interact,
            Target = source,
            Duration = duration,
            Interaction = InteractionKind.Pickup,
        };

        public static TaskStep Deposit(Entity target, float duration) => new()
        {
            Kind = TaskStepKind.Interact,
            Target = target,
            Duration = duration,
            Interaction = InteractionKind.Deposit,
        };

        public static TaskStep Work(Entity crafter, float duration) => new()
        {
            Kind = TaskStepKind.Interact,
            Target = crafter,
            Duration = duration,
            Interaction = InteractionKind.Work,
        };
    }
}
