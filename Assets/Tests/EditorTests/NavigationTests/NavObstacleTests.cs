using FluentAssertions;
using Navigation;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace Tests.EditorTests.NavigationTests
{
    public class NavObstacleTests
    {
        public struct DummyAttributes : INodeAttributes<DummyAttributes>
        {
            public int Id;
            public DummyAttributes Init() => this;
            public DummyAttributes Empty() => throw new System.NotImplementedException();

            public void Merge(DummyAttributes other)
            {
                throw new System.NotImplementedException();
            }
        }
        
        [Test]
        public void AddObstacle_ShouldStoreObstacleAndEdges()
        {
            using var navObstacles = new NavObstacles<DummyAttributes>(chunkSize: 1f);

            using var square = new NativeList<float2>(Allocator.Temp);
            square.Add(new float2(0, 0));
            square.Add(new float2(1, 0));
            square.Add(new float2(1, 1));
            square.Add(new float2(0, 1));

            int id = navObstacles.AddObstacle(square, new DummyAttributes { Id = 42 });

            id.Should().Be(0);
            navObstacles.Obstacles.Length.Should().Be(1);

            var obstacle = navObstacles.Obstacles[0];
            obstacle.Min.Should().Be(new float2(0, 0));
            obstacle.Max.Should().Be(new float2(1, 1));

            // Edges should equal number of vertices
            navObstacles.ObstacleEdges.CountValuesForKey(id).Should().Be(square.Length);

            // Spatial hash should contain triangles
            navObstacles.ObstacleLookup.Count.Should().BeGreaterThan(0);
        }

        [Test]
        public void AddObstacle_WithTooFewVertices_ShouldReturnMinusOne()
        {
            using var navObstacles = new NavObstacles<DummyAttributes>(chunkSize: 1f);

            using var invalid = new NativeList<float2>(Allocator.Temp);
            invalid.Add(new float2(0, 0));

            int id = navObstacles.AddObstacle(invalid, new DummyAttributes());
            id.Should().Be(-1);

            navObstacles.Obstacles.Length.Should().Be(0);
        }

        [Test]
        public void RemoveObstacle_ShouldCleanUpCollections()
        {
            using var navObstacles = new NavObstacles<DummyAttributes>(chunkSize: 1f);

            using var tri = new NativeList<float2>(Allocator.Temp);
            tri.Add(new float2(0, 0));
            tri.Add(new float2(1, 0));
            tri.Add(new float2(0, 1));

            int id = navObstacles.AddObstacle(tri, new DummyAttributes());
            navObstacles.Obstacles.Length.Should().Be(1);

            navObstacles.RemoveObstacle(id);

            navObstacles.Obstacles.Length.Should().Be(0);
            navObstacles.ObstacleEdges.CountValuesForKey(id).Should().Be(0);
            
            navObstacles.ObstacleLookup.Count.Should().Be(0);
        }

        [Test]
        public void CanAddMultipleObstacles()
        {
            using var navObstacles = new NavObstacles<DummyAttributes>(chunkSize: 1f);

            using var square = new NativeList<float2>(Allocator.Temp);
            square.Add(new float2(0, 0));
            square.Add(new float2(1, 0));
            square.Add(new float2(1, 1));
            square.Add(new float2(0, 1));

            using var tri = new NativeList<float2>(Allocator.Temp);
            tri.Add(new float2(2, 0));
            tri.Add(new float2(3, 0));
            tri.Add(new float2(2, 1));

            int id1 = navObstacles.AddObstacle(square, new DummyAttributes());
            int id2 = navObstacles.AddObstacle(tri, new DummyAttributes());

            id1.Should().Be(0);
            id2.Should().Be(1);

            navObstacles.Obstacles.Length.Should().Be(2);

            navObstacles.ObstacleEdges.CountValuesForKey(id1).Should().Be(square.Length);
            navObstacles.ObstacleEdges.CountValuesForKey(id2).Should().Be(tri.Length);
        }
    }
}