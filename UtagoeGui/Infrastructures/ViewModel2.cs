using Livet;
using Livet.Commands;
using Livet.EventListeners;
using System;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace UtagoeGui.Infrastructures
{
    public abstract class ViewModel2 : NotificationObject2, IDisposable
    {
        protected LivetCompositeDisposable CompositeDisposable { get; } = new LivetCompositeDisposable();

        /// <summary>
        /// <see cref="ChangeValueWhenModelPropertyChangeAttribute"/> が指定されたプロパティの変更通知を有効化します。
        /// </summary>
        protected void EnableAutoPropertyChangedEvent(INotifyPropertyChanged model)
        {
            PropertyChangedEventListener listener = null;

            foreach (var prop in this.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                PropertyChangedEventHandler handler = null;
                foreach (var attr in prop.GetCustomAttributes<ChangeValueWhenModelPropertyChangeAttribute>())
                {
                    if (attr.DependentPropertyNames == null) continue;

                    foreach (var propName in attr.DependentPropertyNames)
                    {
                        if (listener == null) listener = new PropertyChangedEventListener(model);
                        if (handler == null) handler = CreateHandler(prop.Name);

                        listener.Add(propName, handler);
                    }
                }
            }

            if (listener != null) this.CompositeDisposable.Add(listener);

            PropertyChangedEventHandler CreateHandler(string propertyName) => (_, __) => this.RaisePropertyChanged(propertyName);
        }

        /// <summary>
        /// <see cref="ChangeCanExecuteWhenModelPropertyChangeAttribute"/> が指定されたコマンドの <see cref="System.Windows.Input.ICommand.CanExecuteChanged"/> を自動的に発生させるようにします。
        /// </summary>
        protected void EnableAutoCanExecuteChangedEvent(INotifyPropertyChanged model)
        {
            PropertyChangedEventListener listener = null;

            foreach (var prop in this.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                PropertyChangedEventHandler handler = null;
                foreach (var attr in prop.GetCustomAttributes<ChangeCanExecuteWhenModelPropertyChangeAttribute>())
                {
                    if (attr.DependentPropertyNames == null) continue;

                    foreach (var propName in attr.DependentPropertyNames)
                    {
                        if (listener == null) listener = new PropertyChangedEventListener(model);

                        if (handler == null)
                        {
                            handler = Expression.Lambda<PropertyChangedEventHandler>(
                                Expression.Call(
                                    Expression.Property(Expression.Constant(this), prop),
                                    s_viewModelCommandRaiseCanExecuteChangedMethodInfo
                                ),
                                Expression.Parameter(typeof(object)),
                                Expression.Parameter(typeof(PropertyChangedEventArgs))
                            ).Compile();
                        }

                        listener.Add(propName, handler);
                    }
                }
            }

            if (listener != null) this.CompositeDisposable.Add(listener);
        }

        private static readonly MethodInfo s_viewModelCommandRaiseCanExecuteChangedMethodInfo = typeof(ViewModelCommand).GetMethod(nameof(ViewModelCommand.RaiseCanExecuteChanged));

        protected static ViewModelCommand CreateCommand(ref ViewModelCommand backingStore, Action execute, Func<bool> canExecute = null)
        {
            if (backingStore == null)
                backingStore = new ViewModelCommand(execute, canExecute);

            return backingStore;
        }

        protected override void RaisePropertyChanged([CallerMemberName] string propertyName = "")
        {
            DispatcherHelper.UIDispatcher.InvokeAsync(() => base.RaisePropertyChanged(propertyName));
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
