using System;

namespace HCore
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class AAssignAttribute : Attribute
    {
        public AAssignAttribute() { }
    }
}