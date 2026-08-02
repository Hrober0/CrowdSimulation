using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// "What is standing on this cell" (design §5). Singleton, maintained by
    /// <c>CellObjectRegistrationSystem</c> alongside the cost it queues into the grid, so the map and the
    /// cost sum are always changed together.
    ///
    /// Several objects may share a cell and all of them are listed - which is what harvest targeting,
    /// selection and land clearing need. Buildings are not in here; they are entities of their own kind
    /// (§6), not cell objects.
    /// </summary>
    public struct CellObjectMap : IComponentData, IDisposable
    {
        private NativeParallelMultiHashMap<int2, Entity> _objectsByCell;

        public CellObjectMap(int capacity, Allocator allocator)
        {
            _objectsByCell = new NativeParallelMultiHashMap<int2, Entity>(capacity, allocator);
        }

        public bool IsCreated => _objectsByCell.IsCreated;

        public bool IsEmpty => _objectsByCell.IsEmpty;

        public int CountAt(int2 cell) => _objectsByCell.CountValuesForKey(cell);

        public bool IsOccupied(int2 cell) => _objectsByCell.ContainsKey(cell);

        public bool TryGetFirst(int2 cell, out Entity entity) =>
            _objectsByCell.TryGetFirstValue(cell, out entity, out _);

        /// <summary>Every object on the cell, in no particular order.</summary>
        public NativeParallelMultiHashMap<int2, Entity>.Enumerator GetObjectsAt(int2 cell) =>
            _objectsByCell.GetValuesForKey(cell);

        public void Add(int2 cell, Entity entity) => _objectsByCell.Add(cell, entity);

        public void Remove(int2 cell, Entity entity) => _objectsByCell.Remove(cell, entity);

        public void Clear() => _objectsByCell.Clear();

        public void Dispose()
        {
            if (_objectsByCell.IsCreated)
            {
                _objectsByCell.Dispose();
            }
        }
    }
}
