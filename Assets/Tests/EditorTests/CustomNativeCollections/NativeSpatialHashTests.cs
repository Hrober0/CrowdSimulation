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
            _hash = new(capacity: 10, cellSize: 1f, allocator: Allocator.Persistent);
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
        public void AddPoint_ShouldIncreaseCapacity_WhenExceeded()
        {
            int before = _hash.Capacity;

            // Add more than initial capacity
            for (int i = 0; i < before + 1; i++)
            {
                _hash.AddPoint(new float2(i, i), i);
            }

            int after = _hash.Capacity;
            after.Should().BeGreaterThan(before);

            int count = _hash.Count;
            count.Should().Be(before + 1);
        }

        [Test]
        public void QueryPoint_ShouldReturnMultipleValuesInSameCell()
        {
            var p = new float2(0.3f, 0.7f);
            _hash.AddPoint(p, 1);
            _hash.AddPoint(p, 2);

            using var results = new NativeList<int>(Allocator.Temp);
            _hash.QueryPoint(p, results);

            results.AsArray().Should().Contain(new[] { 1, 2 });
        }

        [Test]
        public void RemovePoint_WhenMultipleValuesInSameCell_ShouldRemoveOnlySpecifiedValue()
        {
            var p = new float2(0.3f, 0.7f);

            _hash.AddPoint(p, 1);
            _hash.AddPoint(p, 2);
            _hash.AddPoint(p, 3);

            _hash.RemovePoint(p, 2);

            using var results = new NativeList<int>(Allocator.Temp);
            _hash.QueryPoint(p, results);

            results.AsArray().Should().BeEquivalentTo(new[] { 1, 3 },
                options => options.WithoutStrictOrdering());
        }

        [Test]
        public void RemovePoint_OnNonExistingValue_ShouldNotThrow_AndNotChangeOthers()
        {
            var p = new float2(0.3f, 0.7f);

            _hash.AddPoint(p, 1);
            _hash.AddPoint(p, 3);

            _hash.RemovePoint(p, 999); // not present

            using var results = new NativeList<int>(Allocator.Temp);
            _hash.QueryPoint(p, results);

            results.AsArray().Should().BeEquivalentTo(new[] { 1, 3 },
                options => options.WithoutStrictOrdering());
        }

        [Test]
        public void RemoveAABB_WithOverlappingAABBs_ShouldOnlyRemoveRequestedValues()
        {
            // AABB1 and AABB2 overlap; AABB1 has value 1, AABB2 has value 2
            _hash.AddAABB(new float2(0, 0), new float2(2.1f, 2.1f), 1);
            _hash.AddAABB(new float2(1, 1), new float2(3.1f, 3.1f), 2);

            // Remove only 1
            _hash.RemoveAABB(new float2(0, 0), new float2(2.1f, 2.1f), 1);

            using var results = new NativeList<int>(Allocator.Temp);
            _hash.QueryAABB(new float2(0, 0), new float2(4, 4), results);

            // All remaining must be 2
            results.AsArray().Should().NotBeEmpty();
            results.AsArray().Should().OnlyContain(v => v == 2);
        }

        [Test]
        public void QueryPoint_OnEmptyHash_ShouldReturnEmpty()
        {
            using var results = new NativeList<int>(Allocator.Temp);

            _hash.QueryPoint(new float2(5, 5), results);

            results.AsArray().Should().BeEmpty();
        }

        [Test]
        public void QueryAABB_WhenNoValuesInRange_ShouldReturnEmpty()
        {
            _hash.AddPoint(new float2(100, 100), 5);

            using var results = new NativeList<int>(Allocator.Temp);
            _hash.QueryAABB(new float2(0, 0), new float2(10, 10), results);

            results.AsArray().Should().BeEmpty();
        }

        [Test]
        public void Clear_ShouldRemoveAllValues()
        {
            _hash.AddPoint(new float2(0.5f, 0.5f), 1);
            _hash.AddPoint(new float2(1.5f, 1.5f), 2);
            _hash.AddAABB(new float2(0, 0), new float2(2, 2), 3);

            _hash.Clear();

            using var results = new NativeList<int>(Allocator.Temp);
            _hash.QueryAABB(new float2(0, 0), new float2(3, 3), results);

            _hash.Count.Should().Be(0);
            results.AsArray().Should().BeEmpty();
        }

        [Test]
        public void AddAfterClear_ShouldWorkCorrectly()
        {
            _hash.AddPoint(new float2(0.5f, 0.5f), 1);
            _hash.Clear();

            _hash.AddPoint(new float2(0.5f, 0.5f), 2);

            using var results = new NativeList<int>(Allocator.Temp);
            _hash.QueryPoint(new float2(0.5f, 0.5f), results);

            results.AsArray().Should().ContainSingle().Which.Should().Be(2);
        }

        [Test]
        public void MultipleCells_ShouldNotInterfere()
        {
            var p1 = new float2(0.2f, 0.2f); // cell (0,0)
            var p2 = new float2(3.2f, 0.2f); // cell (3,0)
            var p3 = new float2(0.2f, 4.2f); // cell (0,4)

            _hash.AddPoint(p1, 10);
            _hash.AddPoint(p2, 20);
            _hash.AddPoint(p3, 30);

            using var results1 = new NativeList<int>(Allocator.Temp);
            using var results2 = new NativeList<int>(Allocator.Temp);
            using var results3 = new NativeList<int>(Allocator.Temp);

            _hash.QueryPoint(p1, results1);
            _hash.QueryPoint(p2, results2);
            _hash.QueryPoint(p3, results3);

            results1.AsArray().Should().ContainSingle().Which.Should().Be(10);
            results2.AsArray().Should().ContainSingle().Which.Should().Be(20);
            results3.AsArray().Should().ContainSingle().Which.Should().Be(30);
        }

        [Test]
        public void ForEachInAABB_ShouldIterateAllValuesInArea()
        {
            // Fill a 3x3 area
            _hash.AddAABB(new float2(0, 0), new float2(2.9f, 2.9f), 5);

            var processor = new CollectProcessor
            {
                Collected = new NativeList<int>(Allocator.Temp)
            };

            _hash.ForEachInAABB(new float2(0, 0), new float2(3, 3), ref processor);

            try
            {
                processor.Collected.Length.Should().BeGreaterThan(0);
                processor.Collected.AsArray().Should().OnlyContain(v => v == 5);
            }
            finally
            {
                processor.Collected.Dispose();
            }
        }

        [Test]
        public void MultipleAddRemoveCycles_ShouldReuseInternalSlotsCorrectly()
        {
            var p = new float2(1.1f, 1.1f);

            for (int cycle = 0; cycle < 5; cycle++)
            {
                _hash.AddPoint(p, cycle);

                using var results = new NativeList<int>(Allocator.Temp);
                _hash.QueryPoint(p, results);
                results.AsArray().Should().Contain(cycle);

                _hash.RemovePoint(p, cycle);

                results.Clear();
                _hash.QueryPoint(p, results);
                results.AsArray().Should().BeEmpty();
            }
        }

        [Test]
        public void RemovingLastValueFromCell_ShouldRemoveCellEntryInternally()
        {
            var p = new float2(0.5f, 0.5f);

            _hash.AddPoint(p, 1);
            _hash.RemovePoint(p, 1);

            using var results = new NativeList<int>(Allocator.Temp);
            _hash.QueryPoint(p, results);

            results.AsArray().Should().BeEmpty();

            // Now re-add and ensure it still works (cell re-created)
            _hash.AddPoint(p, 2);
            results.Clear();
            _hash.QueryPoint(p, results);

            results.AsArray().Should().ContainSingle().Which.Should().Be(2);
        }
        
        private struct CollectProcessor : ISpatialQueryProcessor<int>
        {
            public NativeList<int> Collected;

            public void Process(int value)
            {
                Collected.Add(value);
            }
        }
    }
}