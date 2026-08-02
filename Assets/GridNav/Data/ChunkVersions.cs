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

        public override string ToString() => $"Versions(passability: {PassabilityVersion}, cost: {CostVersion})";
    }
}
