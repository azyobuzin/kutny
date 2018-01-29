using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.Wave;
using PitchDetector;
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

        private const double MinUnitWidth = 3;
        private double _unitWidth = 10;
        private int _unitCount;

        private Task<VowelClassifier> _svmVowelClassifier;
        private Task<VowelClassifier> _nnVowelClassifier;
        private Task<VowelClassifier> _melSpectrumVowelClassifier;

        private WaveStream _waveStream;
        private readonly WaveOutEvent _player = new WaveOutEvent();
        private readonly DispatcherTimer _playerTimer;
        private double _currentPlaybackPosition; // AnalysisUnit 何個分かで見る
        private long _playbackStartPosition;
        private bool _stoppingPlaybackManually;

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            this.ViewModel.Initialize();

            // 学習はバックグラウンドでやっておく
            var dir = Utils.GetTrainingDataDirectory();
            var trainingData = new[] {
                Path.Combine(dir, "あいうえお 2017-12-18 00-17-09.csv"),
                Path.Combine(dir, "あいうえお 2018-01-20 16-48-52.csv")
            };

            if (this._svmVowelClassifier == null)
            {
                this._svmVowelClassifier = Task.Run(async () =>
                {
                    var classifier = new SvmVowelClassifier();
                    await Task.WhenAll(trainingData.Select(classifier.AddTrainingDataAsync)).ConfigureAwait(false);
                    classifier.Learn();
                    return (VowelClassifier)classifier;
                });
            }

            if (this._nnVowelClassifier == null)
            {
                this._nnVowelClassifier = Task.Run(async () =>
                {
                    var classifier = new NeuralNetworkVowelClassifier();
                    await Task.WhenAll(trainingData.Select(classifier.AddTrainingDataAsync)).ConfigureAwait(false);
                    classifier.Learn();
                    return (VowelClassifier)classifier;
                });
            }

            if (this._melSpectrumVowelClassifier == null)
            {
                this._melSpectrumVowelClassifier = Task.Run(async () =>
                {
                    var classifier = new MelSpectrumVowelClassifier();
                    await Task.WhenAll(trainingData.Select(classifier.AddTrainingDataAsync)).ConfigureAwait(false);
                    classifier.Learn();
                    return (VowelClassifier)classifier;
                });
            }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            this.verticalScrollBar.Maximum = Math.Max(
                0,
                this.mainContentGrid.ActualHeight - this.mainContentContainer.ActualHeight
            );

            this.UpdateHorizontalScrollBar();
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

        private void UpdateHorizontalScrollBar()
        {
            this.horizontalScrollBar.Maximum = Math.Max(
                0,
                this.notesGrid.Width - (this.mainContentGrid.ActualWidth - this.noteNamesGrid.ActualWidth)
            );
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

        private void zoomInButton_Click(object sender, RoutedEventArgs e)
        {
            this.UpdateZoom(this._unitWidth * 2);
        }

        private void zoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            this.UpdateZoom(Math.Max(this._unitWidth / 2, MinUnitWidth));
        }

        private void UpdateZoom(double unitWidth)
        {
            var newScrollValue = this.horizontalScrollBar.Value * unitWidth / this._unitWidth;
            this._unitWidth = unitWidth;
            this.notesGrid.Width = unitWidth * this._unitCount;
            this.UpdateHorizontalScrollBar();
            this.horizontalScrollBar.Value = newScrollValue;
        }

        private async void openButton_Click(object sender, RoutedEventArgs e)
        {
            if (this._player.PlaybackState == PlaybackState.Playing)
                this.PausePlayback();

            if (this._openFileDialog.ShowDialog(this) != true) return;

            this.loadingGrid.Visibility = Visibility.Visible;

            var classifierIndex = classifierComboBox.SelectedIndex;
            var blocks = await Task.Run(() => this.OpenFileAsync(this._openFileDialog.FileName, classifierIndex));

            // 今あるものを全部削除
            this.notesGrid.Children.Clear();

            // リサイズ
            this.UpdateZoom(this._unitWidth);
            this.horizontalScrollBar.Value = 0;
            this.horizontalScrollBar_ValueChanged();

            // Grid のカラム数を合わせる
            var oldColumnCount = this.notesGrid.ColumnDefinitions.Count;
            if (oldColumnCount < this._unitCount)
            {
                for (var i = oldColumnCount; i < this._unitCount; i++)
                    this.notesGrid.ColumnDefinitions.Add(new ColumnDefinition());
            }
            else if (oldColumnCount > this._unitCount)
            {
                this.notesGrid.ColumnDefinitions.RemoveRange(this._unitCount, oldColumnCount - this._unitCount);
            }

            // Border をつくる
            for (var i = 0; i <= 127; i++)
            {
                var border = new Border()
                {
                    BorderThickness = new Thickness(0, 0.5, 0, 0.5),
                    BorderBrush = SystemColors.InactiveBorderBrush
                };
                Grid.SetRow(border, i);
                Grid.SetColumnSpan(border, this._unitCount);
                this.notesGrid.Children.Add(border);
            }

            // ブロックを作る
            foreach (var x in blocks)
            {
                var block = new NoteBlock()
                {
                    Text = VowelClassifier.VowelTypeToString(x.VowelType)
                };
                Grid.SetRow(block, 127 - x.NoteNumber);
                Grid.SetColumn(block, x.Start);
                Grid.SetColumnSpan(block, x.Span);
                this.notesGrid.Children.Add(block);
            }

            this.loadingGrid.Visibility = Visibility.Collapsed;
        }

        private async Task<IReadOnlyList<NoteBlockInfo>> OpenFileAsync(string fileName, int classifierIndex)
        {
            if (this._waveStream != null)
            {
                this._waveStream.Dispose();
                this._waveStream = null;
            }

            Task<VowelClassifier> classifierTask;
            switch (classifierIndex)
            {
                case 0: classifierTask = this._svmVowelClassifier; break;
                case 1: classifierTask = this._nnVowelClassifier; break;
                case 2: classifierTask = this._melSpectrumVowelClassifier; break;
                default: throw new ArgumentOutOfRangeException(nameof(classifierIndex));
            }

            const int vowelWindowSize = 2048;
            const int pitchWindowSize = 1024;

            var blocks = new List<NoteBlockInfo>();
            var reader = new AudioFileReader(fileName);

            try
            {
                var provider = reader.ToSampleProvider().ToMono();
                var sampleRate = provider.WaveFormat.SampleRate;

                var classifier = await classifierTask.ConfigureAwait(false);
                var samples = new float[AnalysisUnit];

                for (var unitCount = 0; ; unitCount++)
                {
                    // 4096 サンプルを読み込み
                    for (var readSamples = 0; readSamples < samples.Length;)
                    {
                        var count = provider.Read(samples, readSamples, samples.Length - readSamples);
                        if (count == 0)
                        {
                            this._unitCount = unitCount;
                            goto EndAnalysis;
                        }
                        readSamples += count;
                    }

                    var maxPower = 0f;
                    foreach (var x in samples)
                    {
                        if (x > maxPower)
                            maxPower = x;
                    }

                    // 音量小さすぎ
                    if (maxPower < 0.15) continue;

                    // 512 ずつずらしながら母音認識
                    var vowelCandidates = new int[(int)VowelType.Other + 1];
                    for (var offset = 0; offset <= AnalysisUnit - vowelWindowSize; offset += 512)
                    {
                        vowelCandidates[(int)classifier.Decide(new ReadOnlySpan<float>(samples, offset, vowelWindowSize), sampleRate)]++;
                    }

                    var vowelCandidate = default(VowelType?);
                    var maxNumOfVotes = 0;
                    for (var j = 0; j < vowelCandidates.Length; j++)
                    {
                        if (vowelCandidates[j] > maxNumOfVotes)
                        {
                            maxNumOfVotes = vowelCandidates[j];
                            vowelCandidate = (VowelType)j;
                        }
                        else if (vowelCandidates[j] == maxNumOfVotes)
                        {
                            vowelCandidate = null;
                        }
                    }

                    // 母音が定まらなかったので、終了
                    if (!vowelCandidate.HasValue || vowelCandidate.Value == VowelType.Other)
                        continue;

                    // 512 ずつずらしながらピッチ検出
                    const int pitchOffsetDelta = 512;
                    var basicFreqs = new List<double>(AnalysisUnit / pitchOffsetDelta);
                    for (var offset = 0; offset <= AnalysisUnit - pitchWindowSize; offset += pitchOffsetDelta)
                    {
                        var f = PitchAccord.EstimateBasicFrequency(
                            sampleRate,
                            new ReadOnlySpan<float>(samples, offset, pitchWindowSize)
                        );

                        if (f.HasValue) basicFreqs.Add(f.Value);
                    }

                    // ピッチ検出に失敗したので終了
                    if (basicFreqs.Count == 0) continue;

                    basicFreqs.Sort();
                    var basicFreq = basicFreqs[basicFreqs.Count / 2]; // 中央値
                    var noteNum = Utils.HzToMidiNote(basicFreq);

                    var block = new NoteBlockInfo(unitCount, noteNum, vowelCandidate.Value);

                    if (blocks.Count == 0 || !blocks[blocks.Count - 1].MergeIfPossible(block))
                        blocks.Add(block);
                }
            }
            catch
            {
                reader.Dispose();
                throw;
            }

            EndAnalysis:
            this._waveStream = reader;
            this._currentPlaybackPosition = 0;
            return blocks;
        }

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

        private void ReloadPlaybackPosition(bool scroll)
        {
            this._currentPlaybackPosition = (double)(this._player.GetPosition() + this._playbackStartPosition)
                / this._waveStream.WaveFormat.BlockAlign
                / AnalysisUnit;

            this.UpdatePlaybackPositionBar();

            if (scroll)
            {
                if (this.playbackPositionBar.Margin.Left >= this.mainContentGrid.ActualWidth - 1)
                {
                    this.horizontalScrollBar.Value = this._currentPlaybackPosition * this._unitWidth;
                }
            }
        }

        private void UpdatePlaybackPositionBar()
        {
            this.playbackPositionBar.Margin = new Thickness(
                this.noteNamesGrid.ActualWidth - this.horizontalScrollBar.Value + this._unitWidth * (this._currentPlaybackPosition - 0.5),
                0, 0, 0
            );
        }

        private void playButton_Click(object sender, RoutedEventArgs e)
        {
            if (this._player.PlaybackState == PlaybackState.Playing)
            {
                this.PausePlayback();
            }
            else if (this._waveStream != null)
            {
                this.PlayFromPosition(this._currentPlaybackPosition);
            }
        }

        private void PausePlayback()
        {
            this._player.Pause();
            this._playerTimer.Stop();
            this.ReloadPlaybackPosition(true);
        }

        private void PlayFromPosition(double pos)
        {
            if (this._player.PlaybackState != PlaybackState.Stopped)
            {
                this._stoppingPlaybackManually = true;
                this._player.Stop();
            }

            this._playbackStartPosition = (long)(this._waveStream.BlockAlign * AnalysisUnit * pos);
            this._waveStream.Position = this._playbackStartPosition;
            this._player.Init(this._waveStream);
            this._player.Play();
            this._playerTimer.Start();
        }

        private void mainContentContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            this.MovePlaybackPosition(e);
        }

        private void MovePlaybackPosition(MouseEventArgs e)
        {
            if (this._waveStream == null) return;

            var pos = e.GetPosition(this.notesGrid).X / this._unitWidth + 0.5;

            if (this._player.PlaybackState == PlaybackState.Playing)
            {
                this.PlayFromPosition(pos);
            }
            else
            {
                this._currentPlaybackPosition = pos;
                this.UpdatePlaybackPositionBar();
            }
        }
    }
}
