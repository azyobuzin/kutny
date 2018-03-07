using System;
using System.Collections.Immutable;
using System.Windows;
using Kutny.WpfInfra;
using Livet.Commands;
using Livet.EventListeners.WeakEvents;
using UtagoeGui.Models;

namespace UtagoeGui.ViewModels
{
    public class MainWindowViewModel : ViewModel2
    {
        public const double UnitWidth = 10;

        public IAppStore Store { get; }
        public IAppActions Actions { get; }

        public MainWindowViewModel(IAppStore store, IAppActions actions)
        {
            this.Store = store;
            this.Actions = actions;

            this.EnableAutoPropertyChangedEvent(store);
            this.EnableAutoCanExecuteChangedEvent(store);

            this.CompositeDisposable.Add(new PropertyChangedWeakEventListener(
                store,
                (_, e) =>
                {
                    switch (e.PropertyName)
                    {
                        case nameof(IAppStore.VoiceAnalysisResult):
                            // 結果が更新されたら NoteBlockViewModel を作り直す
                            this.NoteBlocks = this.Store.VoiceAnalysisResult == null
                                ? ImmutableArray<NoteBlockViewModel>.Empty
                                : ImmutableArray.CreateRange(
                                    this.Store.VoiceAnalysisResult.NoteBlocks,
                                    x => new NoteBlockViewModel(x, this.Store)
                                );
                            break;
                        case nameof(IAppStore.CorrectNoteBlocks):
                            // 正解データも同様に
                            this.CorrectNoteBlocks = ImmutableArray.CreateRange(
                                this.Store.CorrectNoteBlocks,
                                x => new CorrectNoteBlockViewModel(x, this.Store)
                            );
                            break;
                    }
                }
            ));
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

        private ImmutableArray<CorrectNoteBlockViewModel> _correctNoteBlocks = ImmutableArray<CorrectNoteBlockViewModel>.Empty;
        public ImmutableArray<CorrectNoteBlockViewModel> CorrectNoteBlocks
        {
            get => this._correctNoteBlocks;
            set => this.Set(ref this._correctNoteBlocks, value);
        }

        #endregion

        #region イベント

        public event EventHandler AudioFileSelectionRequested;

        #endregion

        #region コマンド

        private ViewModelCommand _openAudioFileCommand;
        [ChangeCanExecuteWhenModelPropertyChange(nameof(IAppStore.IsWorking))]
        public ViewModelCommand OpenAudioFileCommand => CreateCommand(
            ref this._openAudioFileCommand,
            () =>
            {
                this.Actions.StopPlayback();
                RunOnUIThread(() => this.AudioFileSelectionRequested?.Invoke(this, EventArgs.Empty));
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

        private ViewModelCommand _editCorrectScoreCommand;
        public ViewModelCommand EditCorrectScoreCommand => CreateCommand(
            ref this._editCorrectScoreCommand,
            () => this.Actions.OpenCorrectScoreWindow()
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
