using System;

namespace HCore.Extensions
{
    public static class EnumExtensions
    {
        public static int FlagMaxValue<T>(this T e) where T : Enum
        {
            var l = Enum.GetValues(e.GetType()).Length;
            return (1 << l) - 1;
        }
        public static int FixAllFlagToInt<T>(this T e) where T : Enum
        {
            int intE = Convert.ToInt32(e);
            if (intE == -1)
                intE = e.FlagMaxValue();
            return intE;
        }
        public static T FixAllFlagValue<T>(this T e) where T : Enum
        {
            var intE = e.FixAllFlagToInt();
            return (T)Enum.ToObject(typeof(T), intE);
        }
    }
}