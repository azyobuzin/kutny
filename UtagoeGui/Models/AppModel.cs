using NAudio.Wave;
using System;
using System.Threading;

namespace UtagoeGui.Models
{
    public interface IAppActions
    {
        void Initialize();
        void ChangeVowelClassifier(VowelClassifierType type);
        void OpenFile(string fileName);
        void TogglePlaybackState();
        void StopPlayback();
        void MovePlaybackPosition(double positionInSamples);
    }

    public class AppModel : IAppActions
    {
        private readonly AppStore _store = new AppStore();
        public IAppStore Store => this._store;

        private bool _isInitialized;
        private VoiceAnalyzer _voiceAnalyzer;
        private PlayerModel _player;

        public void Initialize()
        {
            if (this._isInitialized)
                throw new InvalidOperationException();

            this._isInitialized = true;

            // バックグラウンドで学習を開始
            this._voiceAnalyzer = new VoiceAnalyzer();
            this._voiceAnalyzer.StartLearning();
        }

        private void EnsureInitialized()
        {
            if (!this._isInitialized)
                throw new InvalidOperationException(nameof(this.Initialize) + " が呼び出されていません。");
        }

        public void ChangeVowelClassifier(VowelClassifierType type)
        {
            this._store.VowelClassifierType = type;
        }

        public void OpenFile(string fileName)
        {
            this.EnsureInitialized();
            this._store.IsWorking = true;

            ThreadPool.QueueUserWorkItem(async _ =>
            {
                VoiceAnalysisResult analysisResult;
                var reader = new AudioFileReader(fileName);

                try
                {
                    analysisResult = await this._voiceAnalyzer
                        .Analyze(this._store.VowelClassifierType, reader.ToSampleProvider())
                        .ConfigureAwait(false);
                }
                catch
                {
                    reader.Dispose();
                    throw; // TODO: エラー処理するならここで
                }

                if (this._player != null) this._player.Dispose();
                this._player = new PlayerModel(reader, this._store);

                this._store.VoiceAnalysisResult = analysisResult;
                this._store.PlaybackPositionInSamples = 0;
                this._store.IsWorking = false;
            });
        }

        public void TogglePlaybackState()
        {
            this.EnsurePlayerInitialized();
            this._player.TogglePlaybackState();
        }

        public void StopPlayback()
        {
            this._player?.Stop();
        }

        public void MovePlaybackPosition(double positionInSamples)
        {
            this.EnsurePlayerInitialized();
            this._player.MovePlaybackPosition(positionInSamples);
        }

        private void EnsurePlayerInitialized()
        {
            if (this._player == null)
                throw new InvalidOperationException("ファイルが読み込まれていません。");
        }
    }
}
