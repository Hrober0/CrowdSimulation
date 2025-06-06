using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace HCore.Extensions
{
    public static class DictionaryExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TValue GetOrAddDefault<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, System.Func<TValue> defaultValue)
        {
            if (dictionary.TryGetValue(key, out var value))
            {
                return value;
            }

            value = defaultValue();
            dictionary.Add(key, value);
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddToInnerCollection<TKey, TValue, TCollection>(this IDictionary<TKey, TCollection> dictionary, TKey key, TValue value) where TCollection : ICollection<TValue>, new()
        {
            if (dictionary.TryGetValue(key, out var collection))
            {
                collection.Add(value);
            }
            else
            {
                dictionary.Add(key, new() { value });
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RemoveFromInnerCollection<TKey, TValue, TCollection>(this IDictionary<TKey, TCollection> dictionary, TKey key, TValue value, bool removeEmptyKey = false) where TCollection : ICollection<TValue>
        {
            if (!dictionary.TryGetValue(key, out var list))
            {
                return false;
            }

            bool removed = list.Remove(value);
            if (removed && removeEmptyKey && list.Count == 0)
            {
                dictionary.Remove(key);
            }
            return removed;

        }
    }
}