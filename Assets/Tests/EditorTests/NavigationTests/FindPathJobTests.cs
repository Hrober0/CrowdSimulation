using NUnit.Framework;
using FluentAssertions;
using Navigation;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Tests.TestsUtilities;

namespace Tests.EditorTests.NavigationTests
{
    public class FindPathJobTests
    {
        [Test]
        public void PortalConstructed_CCW_KeepsOrder()
        {
            var left = new float2(2, 0);
            var right = new float2(1, -2);

            var portal = FindPathJob.CreatePortal(left, right, new(new(0, 0), new(0, 0)));

            portal.Left.Should().BeApproximately(left);
            portal.Right.Should().BeApproximately(right);
        }

        [Test]
        public void PortalConstructed_CW_FlipsOrder()
        {
            var notRight = new float2(2, 0);
            var notLeft = new float2(1, -2);

            var portal = FindPathJob.CreatePortal(notLeft, notRight, new(new(0, 0), new(0, 0)));

            portal.Left.Should().BeApproximately(notRight);
            portal.Right.Should().BeApproximately(notLeft);
        }

        [Test]
        public void Execute_ShouldFindSimplePath()
        {
            var nodes = new NativeArray<NavNode>(2, Allocator.Temp);
            var lookup = new NativeParallelMultiHashMap<int2, int>(2, Allocator.Temp);
            var resultPath = new NativeList<float2>(Allocator.Temp);

            var a = new float2(0, 0);
            var b = new float2(1, 0);
            var c = new float2(0, 1);
            var d = new float2(1, 1);

            var triangle1 = new NavNode(
                a,
                b,
                c,
                connectionAB: -1,
                connectionAC: -1,
                connectionBC: 1,
                0
            );
            var triangle2 = new NavNode(
                b,
                d,
                c,
                connectionAB: -1,
                connectionAC: 0,
                connectionBC: -1,
                0
            );

            nodes[0] = triangle1;
            nodes[1] = triangle2;

            var cellA = (int2)(triangle1.Center / 1f);
            var cellB = (int2)(triangle2.Center / 1f);
            lookup.Add(cellA, 0);
            lookup.Add(cellB, 1);

            var job = new FindPathJob
            {
                StartPos = triangle1.Center,
                TargetPos = triangle2.Center,
                SeekerData = new PathSeekerData(radius: 0.1f),
                Nodes = nodes,
                NodesPositionLookup = lookup,
                LookupCellSize = 1f,
                ResultPath = resultPath
            };

            job.Execute();

            resultPath.Length.Should().BeGreaterThan(1);
            resultPath[0].Should().BeApproximately(job.StartPos);
            resultPath[^1].Should().BeApproximately(job.TargetPos);

            nodes.Dispose();
            lookup.Dispose();
            resultPath.Dispose();
        }

        [Test]
        public void Execute_ShouldFindPathThroughMultipleTriangles()
        {
            var nodes = new NativeArray<NavNode>(4, Allocator.Temp);
            var lookup = new NativeParallelMultiHashMap<int2, int>(4, Allocator.Temp);
            var resultPath = new NativeList<float2>(Allocator.Temp);

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

            // Triangle 0: ABC
            nodes[0] = new NavNode(
                cornerA: a,
                cornerB: b,
                cornerC: c,
                connectionAB: -1,
                connectionAC: -1,
                connectionBC: 1,
                0
            );
            // Triangle 1: CBD
            nodes[1] = new NavNode(
                cornerA: c,
                cornerB: b,
                cornerC: d,
                connectionAB: 0,
                connectionAC: -1,
                connectionBC: 2,
                0
            );
            // Triangle 2: BFD
            nodes[2] = new NavNode(
                cornerA: b,
                cornerB: f,
                cornerC: d,
                connectionAB: -1,
                connectionAC: 1,
                connectionBC: 3,
                0
            );
            // Triangle 3: DEF
            nodes[3] = new NavNode(
                cornerA: d,
                cornerB: e,
                cornerC: f,
                connectionAB: 1,
                connectionAC: 2,
                connectionBC: -1,
                0
            );

            for (int i = 0; i < nodes.Length; i++)
            {
                var cell = (int2)(nodes[i].Center / 1f);
                lookup.Add(cell, i);
            }

            float2 start = nodes[0].Center;
            float2 end = nodes[3].Center;

            var job = new FindPathJob
            {
                StartPos = start,
                TargetPos = end,
                SeekerData = new(radius: 0.1f),
                Nodes = nodes,
                NodesPositionLookup = lookup,
                LookupCellSize = 1f,
                ResultPath = resultPath
            };

            job.Execute();

            // Log intermediate results
            Debug.Log($"Path from {start} to {end}");
            for (int i = 0; i < resultPath.Length; i++)
            {
                Debug.Log($"result[{i}] = {resultPath[i]}");
            }

            // Assertions
            resultPath.Length.Should().Be(2);
            resultPath[0].Should().BeApproximately(job.StartPos);
            resultPath[1].Should().BeApproximately(job.TargetPos);

            // Clean up
            nodes.Dispose();
            lookup.Dispose();
            resultPath.Dispose();
        }
    }
}