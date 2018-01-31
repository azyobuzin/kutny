using System;
using System.Windows;
using Microsoft.Win32;
using UtagoeGui.ViewModels;

namespace UtagoeGui.Views
{
    /// <summary>
    /// EditCorrectScoreWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class EditCorrectScoreWindow : Window
    {
        public EditCorrectScoreWindow()
        {
            InitializeComponent();
        }

        private EditCorrectScoreWindowViewModel ViewModel => (EditCorrectScoreWindowViewModel)this.DataContext;

        private readonly OpenFileDialog _openFileDialog = new OpenFileDialog()
        {
            Filter = "UTAU スクリプト形式|*.ust|すべてのファイル|*"
        };

        private void Window_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is EditCorrectScoreWindowViewModel oldValue)
            {
                oldValue.CorrectScoreFileSelectionRequested -= this.ViewModel_CorrectScoreFileSelectionRequested;
            }

            if (e.NewValue is EditCorrectScoreWindowViewModel newValue)
            {
                newValue.CorrectScoreFileSelectionRequested += this.ViewModel_CorrectScoreFileSelectionRequested;
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            this.ViewModel.Closed();
        }

        private void ViewModel_CorrectScoreFileSelectionRequested(object sender, EventArgs e)
        {
            if (this._openFileDialog.ShowDialog(this) == true)
            {
                this.ViewModel.SelectedCorrectScoreFile(this._openFileDialog.FileName);
            }
        }
    }
}
