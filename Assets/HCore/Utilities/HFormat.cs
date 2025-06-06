using System;
using System.Text;
using UnityEngine;

namespace HCore
{
    public static class HFormat
    {
        public static string GetTypeName(Type type, bool showDeclarationClass = true)
        {
            if (type == null)
                return "<null>";

            var shortName = type.Name;

            if (showDeclarationClass)
            {
                var longName = type.ToString();
                var subTypeIndex = longName.IndexOf('+');
                var genericIndexStart = longName.IndexOf('[');
                if (subTypeIndex > 0 && (genericIndexStart == -1 || subTypeIndex < genericIndexStart))
                {
                    var subTypeStartIndex = longName.LastIndexOf('.') + 1;
                    if (subTypeStartIndex < subTypeIndex)
                    {
                        var declaredClassName = longName.Substring(subTypeStartIndex, subTypeIndex - subTypeStartIndex);
                        shortName = $"{declaredClassName}.{shortName}";
                    }
                }
            }

            if (!type.IsGenericType)
                return shortName;

            var genericArguments = type.GetGenericArguments();
            var sb = new StringBuilder();
            sb.Append(shortName.Split('`')[0]);
            sb.Append('<');
            for (var i = 0; i < genericArguments.Length; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(GetTypeName(genericArguments[i]));
            }
            sb.Append('>');
            return sb.ToString();
        }

        public static string GetNameFromPath(string path)
        {
            var lastDict = path.Split('/')[^1];
            var extSplit = lastDict.Split('.');
            var name = extSplit.Length > 1 ? extSplit[^2] : lastDict;
            return name;
        }
    }
}