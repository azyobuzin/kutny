using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using UtagoeGui.Models;
using UtagoeGui.ViewModels;

namespace UtagoeGui.Views
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            for (var i = 0; i <= Logics.MaximumNoteNumber - Logics.MinimumNoteNumber; i++)
            {
                // noteNamesGrid の用意
                this.noteNamesGrid.RowDefinitions.Add(new RowDefinition());

                var noteNum = Logics.MaximumNoteNumber - i;
                var noteNameTextBlock = new TextBlock()
                {
                    Text = Logics.ToNoteName(noteNum),
                    Padding = new Thickness(6, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var noteNameBorder = new Border()
                {
                    BorderThickness = new Thickness(0, 0.5, 0, 0.5),
                    BorderBrush = SystemColors.ActiveBorderBrush,
                    Child = noteNameTextBlock
                };

                Grid.SetRow(noteNameBorder, i);
                this.noteNamesGrid.Children.Add(noteNameBorder);

                // notesGrid の用意
                this.notesGrid.RowDefinitions.Add(new RowDefinition());

                var noteLineBorder = new Border()
                {
                    BorderThickness = new Thickness(0, 0.5, 0, 0.5),
                    BorderBrush = SystemColors.InactiveBorderBrush
                };

                Grid.SetRow(noteLineBorder, i);
                this.notesGrid.Children.Add(noteLineBorder);
            }
        }

        private MainWindowViewModel ViewModel => (MainWindowViewModel)this.DataContext;

        private readonly OpenFileDialog _openFileDialog = new OpenFileDialog()
        {
            Filter = "すべてのファイル|*"
        };

        private void Window_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // ViewModel がセットされたので、イベントハンドラの設定

            if (e.OldValue is MainWindowViewModel oldValue)
            {
                oldValue.PropertyChanged -= this.ViewModel_PropertyChanged;
                oldValue.AudioFileSelectionRequested -= this.ViewModel_AudioFileSelectionRequested;
            }

            if (e.NewValue is MainWindowViewModel newValue)
            {
                newValue.PropertyChanged += this.ViewModel_PropertyChanged;
                newValue.AudioFileSelectionRequested += this.ViewModel_AudioFileSelectionRequested;
            }
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(MainWindowViewModel.NoteBlocks):
                    this.OnUpdatedNoteBlocks();
                    break;
                case nameof(MainWindowViewModel.ScoreWidth):
                    this.notesGrid.Width = this.ViewModel.ScoreWidth; // Binding うまくいかぬ
                    this.OnUpdatedScale();
                    break;
                case nameof(MainWindowViewModel.PlaybackPositionBarLeftMargin):
                    this.UpdatePlaybackPositionBar();
                    break;
                case nameof(MainWindowViewModel.CorrectNoteBlocks):
                    this.OnUpdatedCorrectNoteBlocks();
                    break;
            }
        }

        private void ViewModel_AudioFileSelectionRequested(object sender, EventArgs e)
        {
            if (this._openFileDialog.ShowDialog(this) == true)
            {
                this.ViewModel.SelectedAudioFile(this._openFileDialog.FileName);
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            this.ViewModel.Initialize();
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            this.OnUpdatedScale();

            this.verticalScrollBar.Maximum = Math.Max(
                0,
                this.mainContentGrid.ActualHeight - this.mainContentContainer.ActualHeight
            );
        }

        private void verticalScrollBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            this.mainContentGrid.Margin = new Thickness(0, -e.NewValue, 0, 0);
        }

        private void mainContentContainer_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            this.verticalScrollBar.Value -= e.Delta;
            e.Handled = true;
        }

        private void horizontalScrollBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            this.horizontalScrollBar_ValueChanged();
        }

        private void horizontalScrollBar_ValueChanged()
        {
            this.notesGrid.Margin = new Thickness(this.noteNamesGrid.ActualWidth - this.horizontalScrollBar.Value, 0, 0, 0);
            this.UpdatePlaybackPositionBar();
        }

        private void OnUpdatedScale()
        {
            var scrollRate = this.horizontalScrollBar.Value / this.horizontalScrollBar.Maximum;
            if (double.IsNaN(scrollRate)) scrollRate = 0; // NaN になるということは Maximum が 0 だった

            var newMaximum = Math.Max(
                0,
                this.ViewModel.ScoreWidth - (this.mainContentGrid.ActualWidth - this.noteNamesGrid.ActualWidth)
            );

            this.horizontalScrollBar.Maximum = newMaximum;
            this.horizontalScrollBar.Value = scrollRate * newMaximum;
        }

        private void OnUpdatedNoteBlocks()
        {
            var childrenSnapshot = this.notesGrid.Children
                .OfType<NoteBlock>()
                .ToArray();

            var newNoteBlocks = this.ViewModel.NoteBlocks;

            for (var i = 0; ; i++)
            {
                if (i < childrenSnapshot.Length)
                {
                    if (i < newNoteBlocks.Length)
                    {
                        // 使いまわす
                        childrenSnapshot[i].DataContext = newNoteBlocks[i];
                    }
                    else
                    {
                        // 余った View を削除
                        this.notesGrid.Children.Remove(childrenSnapshot[i]);
                    }
                }
                else if (i < newNoteBlocks.Length)
                {
                    // 新規作成
                    this.notesGrid.Children.Add(new NoteBlock() { DataContext = newNoteBlocks[i] });
                }
                else
                {
                    break;
                }
            }

            this.horizontalScrollBar_ValueChanged();
        }

        private void OnUpdatedCorrectNoteBlocks()
        {
            var childrenSnapshot = this.notesGrid.Children
               .OfType<CorrectNoteBlock>()
               .ToArray();

            var newNoteBlocks = this.ViewModel.CorrectNoteBlocks;

            for (var i = 0; ; i++)
            {
                if (i < childrenSnapshot.Length)
                {
                    if (i < newNoteBlocks.Length)
                    {
                        // 使いまわす
                        childrenSnapshot[i].DataContext = newNoteBlocks[i];
                    }
                    else
                    {
                        // 余った View を削除
                        this.notesGrid.Children.Remove(childrenSnapshot[i]);
                    }
                }
                else if (i < newNoteBlocks.Length)
                {
                    // 新規作成
                    this.notesGrid.Children.Add(new CorrectNoteBlock() { DataContext = newNoteBlocks[i] });
                }
                else
                {
                    break;
                }
            }
        }

        #region マウス右ボタンでスクロール

        private Point? _mousePosition;

        private void mainContentContainer_MouseMove(object sender, MouseEventArgs e)
        {
            e.Handled = true;

            if (e.LeftButton == MouseButtonState.Pressed)
                this.MovePlaybackPosition(e);

            if (!this._mousePosition.HasValue) return;

            if (e.RightButton == MouseButtonState.Released)
            {
                this._mousePosition = null;
                return;
            }

            var newPos = e.GetPosition(this);

            this.horizontalScrollBar.Value =
                Math.Max(
                    this.horizontalScrollBar.Minimum,
                    Math.Min(
                        this.horizontalScrollBar.Maximum,
                        this.horizontalScrollBar.Value - newPos.X + this._mousePosition.Value.X
                    )
                );

            this.verticalScrollBar.Value =
                Math.Max(
                    this.verticalScrollBar.Minimum,
                    Math.Min(
                        this.verticalScrollBar.Maximum,
                        this.verticalScrollBar.Value - newPos.Y + this._mousePosition.Value.Y
                    )
                );

            this._mousePosition = newPos;
        }

        private void mainContentContainer_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            this._mousePosition = e.GetPosition(this);
            e.Handled = true;
        }

        private void mainContentContainer_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            this._mousePosition = null;
            e.Handled = true;
        }

        #endregion

        #region タッチでスクロール

        private Point? _touchPosition;

        private void mainContentContainer_TouchMove(object sender, TouchEventArgs e)
        {
            e.Handled = true;
            if (!this._touchPosition.HasValue) return;

            var newPos = e.GetTouchPoint(this).Position;

            this.horizontalScrollBar.Value =
                Math.Max(
                    this.horizontalScrollBar.Minimum,
                    Math.Min(
                        this.horizontalScrollBar.Maximum,
                        this.horizontalScrollBar.Value - newPos.X + this._touchPosition.Value.X
                    )
                );

            this.verticalScrollBar.Value =
                Math.Max(
                    this.verticalScrollBar.Minimum,
                    Math.Min(
                        this.verticalScrollBar.Maximum,
                        this.verticalScrollBar.Value - newPos.Y + this._touchPosition.Value.Y
                    )
                );

            this._touchPosition = newPos;
        }

        private void mainContentContainer_TouchDown(object sender, TouchEventArgs e)
        {
            this._touchPosition = e.GetTouchPoint(this).Position;
            e.Handled = true;
        }

        private void mainContentContainer_TouchUp(object sender, TouchEventArgs e)
        {
            this._touchPosition = null;
            e.Handled = true;
        }

        #endregion

        private void UpdatePlaybackPositionBar()
        {
            var newLeftMargin = this.noteNamesGrid.ActualWidth - this.horizontalScrollBar.Value + this.ViewModel.PlaybackPositionBarLeftMargin;

            if (newLeftMargin >= this.mainContentGrid.ActualWidth - 1 && this.ViewModel.Store.IsPlaying)
            {
                // バーが右のほうにすっ飛んでいっているのでスクロール
                this.horizontalScrollBar.Value = this.ViewModel.PlaybackPositionBarLeftMargin;
            }
            else
            {
                this.playbackPositionBar.Margin = new Thickness(newLeftMargin, 0, 0, 0);
            }
        }

        private void mainContentContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            this.MovePlaybackPosition(e);
        }

        private void MovePlaybackPosition(MouseEventArgs e)
        {
            this.ViewModel.MovePlaybackPosition(e.GetPosition(this.notesGrid).X);
        }
    }
}
