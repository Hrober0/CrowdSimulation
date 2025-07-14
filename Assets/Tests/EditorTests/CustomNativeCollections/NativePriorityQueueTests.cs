using System;
using System.Collections.Generic;
using CustomNativeCollections;
using FluentAssertions;
using NUnit.Framework;
using Unity.Collections;

namespace Tests.EditorTests.CustomNativeCollections
{
    public class NativePriorityQueueTests
    {
        private struct IntComparer : IComparer<int>
        {
            public int Compare(int x, int y) => x.CompareTo(y);
        }

        [Test]
        public void EnqueueAndDequeue_ShouldMaintainHeapOrder()
        {
            var queue = new NativePriorityQueue<int>(new IntComparer(), 10, Allocator.Temp);

            queue.Enqueue(5);
            queue.Enqueue(3);
            queue.Enqueue(8);
            queue.Enqueue(1);

            queue.Count.Should().Be(4);

            queue.Dequeue().Should().Be(1);
            queue.Dequeue().Should().Be(3);
            queue.Dequeue().Should().Be(5);
            queue.Dequeue().Should().Be(8);

            queue.Count.Should().Be(0);

            queue.Dispose();
        }

        [Test]
        public void Peek_ShouldReturnMinElementWithoutRemoving()
        {
            var queue = new NativePriorityQueue<int>(new IntComparer(), 5, Allocator.Temp);

            queue.Enqueue(10);
            queue.Enqueue(2);
            queue.Enqueue(5);

            queue.Peek().Should().Be(2);
            queue.Count.Should().Be(3);

            queue.Dequeue().Should().Be(2);
            queue.Peek().Should().Be(5);

            queue.Dispose();
        }

        [Test]
        public void Dequeue_OnEmptyQueue_ShouldThrow()
        {
            var queue = new NativePriorityQueue<int>(new IntComparer(), 5, Allocator.Temp);

            FluentActions.Invoking(() => queue.Dequeue())
                         .Should().Throw<InvalidOperationException>();

            queue.Dispose();
        }

        [Test]
        public void Enqueue_BeyondCapacity_ShouldThrow()
        {
            var queue = new NativePriorityQueue<int>(new IntComparer(), 2, Allocator.Temp);

            queue.Enqueue(1);
            queue.Enqueue(2);

            FluentActions.Invoking(() => queue.Enqueue(3))
                         .Should().Throw<OverflowException>();

            queue.Dispose();
        }

        [Test]
        public void ToArray_ShouldReturnAllElements()
        {
            var queue = new NativePriorityQueue<int>(new IntComparer(), 5, Allocator.Temp);

            queue.Enqueue(3);
            queue.Enqueue(1);
            queue.Enqueue(2);

            var arr = queue.ToArray();
            arr.Should().Contain(new[] { 1, 2, 3 }); // Order is not guaranteed in array
            arr.Length.Should().Be(3);

            queue.Dispose();
        }
    }
}