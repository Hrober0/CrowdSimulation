using Unity.Burst;
using Unity.Jobs;

namespace GridNav
{
    /// <summary>
    /// Drains the edit queue into the grid. Single job on purpose: this is the one place the grid is written,
    /// and the whole map is one shared container, so there is nothing to parallelise here (§13.2, invariant 1).
    /// </summary>
    [BurstCompile]
    public struct ApplyGridEditsJob : IJob
    {
        public GridMap Map;
        public GridEditQueue Edits;

        public void Execute()
        {
            while (Edits.TryDequeue(out GridEdit edit))
            {
                Map.Apply(edit);
            }
        }
    }
}
