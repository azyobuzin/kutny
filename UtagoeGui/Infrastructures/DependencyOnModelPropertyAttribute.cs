using System;

namespace UtagoeGui.Infrastructures
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
    public class DependencyOnModelPropertyAttribute : Attribute
    {
        public string DependentPropertyName { get; }

        public DependencyOnModelPropertyAttribute(string dependentPropertyName)
        {
            this.DependentPropertyName = dependentPropertyName;
        }
    }
}
