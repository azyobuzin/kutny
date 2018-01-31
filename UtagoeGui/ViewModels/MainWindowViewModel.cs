using System;
using System.Collections.Immutable;
using System.Windows;
using Livet.Commands;
using Livet.EventListeners;
using UtagoeGui.Infrastructures;
using UtagoeGui.Models;

namespace UtagoeGui.ViewModels
{
    public class MainWindowViewModel : ViewModel2
    {
        public const double UnitWidth = 10;

        public IAppStore Store { get; }
        public IAppActions Actions { get; }

        public MainWindowViewModel()
        {
            var model = new AppModel();
            this.Store = model.Store;
            this.Actions = model;

            this.EnableAutoPropertyChangedEvent(this.Store);
            this.EnableAutoCanExecuteChangedEvent(this.Store);

            this.CompositeDisposable.Add(new PropertyChangedEventListener(this.Store)
            {
                {
                    nameof(IAppStore.VoiceAnalysisResult),
                    (_, __) =>
                    {
                        // 結果が更新されたら NoteBlockViewModel を作り直す
                        this.NoteBlocks = this.Store.VoiceAnalysisResult == null
                            ? ImmutableArray<NoteBlockViewModel>.Empty
                            : ImmutableArray.CreateRange(
                                this.Store.VoiceAnalysisResult.NoteBlocks,
                                x => new NoteBlockViewModel(x, this.Store)
                            );
                    }
                }
            });
        }

        #region プロパティ

        [ChangeValueWhenModelPropertyChange(nameof(IAppStore.VowelClassifierType))]
        public int SelectedClassifierIndex
        {
            get => VowelClassifierTypeToIndex(this.Store.VowelClassifierType);
            set => this.Actions.ChangeVowelClassifier(IndexToVowerlClassifierType(value));
        }

        [ChangeValueWhenModelPropertyChange(nameof(IAppStore.IsWorking))]
        public Visibility LoadingViewVisibility => this.Store.IsWorking ? Visibility.Visible : Visibility.Collapsed;

        [ChangeValueWhenModelPropertyChange(nameof(IAppStore.IsPlaying))]
        public string PlayButtonText => this.Store.IsPlaying ? "停止" : "再生";

        [ChangeValueWhenModelPropertyChange(nameof(IAppStore.VoiceAnalysisResult), nameof(IAppStore.ScoreScale))]
        public double ScoreWidth => this.Store.VoiceAnalysisResult?.UnitCount * UnitWidth * this.Store.ScoreScale ?? 0;

        private ImmutableArray<NoteBlockViewModel> _noteBlocks = ImmutableArray<NoteBlockViewModel>.Empty;
        public ImmutableArray<NoteBlockViewModel> NoteBlocks
        {
            get => this._noteBlocks;
            set => this.Set(ref this._noteBlocks, value);
        }

        [ChangeValueWhenModelPropertyChange(nameof(IAppStore.PlaybackPositionInSamples), nameof(IAppStore.ScoreScale))]
        public double PlaybackPositionBarLeftMargin => this.Store.PlaybackPositionInSamples * this.DeviceIndependentUnitsPerSample;

        #endregion

        #region イベント

        public event EventHandler AudioFileSelectionRequested;

        #endregion

        #region コマンド

        private ViewModelCommand _openFileCommand;
        [ChangeCanExecuteWhenModelPropertyChange(nameof(IAppStore.IsWorking))]
        public ViewModelCommand OpenAudioFileCommand => CreateCommand(
            ref this._openFileCommand,
            () =>
            {
                this.Actions.StopPlayback();
                this.AudioFileSelectionRequested?.Invoke(this, EventArgs.Empty);
            },
            () => !this.Store.IsWorking
        );

        private ViewModelCommand _playCommand;
        [ChangeCanExecuteWhenModelPropertyChange(nameof(IAppStore.VoiceAnalysisResult))]
        public ViewModelCommand PlayCommand => CreateCommand(
            ref this._playCommand,
            () => this.Actions.TogglePlaybackState(),
            () => this.Store.VoiceAnalysisResult != null
        );

        private ViewModelCommand _zoomInCommand;
        public ViewModelCommand ZoomInCommand => CreateCommand(
            ref this._zoomInCommand,
            () => this.Actions.ZoomIn()
        );

        private ViewModelCommand _zoomOutCommand;
        [ChangeCanExecuteWhenModelPropertyChange(nameof(IAppStore.CanZoomOut))]
        public ViewModelCommand ZoomOutCommand => CreateCommand(
            ref this._zoomOutCommand,
            () => this.Actions.ZoomOut(),
            () => this.Store.CanZoomOut
        );

        #endregion

        #region View からの応答

        public void Initialize()
        {
            this.Actions.Initialize();
        }

        public void SelectedAudioFile(string fileName)
        {
            this.Actions.OpenAudioFile(fileName);
        }

        public void MovePlaybackPosition(double x)
        {
            var pos = x / this.DeviceIndependentUnitsPerSample;
            this.Actions.MovePlaybackPosition(Math.Max(pos, 0));
        }

        #endregion

        private static int VowelClassifierTypeToIndex(VowelClassifierType x) => (int)x;
        private static VowelClassifierType IndexToVowerlClassifierType(int x) => (VowelClassifierType)x;

        private double DeviceIndependentUnitsPerSample => UnitWidth * this.Store.ScoreScale / Logics.AnalysisUnit;
    }
}
