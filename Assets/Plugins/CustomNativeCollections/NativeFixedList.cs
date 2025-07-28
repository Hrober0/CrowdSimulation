using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace CustomNativeCollections
{
    /// <summary>
    /// List keep fixed index of items in it
    /// </summary>
    [NativeContainer]
    [DebuggerDisplay("Length = {Length}")]
    public struct NativeFixedList<T> : IEnumerable<T>, IDisposable where T : unmanaged
    {
        private NativeList<T> _list;

        private NativeList<int> _freeIndexes;

        public NativeList<T> DirtyList => _list;
        public int Length => _list.Length - _freeIndexes.Length;

        public NativeFixedList(int defaultCapacity = 100, Allocator allocator = Allocator.Temp) : this()
        {
            _list = new(defaultCapacity, allocator);
            _freeIndexes = new(defaultCapacity, allocator);
        }
        
        public void Dispose()
        {
            if (_list.IsCreated)
            {
                _list.Dispose();
            }

            if (_freeIndexes.IsCreated)
            {
                _freeIndexes.Dispose();
            }
        }

        public int FreeIndex => _freeIndexes.Length > 0 ? _freeIndexes[^1] : _list.Length;

        [BurstCompile]
        public int Add(T item)
        {
            if (_freeIndexes.Length > 0)
            {
                var newIndex = _freeIndexes[^1];
                _freeIndexes.RemoveAt(_freeIndexes.Length - 1);
                _list[newIndex] = item;
                return newIndex;
            }
            else
            {
                _list.Add(item);
                return _list.Length - 1;
            }
        }

        [BurstCompile]
        public void RemoveAt(int index)
        {
            _freeIndexes.Add(index);
        }
        
        public T this[int index]
        {
            get => _list[index];
            set => _list[index] = value;
        }

        public Enumerator GetEnumerator() => new Enumerator(_list, _freeIndexes);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator : IEnumerator<T>
        {
            private readonly NativeList<T> _list;
            private readonly NativeList<int> _freeSet;
            private int _index;

            public Enumerator(NativeList<T> list, NativeList<int> freeSet)
            {
                _list = list;
                _freeSet = freeSet;
                _index = -1;
                Current = default;
            }

            public T Current { get; private set; }

            object IEnumerator.Current => Current;

            [BurstCompile]
            public bool MoveNext()
            {
                while (++_index < _list.Length)
                {
                    if (!_freeSet.Contains(_index))
                    {
                        Current = _list[_index];
                        return true;
                    }
                }

                return false;
            }

            public void Reset() => _index = -1;

            public void Dispose()
            {
            }
        }
    }
}