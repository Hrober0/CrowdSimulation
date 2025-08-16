using CustomNativeCollections;
using FluentAssertions;
using NUnit.Framework;
using Unity.Collections;

namespace Tests.EditorTests.CustomNativeCollections
{
    public class NativeSortTests
    {
        [Test]
        public void QuickSort_ShouldSortIntegersAscending()
        {
            using var array = new NativeArray<int>(new[] { 5, 1, 4, 2, 3 }, Allocator.Temp);

            NativeSort.QuickSort(array);

            array.ToArray().Should().ContainInOrder(1, 2, 3, 4, 5);
        }

        [Test]
        public void QuickSort_ShouldHandleAlreadySortedArray()
        {
            using var array = new NativeArray<int>(new[] { 1, 2, 3, 4, 5 }, Allocator.Temp);

            NativeSort.QuickSort(array);

            array.ToArray().Should().ContainInOrder(1, 2, 3, 4, 5);
        }

        [Test]
        public void QuickSort_ShouldHandleArrayWithDuplicates()
        {
            using var array = new NativeArray<int>(new[] { 3, 1, 2, 1, 3 }, Allocator.Temp);

            NativeSort.QuickSort(array);

            array.ToArray().Should().ContainInOrder(1, 1, 2, 3, 3);
        }

        [Test]
        public void QuickSort_ShouldHandleEmptyArray()
        {
            using var array = new NativeArray<int>(0, Allocator.Temp);

            NativeSort.QuickSort(array);

            array.Length.Should().Be(0);
        }

        [Test]
        public void QuickSort_ShouldHandleSingleElementArray()
        {
            using var array = new NativeArray<int>(new[] { 42 }, Allocator.Temp);

            NativeSort.QuickSort(array);

            array.ToArray().Should().ContainSingle().Which.Should().Be(42);
        }
    }
}