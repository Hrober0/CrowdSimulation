using System.Reflection;
using FluentAssertions;

namespace Tests.TestsUtilities
{
    public static class ReflectionUtilities
    {
        public static T GetFieldValue<T>(object obj, string fieldName, BindingFlags bindingFlags = BindingFlags.NonPublic | BindingFlags.Instance)
        {
            var field = obj.GetType().GetField(fieldName, bindingFlags);
            field.Should().NotBeNull($"field of name {fieldName} should exist in type {obj.GetType().Name}");
            var value = field!.GetValue(obj);
            value.Should().BeAssignableTo(typeof(T));
            return (T)value;
        }
    }
}