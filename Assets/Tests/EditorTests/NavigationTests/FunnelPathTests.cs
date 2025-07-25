using FluentAssertions;
using Navigation;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Tests.TestsUtilities;

namespace Tests.EditorTests.NavigationTests
{
    public class FunnelPathTests
    {
        [Test]
        public void FunnelPath_StraightLine_ShouldReturnSingleStraightSegment()
        {
            var portals = new NativeArray<Portal>(3, Allocator.Temp);
            var result = new NativeList<float2>(Allocator.Temp);

            var start = new float2(0, 0);
            var end = new float2(0.5f, 2);

            portals[0] = new(start, start);
            portals[1] = new(new(0, 1), new(1, 1));
            portals[2] = new(end, end);

            FunnelPath.FromPortals(portals, result);

            result.Length.Should().BeGreaterOrEqualTo(2);
            result[0].Should().BeApproximately(start);
            result[^1].Should().BeApproximately(end);

            portals.Dispose();
            result.Dispose();
        }

        [Test]
        public void FunnelPath_NarrowTurns_ShouldFollowPortalsCorrectly()
        {
            var portals = new NativeArray<Portal>(6, Allocator.Temp);
            var result = new NativeList<float2>(Allocator.Temp);

            //   Y ↑
            //   6 |                     \          /
            //   5 |                      \        /
            //   4 |                       \      /
            //   3 |                             /                       
            //   2 |              ----------    /
            //   1 |       
            //   0 |  s         /                         
            //  -1 |          /                 e     
            //  -2 |        /
            //     +----------------------------------------→ X
            //         0    1    2    3    4    5    6

            portals[0] = new(new(0, 0), new(0, 0)); // start
            portals[1] = new(new(2, 0), new(1, -2)); // /
            portals[2] = new(new(2, 2), new(4, 2)); // _
            portals[3] = new(new(3, 6), new(4, 4)); // \
            portals[4] = new(new(6, 6), new(5, 2)); // /
            portals[5] = new(new(5, -1), new(5, -1)); // end

            FunnelPath.FromPortals(portals, result);

            // for (var index = 0; index < result.Length; index++)
            // {
            //     Debug.Log($"result[{index}].Should().Be(new float2({result[index].x}, {result[index].y}));");
            // }

            result.Length.Should().Be(5);
            result[0].Should().BeApproximately(new(0, 0));
            result[1].Should().BeApproximately(new(2, 0));
            result[2].Should().BeApproximately(new(4, 4));
            result[3].Should().BeApproximately(new(5, 2));
            result[4].Should().BeApproximately(new(5, -1));

            portals.Dispose();
            result.Dispose();
        }
    }
}