using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using NAudio.Wave;
using PitchDetector;

namespace UtagoeGui
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

        private static readonly string[] s_noteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

        private readonly OpenFileDialog _openFileDialog = new OpenFileDialog()
        {
            Filter = "すべてのファイル|*"
        };

        private double _unitWidth = 10;
        private const double MinUnitWidth = 3;

        private int _unitCount;

        private Task<VowelClassifier> _vowelClassifier;

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            for (var i = 0; i <= 127; i++)
            {
                this.noteNamesGrid.RowDefinitions.Add(new RowDefinition());
                this.notesGrid.RowDefinitions.Add(new RowDefinition());

                var noteNum = 127 - i;
                var noteNameTextBlock = new TextBlock()
                {
                    Text = s_noteNames[noteNum % 12] + (noteNum / 12).ToString(),
                    Padding = new Thickness(6, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var border = new Border()
                {
                    BorderThickness = new Thickness(0, 0.5, 0, 0.5),
                    BorderBrush = SystemColors.ActiveBorderBrush,
                    Child = noteNameTextBlock
                };

                Grid.SetRow(border, i);
                this.noteNamesGrid.Children.Add(border);
            }

            if (this._vowelClassifier == null)
            {
                // 学習はバックグラウンドでやっておく
                this._vowelClassifier = Task.Run(async () =>
                {
                    var classifier = new VowelClassifier();
                    var dir = Utils.GetTrainingDataDirectory();

                    await Task.WhenAll(
                        classifier.AddTrainingDataAsync(Path.Combine(dir, "あいうえお 2017-12-18 00-17-09.csv")),
                        classifier.AddTrainingDataAsync(Path.Combine(dir, "あいうえお 2018-01-20 16-48-52.csv"))
                    ).ConfigureAwait(false);

                    classifier.Learn();
                    return classifier;
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
            this.notesGrid.Margin = new Thickness(this.noteNamesGrid.ActualWidth - e.NewValue, 0, 0, 0);
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
            if (this._openFileDialog.ShowDialog(this) != true) return;

            this.loadingGrid.Visibility = Visibility.Visible;
            var blocks = await Task.Run(() => this.OpenFileAsync(this._openFileDialog.FileName));

            // 今あるものを全部削除
            this.notesGrid.Children.Clear();

            // リサイズ
            this.UpdateZoom(this._unitWidth);
            this.horizontalScrollBar.Value = 0;

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
                string label;
                switch (x.VowelType)
                {
                    case VowelType.A: label = "あ"; break;
                    case VowelType.I: label = "い"; break;
                    case VowelType.U: label = "う"; break;
                    case VowelType.E: label = "え"; break;
                    case VowelType.O: label = "お"; break;
                    case VowelType.N: label = "ん"; break;
                    default: throw new InvalidOperationException();
                }

                var block = new NoteBlock() { Text = label };
                Grid.SetRow(block, 127 - x.NoteNumber);
                Grid.SetColumn(block, x.Start);
                Grid.SetColumnSpan(block, x.Span);
                this.notesGrid.Children.Add(block);
            }

            this.loadingGrid.Visibility = Visibility.Collapsed;
        }

        private async Task<IReadOnlyList<NoteBlockInfo>> OpenFileAsync(string fileName)
        {
            const int analysisUnit = 4096;
            const int vowelWindowSize = 2048;
            const int pitchWindowSize = 1024;

            var blocks = new List<NoteBlockInfo>();

            using (var reader = new MediaFoundationReader(fileName))
            {
                var provider = reader.ToSampleProvider().ToMono();
                var sampleRate = provider.WaveFormat.SampleRate;

                var mfccComputer = new MfccAccord(sampleRate, vowelWindowSize);
                var classifier = await this._vowelClassifier.ConfigureAwait(false);
                var samples = new float[analysisUnit];

                for (var unitCount = 0; ; unitCount++)
                {
                    // 4096 サンプルを読み込み
                    for (var readSamples = 0; readSamples < samples.Length;)
                    {
                        var count = provider.Read(samples, readSamples, samples.Length - readSamples);
                        if (count == 0)
                        {
                            this._unitCount = unitCount;
                            return blocks;
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
                    for (var offset = 0; offset <= analysisUnit - vowelWindowSize; offset += 512)
                    {
                        var mfcc = mfccComputer.ComputeMfcc12D(new ReadOnlySpan<float>(samples, offset, vowelWindowSize));
                        vowelCandidates[(int)classifier.Decide(mfcc)]++;
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
                    var basicFreqs = new List<double>(analysisUnit / pitchOffsetDelta);
                    for (var offset = 0; offset <= analysisUnit - pitchWindowSize; offset += pitchOffsetDelta)
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
        }

        private Point? _mousePosition;

        private void mainContentContainer_MouseMove(object sender, MouseEventArgs e)
        {
            e.Handled = true;

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
    }
}
