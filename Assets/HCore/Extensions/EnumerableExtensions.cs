using System;
using System.Collections.Generic;
using System.Text;

namespace HCore.Extensions
{
    public static class EnumerableExtensions
    {
        public static string ElementsString<T>(this IEnumerable<T> enumerable)
        {
            var sb = new StringBuilder();
            sb.Append("[");
            var f = false;
            foreach (var item in enumerable)
            {
                if (f)
                {
                    sb.Append(", ");
                }
                else
                {
                    f = true;
                }

                sb.Append(item);
            }
            sb.Append("]");
            return sb.ToString();
        }

        public static void ForEach<T>(this IEnumerable<T> enumerable, Action<T> action)
        {
            foreach (var item in enumerable)
            {
                action(item);
            }
        }

        public static T Find<T>(this IEnumerable<T> enumerable, Predicate<T> predicate)
        {
            foreach (var item in enumerable)
            {
                if (predicate(item))
                {
                    return item;
                }
            }
            return default;
        }

        public static bool Exist<T>(this IEnumerable<T> enumerable, Predicate<T> predicate)
        {
            foreach (var item in enumerable)
            {
                if (predicate(item))
                {
                    return true;
                }
            }
            return false;
        }
    }
}