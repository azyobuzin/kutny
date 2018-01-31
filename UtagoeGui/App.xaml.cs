using System.ComponentModel;
using System.Linq;
using System.Windows;
using Livet;
using UtagoeGui.Models;
using UtagoeGui.ViewModels;
using UtagoeGui.Views;

namespace UtagoeGui
{
    /// <summary>
    /// App.xaml の相互作用ロジック
    /// </summary>
    public partial class App : Application
    {
        public AppModel Model { get; private set; }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            DispatcherHelper.UIDispatcher = this.Dispatcher;

            this.Model = new AppModel();
            this.Model.Store.PropertyChanged += this.Store_PropertyChanged;
            this.MainWindow = this.ShowMainWindow();
        }

        private Window ShowMainWindow()
        {
            var vm = new MainWindowViewModel(this.Model.Store, this.Model);
            var window = new MainWindow() { DataContext = vm };
            window.Show();
            return window;
        }

        private void Store_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(IAppStore.IsEditingCorrectScore):
                    this.OpenOrCloseEditCorrectScoreWindow();
                    break;
            }
        }

        private void OpenOrCloseEditCorrectScoreWindow()
        {
            if (this.Model.Store.IsEditingCorrectScore)
            {
                if (!this.Windows.OfType<EditCorrectScoreWindow>().Any())
                {
                    var vm = new EditCorrectScoreWindowViewModel(this.Model.Store, this.Model);
                    new EditCorrectScoreWindow() { DataContext = vm }.Show();
                }
            }
            else
            {
                foreach (var window in this.Windows.OfType<EditCorrectScoreWindow>())
                    window.Close();
            }
        }
    }
}
