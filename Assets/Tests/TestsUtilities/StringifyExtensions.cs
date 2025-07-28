using System.Collections.Generic;
using System.Text;

namespace Tests.TestsUtilities
{
    public static class StringifyExtensions
    {
        public static string Stringify<T>(this IEnumerable<T> enumerable)
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
    }
}