using System.Collections.Generic;

namespace HCore.Extensions
{
    public static class ReadOnlyCollectionsExtensions
    {
        public static bool ContainsV<T>(this IReadOnlyCollection<T> list, T item) where T : struct
        {
            foreach (var s in list)
            {
                if (s.Equals(item))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool Contains<T>(this IReadOnlyCollection<T> list, T item) where T : class
        {
            foreach (var s in list)
            {
                if (s == item)
                {
                    return true;
                }
            }
            return false;
        }

        public static bool CompearContent<T>(this IReadOnlyCollection<T> list, IReadOnlyCollection<T> other)
        {
            var thisSet = new HashSet<T>(list);
            var otherSet = new HashSet<T>(other);

            return thisSet.SetEquals(otherSet);
        }
    }
}