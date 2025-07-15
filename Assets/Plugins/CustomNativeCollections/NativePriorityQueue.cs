using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace CustomNativeCollections
{
    /// <summary>
    /// Priority Queue implementation with item data stored in native containers. This version uses an IComparer<T> instead of relying on IComparable<T> being implemented by the stored type.
    /// </summary>
    /// <typeparam name="T"></typeparam>

    [NativeContainer]
    [DebuggerDisplay("Length = {Count}")]
    public struct NativePriorityQueue<T> : IDisposable where T : struct, IComparable<T>
    {
        private NativeArray<T> _heap;
        private NativeReference<int> _count;

        public int Count => _count.Value;

        public NativePriorityQueue(int initialCapacity = 64, Allocator allocator = Allocator.Temp) : this()
        {
            _heap = new NativeArray<T>(initialCapacity, allocator);
            _count = new NativeReference<int>(allocator);
            _count.Value = 0;
        }


        /// <summary>
        /// Disposes of any native memory held by this instance
        /// </summary>
        public void Dispose()
        {
            if (_heap.IsCreated)
            {
                _heap.Dispose();
            }

            if (_count.IsCreated)
            {
                _count.Dispose();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Peek()
        {
            if (_heap.Length == 0)
            {
                throw new InvalidOperationException("Cannot peek at first item when the heap is empty.");
            }

            return _heap[0];
        }

        /// <summary>
        /// Adds a key and value to the heap.
        /// </summary>
        /// <param name="item">The item to add to the heap.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue(T item)
        {
            if (Count >= _heap.Length)
            {
                throw new OverflowException();
            }

            _heap[Count] = item;

            HeapifyUp(Count);

            _count.Value++;
        }

        /// <summary>
        /// Removes and returns the first item in the heap.
        /// </summary>
        /// <returns>The first value in the heap.</returns>
        public T Dequeue()
        {
            if (Count == 0)
            {
                throw new InvalidOperationException("Cannot remove item from an empty heap");
            }

            T v = _heap[0];

            _count.Value--;

            // Copy the last node to the root node
            _heap[0] = _heap[Count];

            // Restore the heap property of the tree
            HeapifyDown(0);

            return v;
        }

        /// <summary>
        /// Returns the raw (not necessarily sorted) contents of the priority queue as a managed array.
        /// </summary>
        /// <returns></returns>
        public T[] ToArray()
        {
            var length = Count;

            var result = new T[length];
            for (int i = 0; i < length; i++)
            {
                result[i] = _heap[i];
            }

            return result;
        }

        private void HeapifyUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) >> 1;
                if (_heap[index].CompareTo(_heap[parent]) >= 0)
                {
                    break;
                }

                (_heap[parent], _heap[index]) = (_heap[index], _heap[parent]);
                index = parent;
            }
        }

        private void HeapifyDown(int index)
        {
            while (true)
            {
                int smallest = index;
                int left = 2 * index + 1;
                int right = 2 * index + 2;

                if (left < Count && _heap[left].CompareTo(_heap[smallest]) < 0)
                {
                    smallest = left;
                }

                if (right < Count && _heap[right].CompareTo(_heap[smallest]) < 0)
                {
                    smallest = right;
                }

                if (smallest == index)
                {
                    break;
                }

                (_heap[index], _heap[smallest]) = (_heap[smallest], _heap[index]);
                index = smallest;
            }
        }
    }
}