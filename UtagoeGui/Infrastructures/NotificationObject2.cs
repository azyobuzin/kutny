using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Livet;

namespace UtagoeGui.Infrastructures
{
    public abstract class NotificationObject2 : NotificationObject
    {
        protected void Set<T>(ref T backingStore, T value, Action<T> actionIfChanged = null, [CallerMemberName] string propertyName = null)
        {
            if (string.IsNullOrEmpty(propertyName)) throw new ArgumentNullException(nameof(propertyName));

            if (!EqualityComparer<T>.Default.Equals(backingStore, value))
            {
                backingStore = value;
                this.RaisePropertyChanged(propertyName);
                actionIfChanged?.Invoke(value);
            }
        }
    }
}
