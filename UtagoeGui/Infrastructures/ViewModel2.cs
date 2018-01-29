using Livet;
using Livet.EventListeners;
using System;
using System.ComponentModel;
using System.Reflection;

namespace UtagoeGui.Infrastructures
{
    public abstract class ViewModel2 : NotificationObject2, IDisposable
    {
        protected LivetCompositeDisposable CompositeDisposable { get; } = new LivetCompositeDisposable();

        /// <summary>
        /// <see cref="DependencyOnStorePropertyAttributeAttribute"/> が指定されたプロパティの変更通知を有効化します。
        /// </summary>
        protected void EnableAutoPropertyChangedEvent(INotifyPropertyChanged store)
        {
            PropertyChangedEventListener listener = null;

            foreach (var prop in this.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                PropertyChangedEventHandler handler = null;
                foreach (var attr in prop.GetCustomAttributes<DependencyOnStorePropertyAttributeAttribute>())
                {
                    if (listener == null) listener = new PropertyChangedEventListener(store);
                    if (handler == null) handler = CreateHandler(prop.Name);

                    listener.Add(attr.DependentPropertyName, handler);
                }
            }

            if (listener != null) this.CompositeDisposable.Add(listener);

            PropertyChangedEventHandler CreateHandler(string propertyName) => (_, __) => this.RaisePropertyChanged(propertyName);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing) this.CompositeDisposable.Dispose();
        }

        public virtual void Dispose()
        {
            Dispose(true);
        }
    }
}
