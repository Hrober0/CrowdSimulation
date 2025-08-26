using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FluentAssertions.Collections;

namespace Tests.TestsUtilities
{
    public static class FluentAssertionIEnumerableExtensions
    {
        public static AndConstraint<GenericCollectionAssertions<T>> ContainInOrderLooped<T>(
            this GenericCollectionAssertions<T> assertions,
            params T[] expected)
        {
            List<T> inputArray = assertions.Subject.ToList();

            inputArray.Should().HaveCountGreaterOrEqualTo(expected.Length);

            inputArray.Should().Contain(expected[0],
                $"first expected item {expected[0]} must exist in the collection");

            var startIndex = inputArray.IndexOf(expected[0]);

            for (int i = 0; i < expected.Length; i++)
            {
                T nextItem = inputArray[(startIndex + i) % expected.Length];

                nextItem.Should().Be(expected[i],
                    $"{expected[i]} was expected in sequence {string.Join(",", inputArray)}");
            }

            return new(assertions);
        }
        
        public static AndConstraint<GenericCollectionAssertions<T>> ContainOnly<T>(
            this GenericCollectionAssertions<T> assertions, params T[] expected)
        {
            var actual = assertions.Subject?.ToList() ?? new List<T>();

            // Check count
            actual.Count.Should().Be(expected.Length,
                $"collection should have exactly {expected.Length} items, but found {actual.Count} ({string.Join(", ", actual)})");

            // Check content (ignoring order, but respecting duplicates)
            var actualGrouped = actual.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
            var expectedGrouped = expected.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());

            actualGrouped.Should().BeEquivalentTo(expectedGrouped,
                "collection should contain only [{0}], but found [{1}]",
                string.Join(", ", expected),
                string.Join(", ", actual));

            return new(assertions);
        }
    }
}