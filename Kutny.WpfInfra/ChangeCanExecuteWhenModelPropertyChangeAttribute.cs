using System;

namespace Kutny.WpfInfra
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
    public class ChangeCanExecuteWhenModelPropertyChangeAttribute : Attribute
    {
        public string[] DependentPropertyNames { get; }

        public ChangeCanExecuteWhenModelPropertyChangeAttribute(params string[] dependentPropertyNames)
        {
            this.DependentPropertyNames = dependentPropertyNames;
        }
    }
}
