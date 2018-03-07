using System;

namespace Kutny.WpfInfra
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class ValidationMethodAttribute : Attribute
    {
        public string ValidationMethodName { get; }

        public ValidationMethodAttribute(string validationMethodName)
        {
            this.ValidationMethodName = validationMethodName;
        }
    }
}
