namespace GridNav
{
    /// <summary>
    /// Two counters per chunk, not one (design §3). Harvesting a forest churns cost constantly; with a single
    /// counter every swing of an axe would rebuild the navigation graph.
    /// </summary>
    public struct ChunkVersions
    {
        /// <summary>
        /// Bumped when a cell in the chunk crossed the blocked threshold, or when its exit mask changed.
        /// Consumers: the chunk gate graph.
        /// </summary>
        public uint PassabilityVersion;

        /// <summary>
        /// Bumped on every cost or exit change, passability-changing or not. Consumers: the flow field cache.
        /// Any bump of <see cref="PassabilityVersion"/> bumps this one too, so a consumer only ever watches one.
        /// </summary>
        public uint CostVersion;

        /// <summary>
        /// Bumped when a structure in the chunk appears, falls, or changes damage band. Consumers: the flow
        /// field cache and the gate graph, but **only for a traversal that can break things**.
        ///
        /// It is separate from <see cref="CostVersion"/> on purpose, and it is the reason a fight is
        /// affordable. A structure's health changes nothing for a hauler - a wall is a wall at any health -
        /// so a battle raging across the map must not invalidate the fields the bread economy is steering on.
        /// Folding this into the cost version would rebuild every civilian field around a building under
        /// attack, for the length of the fight, to no effect whatever.
        /// </summary>
        public uint StructureVersion;

        public override string ToString() =>
            $"Versions(passability: {PassabilityVersion}, cost: {CostVersion}, structure: {StructureVersion})";
    }
}
