using CustomNativeCollections;
using FluentAssertions;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace Tests.EditorTests.CustomNativeCollections
{
    public class NativeSpatialHashTests
    {
        private NativeSpatialHash<int> _hash;

        [SetUp]
        public void Setup()
        {
            _hash = new(capacity: 2, cellSize: 1f, allocator: Allocator.Persistent, capacityAddition: 2);
        }

        [TearDown]
        public void Teardown()
        {
            if (_hash.IsCreated)
            {
                _hash.Dispose();
            }
        }

        [Test]
        public void AddPoint_ShouldBeQueryable()
        {
            _hash.AddPoint(new float2(0.5f, 0.5f), 42);

            using var results = new NativeList<int>(Allocator.Temp);
            _hash.QueryPoint(new float2(0.5f, 0.5f), results);

            results.AsArray().Should().ContainSingle().Which.Should().Be(42);
        }

        [Test]
        public void RemovePoint_ShouldRemoveValue()
        {
            var p = new float2(1.2f, 1.7f);
            _hash.AddPoint(p, 99);

            _hash.RemovePoint(p, 99);

            using var results = new NativeList<int>(Allocator.Temp);
            _hash.QueryPoint(p, results);

            results.AsArray().Should().BeEmpty();
        }

        [Test]
        public void AddAABB_ShouldFillAllCells()
        {
            _hash.AddAABB(new float2(0, 0), new float2(2.1f, 1.1f), 7);

            using var results = new NativeList<int>(Allocator.Temp);
            _hash.QueryAABB(new float2(0, 0), new float2(3, 2), results);

            results.AsArray().Should().NotBeEmpty();
            results.AsArray().Should().OnlyContain(v => v == 7);
        }

        [Test]
        public void RemoveAABB_ShouldClearValues()
        {
            _hash.AddAABB(new float2(0, 0), new float2(2.1f, 1.1f), 11);
            _hash.RemoveAABB(new float2(0, 0), new float2(2.1f, 1.1f), 11);

            using var results = new NativeList<int>(Allocator.Temp);
            _hash.QueryAABB(new float2(0, 0), new float2(3, 2), results);

            results.AsArray().Should().BeEmpty();
        }

        [Test]
        public void EnsureCapacity_ShouldIncreaseCapacity_WhenExceeded()
        {
            int before = _hash.Capacity;

            // Add more than initial capacity
            for (int i = 0; i < before + 5; i++)
            {
                _hash.AddPoint(new float2(i, i), i);
            }

            int after = _hash.Capacity;
            after.Should().BeGreaterThan(before);

            int count = _hash.Count;
            count.Should().Be(before + 5);
        }

        [Test]
        public void MultipleValuesInSameCell_ShouldBeReturned()
        {
            var p = new float2(0.3f, 0.7f);
            _hash.AddPoint(p, 1);
            _hash.AddPoint(p, 2);

            using var results = new NativeList<int>(Allocator.Temp);
            _hash.QueryPoint(p, results);

            results.AsArray().Should().Contain(new[] { 1, 2 });
        }
    }
}