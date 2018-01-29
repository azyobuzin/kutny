using System.Windows;
using UtagoeGui.Infrastructures;
using UtagoeGui.Models;

namespace UtagoeGui.ViewModels
{
    public class MainWindowViewModel : ViewModel2
    {
        public IAppStore Store { get; }
        public IAppActions Actions { get; }

        public MainWindowViewModel()
        {
            var model = new AppModel();
            this.Store = model.Store;
            this.Actions = model;

            this.EnableAutoPropertyChangedEvent(this.Store);
        }

        public void Initialize()
        {
            this.Actions.Initialize();
        }

        [DependencyOnModelProperty(nameof(IAppStore.VowelClassifierType))]
        public int SelectedClassifierIndex
        {
            get => (int)this.Store.VowelClassifierType;
            set => this.Actions.ChangeVowelClassifier((VowelClassifierType)value);
        }

        [DependencyOnModelProperty(nameof(IAppStore.IsWorking))]
        public Visibility LoadingViewVisibility => this.Store.IsWorking ? Visibility.Visible : Visibility.Collapsed;
    }
}
