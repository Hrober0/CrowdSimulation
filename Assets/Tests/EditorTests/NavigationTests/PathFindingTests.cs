using FluentAssertions;
using HCore.Extensions;
using Navigation;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Tests.TestsUtilities;

namespace Tests.EditorTests.NavigationTests
{
    public class PathFindingTests
    {
        [Test]
        public void FunnelPath_StraightLine_ShouldReturnSingleStraightSegment()
        {
            var portals = new NativeList<Portal>(Allocator.Temp)
            {
                new(new(0, 1), new(1, 1))
            };
            var result = new NativeList<float2>(Allocator.Temp);

            var start = new float2(0, 0);
            var end = new float2(0.5f, 2);

            PathFinding.FunnelPath(start, end, portals.AsArray(), result);

            result.Length.Should().BeGreaterOrEqualTo(2);
            result[0].Should().BeApproximately(start);
            result[^1].Should().BeApproximately(end);

            portals.Dispose();
            result.Dispose();
        }

        [Test]
        public void FunnelPath_NarrowTurns_ShouldFollowPortalsCorrectly()
        {
            var portals = new NativeList<Portal>(Allocator.Temp)
            {
                new(new(2, 0), new(1, -2)),// /
                new(new(2, 2), new(4, 2)), // _
                new(new(3, 6), new(4, 4)), // \
                new(new(6, 6), new(5, 2)), // /
            };
            var result = new NativeList<float2>(Allocator.Temp);

            //   Y ↑
            //   6 |                     \          /
            //   5 |                      \        /
            //   4 |                       \      /
            //   3 |                             /                       
            //   2 |              ----------    /
            //   1 |       
            //   0 |  s         /                         
            //  -1 |          /            e          
            //  -2 |        /
            //     +----------------------------------------→ X
            //         0    1    2    3    4    5    6

            var start = new float2(0, 0);
            var end = new float2(4, -1);

            PathFinding.FunnelPath(start, end, portals.AsArray(), result);

            // for (var index = 0; index < result.Length; index++)
            // {
            //     Debug.Log($"result[{index}].Should().Be(new float2({result[index].x}, {result[index].y}));");
            // }

            result.Length.Should().Be(5);
            result[0].Should().BeApproximately(new(0, 0));
            result[1].Should().BeApproximately(new(2, 0));
            result[2].Should().BeApproximately(new(4, 4));
            result[3].Should().BeApproximately(new(5, 2));
            result[4].Should().BeApproximately(new(4, -1));

            portals.Dispose();
            result.Dispose();
        }
        
        [Test]
        public void CreatePortal_CCW_KeepsOrder()
        {
            var left = new float2(2, 0);
            var right = new float2(1, -2);

            var portal = PathFinding.CreatePortal(left, right, new(new(0, 0), new(0, 0)));

            portal.Left.Should().BeApproximately(left);
            portal.Right.Should().BeApproximately(right);
        }

        [Test]
        public void CreatePortal_CW_FlipsOrder()
        {
            var notRight = new float2(2, 0);
            var notLeft = new float2(1, -2);

            var portal = PathFinding.CreatePortal(notLeft, notRight, new(new(0, 0), new(0, 0)));

            portal.Left.Should().BeApproximately(notRight);
            portal.Right.Should().BeApproximately(notLeft);
        }
        
        [Test]
        public void FindPath_ShouldConnectTwoNodes()
        {
            /*
                Geometry:
                Triangle layout (top view):
              1 |  C_____D
                |  |\    |
            0.5 |  |  \  |
                |  |____\|
              0 |  A     B
                +----------------------→ X
                   0     1     2    3

                Triangle 0: ABC
                Triangle 1: BDC

                Path should go from t0 (0.1, 0.1) to t1 (0.9, 0.9)
            */

            var a = new float2(0, 0);
            var b = new float2(1, 0);
            var c = new float2(0, 1);
            var d = new float2(1, 1);

            var nodes = new NativeArray<NavNode<IdAttribute>>(2, Allocator.Temp);
            nodes[0] = new(
                a,
                b,
                c,
                connectionAB: -1,
                connectionBC: 1,
                connectionCA: -1,
                new()
            );
            nodes[1] = new(
                b,
                d,
                c,
                connectionAB: -1,
                connectionBC: -1,
                connectionCA: 0,
                new()
            );

            var start = new float2(0.1f, 0.1f);
            var target = new float2(0.9f, 0.9f);
            NativeList<Portal> resultPath = FindPath(start, target, nodes);

            resultPath.Length.Should().Be(1);
            resultPath[0].Left.Should().BeApproximately(new(0, 1));
            resultPath[0].Right.Should().BeApproximately(new(1, 0));

            nodes.Dispose();
            resultPath.Dispose();
        }

        [Test]
        public void FindPath_ShouldFindPathThroughMultipleNodes()
        {
            /*
                Geometry:
                Triangle layout (top view):
              1 |     C_____D_____E
                |    /\    /\    /
            0.5 |   /  \  /  \  /
                |  /____\/____\/
              0 |  A     B     F
                +----------------------→ X
                   0     1     2    3

                Triangle 0: ABC
                Triangle 1: CBD
                Triangle 2: BFD
                Triangle 3: DEF

                Path should go from t0 (0.5, 0.6) to t3 (2, 0.6)
            */

            float2 a = new(0, 0);
            float2 b = new(1, 0);
            float2 c = new(0.5f, 1);
            float2 d = new(1.5f, 1);
            float2 e = new(2.5f, 1);
            float2 f = new(2f, 0);

            var nodes = new NativeArray<NavNode<IdAttribute>>(4, Allocator.Temp);

            // Triangle 0: ABC
            nodes[0] = new(
                cornerA: a,
                cornerB: b,
                cornerC: c,
                connectionAB: -1,
                connectionBC: 1,
                connectionCA: -1,
                new()
            );
            // Triangle 1: CBD
            nodes[1] = new(
                cornerA: c,
                cornerB: b,
                cornerC: d,
                connectionAB: 0,
                connectionBC: 2,
                connectionCA: -1,
                new()
            );
            // Triangle 2: BFD
            nodes[2] = new(
                cornerA: b,
                cornerB: f,
                cornerC: d,
                connectionAB: -1,
                connectionBC: 3,
                connectionCA: 1,
                new()
            );
            // Triangle 3: DEF
            nodes[3] = new(
                cornerA: d,
                cornerB: e,
                cornerC: f,
                connectionAB: 1,
                connectionBC: -1,
                connectionCA: 2,
                new()
            );

            float2 start = nodes[0].Center;
            float2 target = nodes[3].Center;

            NativeList<Portal> resultPath = FindPath(start, target, nodes);

            // Assertions
            resultPath.Length.Should().Be(3);
            resultPath[0].Left.Should().BeApproximately(new(0.5f, 1));
            resultPath[0].Right.Should().BeApproximately(new(1, 0));
            resultPath[1].Left.Should().BeApproximately(new(1.5f, 1));
            resultPath[1].Right.Should().BeApproximately(new(1, 0));
            resultPath[2].Left.Should().BeApproximately(new(1.5f, 1));
            resultPath[2].Right.Should().BeApproximately(new(2, 0));

            // Clean up
            nodes.Dispose();
            resultPath.Dispose();
        }

        [Test]
        public void ComputeGuidanceVector_ShouldReturnStraightLine_WhenAgentIsInCenter()
        {
            var vector = ComputeGuidanceVector(new(0, 0), new(new(-1, 1), new(1, 1)), new(new(-1, 2), new(1, 2)));
            vector.Should().BeApproximately(new(0, 1));
            vector.ToAngleT0().Should().BeApproximately(0, .01f);
        }
        
        [Test]
        public void ComputeGuidanceVector_ShouldReturnGuideToCorner_WhenAgentIsOutsidePortal_OnTheLeft()
        {
            var vector = ComputeGuidanceVector(new(-2, 0), new(new(-1, 1), new(1, 1)), new(new(-1, 2), new(1, 2)));
            vector.ToAngleT0().Should().BeLessThan(45);
        }
        
        [Test]
        public void ComputeGuidanceVector_ShouldReturnGuideToCorner_WhenAgentIsOutsidePortal_OnTheRight()
        {
            var vector = ComputeGuidanceVector(new(5, .7f), new(new(-1, 1), new(1, 1)), new(new(-1, 2), new(1, 2)));
            vector.ToAngleT0().Should().BeGreaterThan(-45);
        }
        
        [Test]
        public void ComputeGuidanceVector_ShouldReturnGuideToNextPortal_WhenAgentIsOutsidePortal_OnTheLeft()
        {
            var vector = ComputeGuidanceVector(new(-2, 0), new(new(-1, 1), new(1, 1)), new(new(-1, 4), new(1, 1)));
            vector.ToAngleT0().Should().BeLessThan(45);
        }
        
        [Test]
        public void ComputeGuidanceVector_ShouldReturnGuideToNextPortal_WhenAgentIsInsidePortal()
        {
            var vector = ComputeGuidanceVector(new(.7f, 0), new(new(-1, 1), new(1, 1)), new(new(-1, 4), new(1, 1)));
            vector.ToAngleT0().Should().BeGreaterThan(315);
        }
        
        [Test]
        public void ComputeGuidanceVector_ShouldReturnGuideToNextPortal_WhenAgentIsOutsidePortal_OnTheLeft_WhenNextPortalInTiltedLeft()
        {
            var vector = ComputeGuidanceVector(new(-2, 0), new(new(-1, 1), new(1, 1)), new(new(-1, 1), new(1, 4)));
            vector.ToAngleT0().Should().BeLessThan(45);
        }
        
        [Test]
        public void ComputeGuidanceVector_ShouldReturnGuideToNextPortal_WhenAgentIsInsidePortal_WhenNextPortalInTiltedLeft()
        {
            var vector = ComputeGuidanceVector(new(.7f, 0), new(new(-1, 1), new(1, 1)), new(new(-1, 1), new(1, 4)));
            vector.ToAngleT0().Should().BeGreaterThan(315);
        }
        
        [Test]
        public void ComputeGuidanceVector_ShouldReturnGuideToFirstPortal_WhenAgentAfterPortal()
        {
            var vector = ComputeGuidanceVector(new(1.7f, 2), new(new(-1, 1), new(1, 1)), new(new(-1, 4), new(1, 1)));
            vector.ToAngleT0().Should().BeLessThan(270);
        }

        [Test]
        public void ComputeGuidanceVector_ShouldReturnGuideToFirstPortal_WhenAgentIsCloseToPortal()
        {
            var vector = ComputeGuidanceVector(new(2f, .9f), new(new(-1, 1), new(1, 1)), new(new(-1, 4), new(1, 1)));
            vector.ToAngleT0().Should().BeLessThan(315);
            vector.ToAngleT0().Should().BeGreaterThan(270);
        }
        
        private static float2 ComputeGuidanceVector(float2 agentPosition, Portal portal, Portal nextPortal, float portalEdgeBias = 0.3f)
        {
            var result = PathFinding.ComputeGuidanceVector(agentPosition, portal, nextPortal.Center, portalEdgeBias);
            
            DebugUtils.Draw(portal.Left, portal.Right, Color.yellow, 5);
            DebugUtils.Draw(nextPortal.Left, nextPortal.Right, Color.magenta, 5);
            agentPosition.To3D().DrawPoint(Color.red, 5, .3f);
            DebugUtils.Draw(agentPosition, agentPosition + result, Color.green, 5);
                
            return result;
        }
        private static NativeList<Portal> FindPath(float2 start, float2 target, NativeArray<NavNode<IdAttribute>> nodes)
        {
            const float CELL_SIZE = 1f;

            using var lookup = new NativeParallelMultiHashMap<int2, int>(2, Allocator.Temp);
            var portals = new NativeList<Portal>(Allocator.Temp);

            for (int i = 0; i < nodes.Length; i++)
            {
                var cell = (int2)(nodes[i].Center / CELL_SIZE);
                lookup.Add(cell, i);
            }

            PathFinding.FindPath(
                start,
                GetCellFromWorldPosition(start),
                target,
                GetCellFromWorldPosition(target),
                nodes,
                new SamplePathSeeker(),
                portals
                );

            start.To3D().DrawPoint(Color.green, 5, .3f);
            target.To3D().DrawPoint(Color.red, 5, .3f);
            foreach (var portal in portals.AsArray())
            {
                DebugUtils.Draw(portal.Left, portal.Right, Color.yellow, 5);
            }

            return portals;

            int GetCellFromWorldPosition(float2 position)
            {
                var cell = (int2)(position / CELL_SIZE);
                foreach (var index in lookup.GetValuesForKey(cell))
                {
                    NavNode<IdAttribute> node = nodes[index];
                    if (Triangle.PointIn(position, node.CornerA, node.CornerB, node.CornerC))
                    {
                        return index;
                    }
                }

                return NavNode.NULL_INDEX;
            }
        }
    }
}