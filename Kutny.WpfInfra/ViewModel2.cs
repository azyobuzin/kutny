using System;
using System.Collections;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Livet;
using Livet.Commands;
using Livet.EventListeners;
using Livet.EventListeners.WeakEvents;

namespace Kutny.WpfInfra
{
    public abstract class ViewModel2 : NotificationObject2, IDisposable, INotifyDataErrorInfo
    {
        protected LivetCompositeDisposable CompositeDisposable { get; } = new LivetCompositeDisposable();

        /// <summary>
        /// <see cref="ChangeValueWhenModelPropertyChangeAttribute"/> が指定されたプロパティの変更通知を有効化します。
        /// </summary>
        protected void EnableAutoPropertyChangedEvent(INotifyPropertyChanged model)
        {
            PropertyChangedWeakEventListener listener = null;

            foreach (var prop in this.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                PropertyChangedEventHandler handler = null;
                foreach (var attr in prop.GetCustomAttributes<ChangeValueWhenModelPropertyChangeAttribute>())
                {
                    if (attr.DependentPropertyNames == null) continue;

                    foreach (var propName in attr.DependentPropertyNames)
                    {
                        if (listener == null) listener = new PropertyChangedWeakEventListener(model);
                        if (handler == null) handler = CreateHandler(prop.Name);

                        listener.Add(propName, handler);
                    }
                }
            }

            if (listener != null) this.CompositeDisposable.Add(listener);

            PropertyChangedEventHandler CreateHandler(string propertyName) => (_, __) => this.RaisePropertyChanged(propertyName);
        }

        /// <summary>
        /// <see cref="ChangeCanExecuteWhenModelPropertyChangeAttribute"/> および <see cref="ChangeCanExecuteWhenThisPropertyChangeAttribute"/> が指定されたコマンドの <see cref="System.Windows.Input.ICommand.CanExecuteChanged"/> を自動的に発生させるようにします。
        /// </summary>
        protected void EnableAutoCanExecuteChangedEvent(INotifyPropertyChanged model = null)
        {
            PropertyChangedEventListener thisListener = null;
            PropertyChangedWeakEventListener modelListener = null;

            foreach (var prop in this.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                PropertyChangedEventHandler handler = null;

                foreach (var attr in prop.GetCustomAttributes())
                {
                    if (attr is ChangeCanExecuteWhenThisPropertyChangeAttribute thisAttr)
                    {
                        if (thisAttr.DependentPropertyNames == null) continue;

                        foreach (var propName in thisAttr.DependentPropertyNames)
                        {
                            if (thisListener == null) thisListener = new PropertyChangedEventListener(this);
                            if (handler == null) handler = CreateHandler(prop);

                            thisListener.Add(propName, handler);
                        }
                    }
                    else if (attr is ChangeCanExecuteWhenModelPropertyChangeAttribute modelAttr)
                    {
                        if (modelAttr.DependentPropertyNames == null) continue;

                        if (model == null)
                        {
                            throw new ArgumentNullException(nameof(ChangeCanExecuteWhenModelPropertyChangeAttribute)
                                + " が指定されたプロパティが存在するにもかかわらず " + nameof(model) + " が null です。");
                        }

                        foreach (var propName in modelAttr.DependentPropertyNames)
                        {
                            if (modelListener == null) modelListener = new PropertyChangedWeakEventListener(model);
                            if (handler == null) handler = CreateHandler(prop);

                            modelListener.Add(propName, handler);
                        }
                    }
                }
            }

            if (thisListener != null) this.CompositeDisposable.Add(thisListener);
            if (modelListener != null) this.CompositeDisposable.Add(modelListener);

            PropertyChangedEventHandler CreateHandler(PropertyInfo prop) =>
                Expression.Lambda<PropertyChangedEventHandler>(
                    Expression.Call(
                        Expression.Property(Expression.Constant(this), prop),
                        s_viewModelCommandRaiseCanExecuteChangedMethodInfo
                    ),
                    Expression.Parameter(typeof(object)),
                    Expression.Parameter(typeof(PropertyChangedEventArgs))
                ).Compile();
        }

        private static readonly MethodInfo s_viewModelCommandRaiseCanExecuteChangedMethodInfo = typeof(ViewModelCommand).GetMethod(nameof(ViewModelCommand.RaiseCanExecuteChanged));

        protected static ViewModelCommand CreateCommand(ref ViewModelCommand backingStore, Action execute, Func<bool> canExecute = null)
        {
            if (backingStore == null)
                backingStore = new ViewModelCommand(execute, canExecute);

            return backingStore;
        }

        protected static void RunOnUIThread(Action action)
        {
            var dispatcher = DispatcherHelper.UIDispatcher;
            if (dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.InvokeAsync(action);
            }
        }

        protected override void RaisePropertyChanged([CallerMemberName] string propertyName = "")
        {
            RunOnUIThread(() => base.RaisePropertyChanged(propertyName));
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing) this.CompositeDisposable.Dispose();
        }

        public virtual void Dispose()
        {
            Dispose(true);
        }

        #region INotifyDataErrorInfo

        private readonly ConcurrentDictionary<string, IEnumerable> _validationErrors = new ConcurrentDictionary<string, IEnumerable>();

        public virtual bool HasErrors =>
            this._validationErrors.Values.Any(x =>
            {
                if (x == null) return false;
                var enumerator = x.GetEnumerator();
                try
                {
                    return enumerator.MoveNext();
                }
                finally
                {
                    (enumerator as IDisposable)?.Dispose();
                }
            });

        public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;

        public virtual IEnumerable GetErrors(string propertyName)
        {
            this._validationErrors.TryGetValue(propertyName, out var x);
            return x;
        }

        protected virtual void RaiseErrorsChanged(string propertyName)
        {
            var handler = this.ErrorsChanged;
            if (handler != null)
                RunOnUIThread(() => handler(this, new DataErrorsChangedEventArgs(propertyName)));

            this.RaisePropertyChanged(nameof(this.HasErrors));
        }

        protected void EnableValidation()
        {
            PropertyChangedEventListener listener = null;

            foreach (var prop in this.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var attr = prop.GetCustomAttribute<ValidationMethodAttribute>();
                if (attr == null) continue;

                if (listener == null) listener = new PropertyChangedEventListener(this);

                var validationMethodInfo = this.GetType().GetMethod(attr.ValidationMethodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                var thisConstant = Expression.Constant(this);
                var propName = prop.Name;
                var propNameConstant = Expression.Constant(propName);
                var handler = Expression.Lambda<PropertyChangedEventHandler>(
                    Expression.Block(
                        Expression.Call(
                            Expression.Field(thisConstant, s_validationErrorsFieldInfo),
                            s_concurrentDictionarySetItemMethodInfo,
                            propNameConstant,
                            Expression.Call(
                                thisConstant,
                                validationMethodInfo
                            )
                        ),
                        Expression.Call(
                            thisConstant,
                            s_raiseErrorsChangedMethodInfo,
                            propNameConstant
                        )
                    ),
                    Expression.Parameter(typeof(object)),
                    Expression.Parameter(typeof(PropertyChangedEventArgs))
                ).Compile();

                listener.Add(propName, handler);
            }

            if (listener != null) this.CompositeDisposable.Add(listener);
        }

        private static readonly FieldInfo s_validationErrorsFieldInfo = typeof(ViewModel2).GetField(nameof(_validationErrors), BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo s_concurrentDictionarySetItemMethodInfo = typeof(ConcurrentDictionary<string, IEnumerable>).GetMethod("set_Item", BindingFlags.Public | BindingFlags.Instance);
        private static readonly MethodInfo s_raiseErrorsChangedMethodInfo = typeof(ViewModel2).GetMethod(nameof(RaiseErrorsChanged), BindingFlags.NonPublic | BindingFlags.Instance);

        #endregion
    }
}
