using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using AutoHarmony.ViewModels;

namespace AutoHarmony.Views
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private MainWindowViewModel ViewModel => (MainWindowViewModel)this.DataContext;

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            this.ViewModel.Initialize();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            this.ViewModel.Exit();
        }
    }
}
