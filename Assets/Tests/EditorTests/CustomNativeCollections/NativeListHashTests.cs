using System;
using CustomNativeCollections;
using FluentAssertions;
using NUnit.Framework;
using Unity.Collections;

namespace Tests.EditorTests.CustomNativeCollections
{
    public class NativeListHashTests
    {
        [Test]
        public void AddOrGetId_ShouldReturnSameId_ForSameListRegardlessOfOrder()
        {
            using var listHash = new NativeListHash<int>(16, Allocator.Temp);

            using var a = new NativeArray<int>(new[] { 1, 2, 3 }, Allocator.Temp);
            using var b = new NativeArray<int>(new[] { 3, 1, 2 }, Allocator.Temp);

            int id1 = listHash.AddOrGetId(a);
            int id2 = listHash.AddOrGetId(b);

            id1.Should().Be(id2); // order-independent
            listHash.Length.Should().Be(3);
        }

        [Test]
        public void AddOrGetId_ShouldReturnDifferentIds_ForDifferentLists()
        {
            using var listHash = new NativeListHash<int>(16, Allocator.Temp);

            using var a = new NativeArray<int>(new[] { 1, 2, 3 }, Allocator.Temp);
            using var b = new NativeArray<int>(new[] { 1, 2, 4 }, Allocator.Temp);

            int id1 = listHash.AddOrGetId(a);
            int id2 = listHash.AddOrGetId(b);

            id1.Should().NotBe(id2);
        }
        
        [Test]
        public void AddOrGetId_ShouldIncreaseCapacity_WhenExceedsCapacity()
        {
            using var listHash = new NativeListHash<int>(1, Allocator.Temp);

            using var a = new NativeArray<int>(new[] { 1, 2, 3 }, Allocator.Temp);
            using var b = new NativeArray<int>(new[] { 2, 3, 4 }, Allocator.Temp);

            listHash.AddOrGetId(a);
            listHash.AddOrGetId(b);
            
            listHash.Length.Should().Be(6);
        }

        [Test]
        public void TryGetId_ShouldReturnCorrectId_WithoutAdding()
        {
            using var listHash = new NativeListHash<int>(16, Allocator.Temp);

            using var a = new NativeArray<int>(new[] { 5, 6, 7 }, Allocator.Temp);
            using var b = new NativeArray<int>(new[] { 7, 5, 6 }, Allocator.Temp);
            using var c = new NativeArray<int>(new[] { 8, 9 }, Allocator.Temp);

            int idAdded = listHash.AddOrGetId(a);

            listHash.TryGetId(b, out int idFound).Should().BeTrue();
            listHash.TryGetId(c, out int idMissing).Should().BeFalse();

            idFound.Should().Be(idAdded);
            idMissing.Should().Be(-1);
            listHash.Length.Should().Be(3);
        }

        [Test]
        public void AddOrGetId_ShouldHandleEmptyList()
        {
            using var listHash = new NativeListHash<int>(16, Allocator.Temp);

            using var empty = new NativeArray<int>(0, Allocator.Temp);
            int id = listHash.AddOrGetId(empty);

            id.Should().Be(0);
            listHash.TryGetId(empty, out var idFound).Should().BeTrue();
            idFound.Should().Be(id);
            listHash.Length.Should().Be(0);
        }

        [Test]
        public void AddOrGetId_ShouldHandleSingleElementLists()
        {
            using var listHash = new NativeListHash<int>(16, Allocator.Temp);

            using var a = new NativeArray<int>(new[] { 42 }, Allocator.Temp);
            using var b = new NativeArray<int>(new[] { 42 }, Allocator.Temp);
            using var c = new NativeArray<int>(new[] { 99 }, Allocator.Temp);

            int idA = listHash.AddOrGetId(a);
            int idB = listHash.AddOrGetId(b);
            int idC = listHash.AddOrGetId(c);

            idA.Should().Be(idB);
            idC.Should().NotBe(idA);
            listHash.Length.Should().Be(2);
        }
        
        [Test]
        public void GetElements_ShouldReturnSortedElements_ForValidId()
        {
            using var listHash = new NativeListHash<int>(10, Allocator.Temp);

            using var arr1 = new NativeArray<int>(new[] { 3, 1, 2 }, Allocator.Temp);
            int id1 = listHash.AddOrGetId(arr1);

            using var arr2 = new NativeArray<int>(new[] { 10, 5 }, Allocator.Temp);
            int id2 = listHash.AddOrGetId(arr2);

            // Act
            using var elems1 = listHash.GetElements(id1, Allocator.Temp);
            using var elems2 = listHash.GetElements(id2, Allocator.Temp);

            // Assert
            elems1.ToArray().Should().Equal(1, 2, 3); // sorted order
            elems2.ToArray().Should().Equal(5, 10);   // sorted order
            listHash.Length.Should().Be(5);
        }

        [Test]
        public void GetElements_ShouldThrow_WhenIdIsOutOfRange()
        {
            using var hash = new NativeListHash<int>(5, Allocator.Temp);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                using var _ = hash.GetElements(999, Allocator.Temp);
            });
        }
    }
}