using UnityEngine;

namespace HCore
{
    [System.Serializable]
    public struct InterfaceField<T> where T : class
    {
        [SerializeField] private Object objectRef;

        public readonly bool HasValue  => objectRef != null;
        public readonly T Value => objectRef as T;

        public T CachedValue { get; private set; }
        public void CacheValue() => CachedValue = Value;


        public static implicit operator T (InterfaceField<T> field) => field.Value;
    }
}