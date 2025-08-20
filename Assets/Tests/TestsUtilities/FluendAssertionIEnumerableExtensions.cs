using System.Collections.Generic;
using System.Linq;
using FluentAssertions;

namespace Tests.TestsUtilities
{
    public static class FluentAssertionIEnumerableExtensions
    {
        public static void Should_ContainInOrder<T>(this IEnumerable<T> input, params T[] expected)
        {
            var inputArray = input.ToList();
            inputArray.Count.Should().Be(expected.Length);
            inputArray.Should().Contain(expected[0]);

            var startIndex = inputArray.IndexOf(expected[0]);
            for (int i = 0; i < expected.Length; i++)
            {
                T nextItem = inputArray[(startIndex + i) % expected.Length];
                nextItem.Should().Be(expected[i], $"{expected[i]} was expected in {nameof(input)}{inputArray.Stringify()}");
            }
        }
    }
}