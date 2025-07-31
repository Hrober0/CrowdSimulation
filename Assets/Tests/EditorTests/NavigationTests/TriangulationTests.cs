using System.Collections.Generic;
using andywiecko.BurstTriangulator;
using FluentAssertions;
using HCore.Extensions;
using HCore.Shapes;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Triangle = Navigation.Triangle;

namespace Tests.EditorTests.NavigationTests
{
    public class TriangulationTests
    {
        [Test]
        public void Triangulation_ShouldTriangulateRectangle()
        {
            using var positions = new NativeList<float2>(Allocator.TempJob)
            {
                new(0, 10),
                new(10, 10),
                new(10, 0),
                new(0, 0),
            };
            using var triangulator = new Triangulator<float2>(Allocator.TempJob)
            {
                Input =
                {
                    Positions = positions.AsArray(),
                },
            };

            triangulator.Run();

            var result = GetTriangles(triangulator.Output);
            // DebugResult(result);

            result.Should().BeEquivalentTo(new List<Triangle>()
            {
                new(new(10, 0), new(0, 0), new(0, 10)),
                new(new(0, 10), new(10, 10), new(10, 0)),
            });
        }

        [Test]
        public void Triangulation_ShouldTriangulatePointInRectangle()
        {
            using var positions = new NativeList<float2>(Allocator.TempJob)
            {
                new(0, 10),
                new(10, 10),
                new(10, 0),
                new(0, 0),
                new(3, 5),
            };
            using var triangulator = new Triangulator<float2>(Allocator.TempJob)
            {
                Input =
                {
                    Positions = positions.AsArray(),
                },
            };

            triangulator.Run();

            var result = GetTriangles(triangulator.Output);
            // DebugResult(result);

            result.Should().BeEquivalentTo(new List<Triangle>()
            {
                new(new(3, 5), new(0, 10), new(10, 10)),
                new(new(10, 10), new(10, 0), new(3, 5)),
                new(new(10, 0), new(0, 0), new(3, 5)),
                new(new(3, 5), new(0, 0), new(0, 10)),
            });
        }

        [Test]
        public void Triangulation_ShouldTriangulateTriangleInRectangle()
        {
            using var positions = new NativeList<float2>(Allocator.TempJob)
            {
                new(17.000000f, 0.000000f),
                new(17.000000f, 10.000000f),
                new(4.528213f, 4.100906f),
                new(5.227706f, 3.386267f),
                new(0.000000f, 10.000000f),
                new(0.000000f, 0.000000f),
                new(5.942345f, 4.085761f),
            };
            using var triangulator = new Triangulator<float2>(Allocator.TempJob)
            {
                Input =
                {
                    Positions = positions.AsArray(),
                },
            };

            triangulator.Run();

            var result = GetTriangles(triangulator.Output);
            // DebugResult(result);

            result.Should().HaveCount(8);
        }

        [Test]
        public void Triangulation_ShouldTriangulateHoleInRectangle()
        {
            using var positions = new NativeList<float2>(Allocator.TempJob)
            {
                new(0, 10),
                new(10, 10),
                new(10, 0),
                new(0, 0),
                new(3, 5), // constrain
                new(6, 5), // constrain
                new(5, 7), // constrain
            };
            using var constraintEdges = new NativeList<int>(Allocator.TempJob)
            {
                4, 5,
                5, 6,
                6, 4,
            };
            using var holes = new NativeList<float2>(Allocator.TempJob)
            {
                new(5, 6), // center of triangle
            };
            using var triangulator = new Triangulator<float2>(Allocator.TempJob)
            {
                Input =
                {
                    Positions = positions.AsArray(),
                    ConstraintEdges = constraintEdges.AsArray(),
                    HoleSeeds = holes.AsArray(),
                },
            };

            triangulator.Run();

            var result = GetTriangles(triangulator.Output);
            // DebugResult(result);

            result.Should().BeEquivalentTo(new List<Triangle>()
            {
                new(new(3, 5), new(0, 10), new(5, 7)),
                new(new(5, 7), new(10, 10), new(6, 5)),
                new(new(0, 10), new(10, 10), new(5, 7)),
                new(new(6, 5), new(0, 0), new(3, 5)),
                new(new(3, 5), new(0, 0), new(0, 10)),
                new(new(10, 10), new(10, 0), new(6, 5)),
                new(new(6, 5), new(10, 0), new(0, 0)),
            });
        }


        private static List<Triangle> GetTriangles(OutputData<float2> output)
        {
            List<Triangle> result = new(output.Triangles.Length / 3);
            for (int i = 0; i < output.Triangles.Length; i += 3)
            {
                result.Add(new(
                    output.Positions[output.Triangles[i]],
                    output.Positions[output.Triangles[i + 1]],
                    output.Positions[output.Triangles[i + 2]]
                ));
            }

            return result;
        }

        private static void DebugResult(List<Triangle> result)
        {
            var s = "";
            for (var index = 0; index < result.Count; index++)
            {
                var triangle = result[index];
                triangle.DrawBorder(Color.red, 3);
                Triangle.Center(triangle.A, triangle.B, triangle.C).To3D().DrawPoint(Color.white, 3, .1f);
                s += $"new(new({triangle.A.x},{triangle.A.y}), new({triangle.B.x}, {triangle.B.y}), new({triangle.C.x}, {triangle.C.y})),\n";
            }

            Debug.Log(s);
        }
    }
}