using System.Collections.Generic;
using andywiecko.BurstTriangulator;
using andywiecko.BurstTriangulator.LowLevel.Unsafe;
using FluentAssertions;
using HCore.Extensions;
using HCore.Shapes;
using NUnit.Framework;
using Tests.TestsUtilities;
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

        [Test]
        public void Triangulation_ShouldValidateInput()
        {
            // [Triangulator]: ConstraintEdges[6] = (6, 7) = <(7,950962, 5,450962), (8,502167, 6,405676)> and ConstraintEdges[14] = (8, 13) = <(9,450962, 8,049038), (9,002668, 7,272571)> intersect!
            using var positions = new NativeArray<float2>(new float2[] {
                new(7.95096207f, 5.45096207f),
                new(8.50216675f, 6.40567636f),
                new(9.45096207f, 8.04903793f),
                new(9.00266838f, 7.27257061f),
            }, Allocator.Persistent);
            using var constraints = new NativeArray<int>(new[] { 0, 1, 2, 3  }, Allocator.Persistent);
            
            using var outputTriangles = new NativeList<int>(positions.Length * 3, Allocator.Temp);
            using var triangulationStatus = new NativeReference<Status>(Allocator.Temp);
            new UnsafeTriangulator<float2>().Triangulate(
                input: new()
                {
                    Positions = positions,
                    ConstraintEdges = constraints,
                },
                output: new()
                {
                    Triangles = outputTriangles,
                    Status = triangulationStatus,
                },
                args: Args.Default(), 
                allocator: Allocator.Temp
            );
        
            var focusEdges = new List<int2>() { new(6, 7), new(8, 13) };
            for (var index = 0; index < constraints.Length; index+=2)
            {
                var s = constraints[index];
                var e = constraints[index + 1];
                var color = focusEdges.Contains(new(s, e)) ? Color.red : Color.green;
                Debug.DrawLine(math.float3(positions[s], 0), math.float3(positions[e], 0), color, 5f);
            }
        
            triangulationStatus.Value.Should().Be(Status.OK);
        }
        
        // [Test]
        // public void Triangulation_ShouldNotThrowOnSloan()
        // {
        //     // [Triangulator]: Sloan max iterations exceeded! This may suggest that input data is hard to resolve by Sloan's algorithm.
        //     // It usually happens when the scale of the input positions is not uniform. Please try to post-process input data or increase SloanMaxIters value.
        //     using var positions = new NativeArray<float2>(new float2[] {
        //         new(0.292827606f, 0.000265283423f), //
        //         new(0.11535418f, 0.859595299f),  //
        //         new(18f, 0f),
        //         new(0.0410567857f, 0.000269055367f),
        //     }, Allocator.Persistent);
        //     using var constraints = new NativeArray<int>(new[] { 0, 1 }, Allocator.Persistent);
        //     
        //     using var outputTriangles = new NativeList<int>(positions.Length * 3, Allocator.Temp);
        //     using var triangulationStatus = new NativeReference<Status>(Allocator.Temp);
        //     new UnsafeTriangulator<float2>().Triangulate(
        //         input: new()
        //         {
        //             Positions = positions,
        //             ConstraintEdges = constraints,
        //         },
        //         output: new()
        //         {
        //             Triangles = outputTriangles,
        //             Status = triangulationStatus,
        //         },
        //         args: Args.Default(validateInput: true), 
        //         allocator: Allocator.Temp
        //     );
        //
        //     var focusEdges = new List<int2>() { new(0, 1), new(8, 9) };
        //     for (var index = 0; index < constraints.Length; index+=2)
        //     {
        //         var s = constraints[index];
        //         var e = constraints[index + 1];
        //         var color = focusEdges.Contains(new(s, e)) ? Color.red : Color.green;
        //         Debug.DrawLine(math.float3(positions[s], 0), math.float3(positions[e], 0), color, 5f);
        //     }
        //
        //     foreach (var p in positions)
        //     {
        //         p.To3D().DrawPoint(Color.yellow, 5, 0.1f);
        //     }
        //
        //     triangulationStatus.Value.Should().Be(Status.OK);
        // }
        
        
        private static List<Triangle> GetTriangles(andywiecko.BurstTriangulator.OutputData<float2> output)
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