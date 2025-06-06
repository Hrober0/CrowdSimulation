using System.Collections.Generic;

namespace HCore.Extensions
{
    public static class HashSetExtensions
    {
        public static void AddRange<T>(this HashSet<T> set, IEnumerable<T> items)
        {
            foreach (var item in items)
            {
                set.Add(item);
            }
        }
        public static List<T> ToList<T>(this HashSet<T> set)
        {
            var list = new List<T>();
            foreach (var item in set)
            {
                list.Add(item);
            }

            return list;
        }
    }

}