using System.Collections.Immutable;
using System.ComponentModel;
using StatefulModel;
using UtagoeGui.Infrastructures;

namespace UtagoeGui.Models
{
    /// <summary>
    /// <see cref="AppStore"/> への読み取り専用アクセスを提供します。
    /// </summary>
    public interface IAppStore : INotifyPropertyChanged
    {
        bool IsWorking { get; }
        VowelClassifierType VowelClassifierType { get; }
        VoiceAnalysisResult VoiceAnalysisResult { get; }
        bool IsPlaying { get; }
        double PlaybackPositionInSamples { get; }
        double ScoreScale { get; }
        bool CanZoomOut { get; }
        bool IsEditingCorrectData { get; }
        ImmutableArray<CorrectNoteBlockModel> CorrectNoteBlocks { get; }
        double CorrectDataStartPositionInAnalysisUnits { get; }
        ReadOnlyNotifyChangedCollection<TempoSetting> TempoSettings { get; }
    }

    internal class AppStore : NotificationObject2, IAppStore
    {
        private bool _isWorking;
        public bool IsWorking
        {
            get => this._isWorking;
            set => this.Set(ref this._isWorking, value);
        }

        private VowelClassifierType _vowelClassifierType = VowelClassifierType.MfccSupportVectorMachine;
        public VowelClassifierType VowelClassifierType
        {
            get => this._vowelClassifierType;
            set => this.Set(ref this._vowelClassifierType, value);
        }

        private VoiceAnalysisResult _noteBlocks;
        public VoiceAnalysisResult VoiceAnalysisResult
        {
            get => this._noteBlocks;
            set => this.Set(ref this._noteBlocks, value);
        }

        private bool _isPlaying;
        public bool IsPlaying
        {
            get => this._isPlaying;
            set => this.Set(ref this._isPlaying, value);
        }

        private double _playbackPositionInSamples;
        public double PlaybackPositionInSamples
        {
            get => this._playbackPositionInSamples;
            set => this.Set(ref this._playbackPositionInSamples, value);
        }

        private double _scoreScale = 1.0;
        public double ScoreScale
        {
            get => this._scoreScale;
            set => this.Set(ref this._scoreScale, value);
        }

        private bool _canZoomOut = true;
        public bool CanZoomOut
        {
            get => this._canZoomOut;
            set => this.Set(ref this._canZoomOut, value);
        }

        private bool _isEditingCorrectData;
        public bool IsEditingCorrectData
        {
            get => this._isEditingCorrectData;
            set => this.Set(ref this._isEditingCorrectData, value);
        }

        private ImmutableArray<CorrectNoteBlockModel> _correctNoteBlocks;
        public ImmutableArray<CorrectNoteBlockModel> CorrectNoteBlocks
        {
            get => this._correctNoteBlocks;
            set => this.Set(ref this._correctNoteBlocks, value);
        }

        private double _correctDataStartPositionInUnits;
        public double CorrectDataStartPositionInAnalysisUnits
        {
            get => this._correctDataStartPositionInUnits;
            set => this.Set(ref this._correctDataStartPositionInUnits, value);
        }

        public SortedObservableCollection<TempoSetting, int> TempoSettings { get; } = new SortedObservableCollection<TempoSetting, int>(x => x.Position);

        private ReadOnlyNotifyChangedCollection<TempoSetting> _readOnlyTempoSettings;
        ReadOnlyNotifyChangedCollection<TempoSetting> IAppStore.TempoSettings => this._readOnlyTempoSettings
            ?? (this._readOnlyTempoSettings = this.TempoSettings.ToSyncedReadOnlyNotifyChangedCollection());
    }
}
