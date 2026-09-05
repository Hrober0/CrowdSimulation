using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Rts;
using Unity.Mathematics;

namespace Tests.EditorTests.RtsTests
{
    /// <summary>
    /// The ring walk both outward searches are built on (design §14 steps 10 and 11).
    ///
    /// Worth its own tests because getting it subtly wrong is silent: a ring that skips a cell means a mine
    /// that occasionally fails to notice the seam next door, which looks like bad luck rather than like a
    /// bug, and a ring that visits one twice only ever wastes work. Neither would fail a test about mining.
    /// </summary>
    public class CellRingTests
    {
        private static readonly int2 Centre = new(3, -2);

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(5)]
        public void ARingIsExactlyTheCellsAtThatDistance(int ring)
        {
            var expected = new HashSet<int2>();
            for (int y = -ring; y <= ring; y++)
            {
                for (int x = -ring; x <= ring; x++)
                {
                    if (math.max(math.abs(x), math.abs(y)) == ring)
                    {
                        expected.Add(Centre + new int2(x, y));
                    }
                }
            }

            var walked = new HashSet<int2>();
            int count = CellRing.Count(ring);

            for (int i = 0; i < count; i++)
            {
                walked.Add(CellRing.At(Centre, ring, i)).Should()
                      .BeTrue("no cell should be visited twice");
            }

            count.Should().Be(expected.Count, "the count has to match what the walk produces");
            walked.Should().BeEquivalentTo(expected);
        }

        /// <summary>
        /// Walked together, the rings cover the square exactly once - which is what makes "search outward
        /// until something turns up" the same search as "look at everything in range, nearest first".
        /// </summary>
        [Test]
        public void TheRingsTogetherCoverTheWholeRangeExactlyOnce()
        {
            const int range = 4;

            var seen = new HashSet<int2>();
            for (int ring = 0; ring <= range; ring++)
            {
                int count = CellRing.Count(ring);
                for (int i = 0; i < count; i++)
                {
                    seen.Add(CellRing.At(Centre, ring, i)).Should().BeTrue();
                }
            }

            int side = range * 2 + 1;
            seen.Count.Should().Be(side * side);
        }
    }
}
