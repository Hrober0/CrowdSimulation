using NUnit.Framework;
using FluentAssertions;
using Navigation;
using Unity.Collections;
using Unity.Mathematics;
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

            var nodes = new NativeArray<NavNode>(2, Allocator.Temp);
            nodes[0] = new NavNode(
                a,
                b,
                c,
                connectionAB: -1,
                connectionBC: 1,
                connectionCA: -1
            );
            nodes[1] = new NavNode(
                b,
                d,
                c,
                connectionAB: -1,
                connectionBC: -1,
                connectionCA: 0
            );

            var start = new float2(0.1f, 0.1f);
            var target = new float2(0.9f, 0.9f);
            NativeList<float2> resultPath = ExecuteJob(start, target, nodes);

            resultPath.Length.Should().Be(2);
            resultPath[0].Should().BeApproximately(start);
            resultPath[1].Should().BeApproximately(target);

            nodes.Dispose();
            resultPath.Dispose();
        }

        [Test]
        public void Execute_ShouldFindPathThroughMultipleTriangles()
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

            var nodes = new NativeArray<NavNode>(4, Allocator.Temp);

            // Triangle 0: ABC
            nodes[0] = new NavNode(
                cornerA: a,
                cornerB: b,
                cornerC: c,
                connectionAB: -1,
                connectionBC: 1,
                connectionCA: -1
            );
            // Triangle 1: CBD
            nodes[1] = new NavNode(
                cornerA: c,
                cornerB: b,
                cornerC: d,
                connectionAB: 0,
                connectionBC: 2,
                connectionCA: -1
            );
            // Triangle 2: BFD
            nodes[2] = new NavNode(
                cornerA: b,
                cornerB: f,
                cornerC: d,
                connectionAB: -1,
                connectionBC: 3,
                connectionCA: 1
            );
            // Triangle 3: DEF
            nodes[3] = new NavNode(
                cornerA: d,
                cornerB: e,
                cornerC: f,
                connectionAB: 1,
                connectionBC: -1,
                connectionCA: 2
            );

            float2 start = nodes[0].Center;
            float2 target = nodes[3].Center;

            NativeList<float2> resultPath = ExecuteJob(start, target, nodes);

            // Log intermediate results
            // Debug.Log($"Path from {start} to {end}");
            // for (int i = 0; i < resultPath.Length; i++)
            // {
            //     Debug.Log($"result[{i}] = {resultPath[i]}");
            // }

            // Assertions
            resultPath.Length.Should().Be(2);
            resultPath[0].Should().BeApproximately(start);
            resultPath[1].Should().BeApproximately(target);

            // Clean up
            nodes.Dispose();
            resultPath.Dispose();
        }

        private static NativeList<float2> ExecuteJob(float2 start, float2 end, NativeArray<NavNode> nodes)
        {
            const float CELL_SIZE = 1f;
            
            using var lookup = new NativeParallelMultiHashMap<int2, int>(2, Allocator.Temp);
            var resultPath = new NativeList<float2>(Allocator.Temp);

            for (int i = 0; i < nodes.Length; i++)
            {
                var cell = (int2)(nodes[i].Center / CELL_SIZE);
                lookup.Add(cell, i);
            }

            var job = new FindPathJob
            {
                StartPos = start,
                StartNodeIndex = GetCellFromWorldPosition(start),
                TargetPos = end,
                TargetNodeIndex = GetCellFromWorldPosition(end),
                SeekerData = new(radius: 0.1f),
                Nodes = nodes,
                ResultPath = resultPath
            };

            job.Execute();

            return resultPath;
            
            int GetCellFromWorldPosition(float2 position)
            {
                var cell = (int2)(position / CELL_SIZE);
                foreach (var index in lookup.GetValuesForKey(cell))
                {
                    NavNode node = nodes[index];
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