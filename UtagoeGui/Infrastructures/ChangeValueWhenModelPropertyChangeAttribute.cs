using System;

namespace UtagoeGui.Infrastructures
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
    public class ChangeValueWhenModelPropertyChangeAttribute : Attribute
    {
        public string[] DependentPropertyNames { get; }

        public ChangeValueWhenModelPropertyChangeAttribute(params string[] dependentPropertyNames)
        {
            this.DependentPropertyNames = dependentPropertyNames;
        }
    }
}
