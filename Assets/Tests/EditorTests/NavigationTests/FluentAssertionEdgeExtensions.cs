using System.Collections.Generic;
using FluentAssertions;
using Navigation;

namespace Tests.EditorTests.NavigationTests
{
    public static class FluentAssertionEdgeExtensions
    {
        public static void Should_ContainKey(this IEnumerable<Edge> edges, EdgeKey key)
        {
            edges.Should().Contain(e => e.ToEdgeKey().Equals(key),
                $"because edge list should contain {key}");
        }

        public static void ShouldNotContainKey(this IEnumerable<Edge> edges, EdgeKey key)
        {
            edges.Should().NotContain(e => e.ToEdgeKey().Equals(key),
                $"because edge list should not contain {key}");
        }
    }
}