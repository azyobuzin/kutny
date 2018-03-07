using System;

namespace Kutny.WpfInfra
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
    public class ChangeCanExecuteWhenThisPropertyChangeAttribute : Attribute
    {
        public string[] DependentPropertyNames { get; }

        public ChangeCanExecuteWhenThisPropertyChangeAttribute(params string[] dependentPropertyNames)
        {
            this.DependentPropertyNames = dependentPropertyNames;
        }
    }
}
