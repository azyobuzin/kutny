using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using NAudio.Wave;

namespace UtagoeGui.Models
{
    public interface IAppActions
    {
        void Initialize();
        void ChangeVowelClassifier(VowelClassifierType type);
        void OpenAudioFile(string fileName);
        void TogglePlaybackState();
        void StopPlayback();
        void MovePlaybackPosition(double positionInSamples);
        void ZoomIn();
        void ZoomOut();
        void OpenCorrectData(string fileName);
        void AddTempoSetting(double tempo, int position);
        void RemoveTempoSetting(int index);
    }

    public class AppModel : IAppActions
    {
        private readonly AppStore _store = new AppStore();
        public IAppStore Store => this._store;

        private bool _isInitialized;
        private VoiceAnalyzer _voiceAnalyzer;
        private PlayerModel _player;
        private double _correctDataDefaultTempo;

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

        public void OpenAudioFile(string fileName)
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

        public void ZoomIn()
        {
            this._store.ScoreScale *= 2;
            this._store.CanZoomOut = true;
        }

        public void ZoomOut()
        {
            const double minimumScale = 0.25;

            var scale = Math.Max(
                this._store.ScoreScale / 2,
                minimumScale
            );

            this._store.ScoreScale = scale;
            this._store.CanZoomOut = Math.Abs(scale - minimumScale) > double.Epsilon;
        }

        public void OpenCorrectData(string fileName)
        {
            var correctNoteBlocksBuilder = ImmutableArray.CreateBuilder<CorrectNoteBlockModel>();
            TempoSetting tempoSetting;

            using (var reader = new UtauScriptReader(File.OpenRead(fileName)))
            {
                // テンポ情報を取得
                while (true)
                {
                    if (!reader.ReadSection())
                        throw new EndOfStreamException();

                    if (reader.SectionName == "#SETTING")
                    {
                        this._correctDataDefaultTempo = double.Parse(reader.GetField("Tempo"), CultureInfo.InvariantCulture);
                        break;
                    }
                }

                tempoSetting = new TempoSetting(this._correctDataDefaultTempo, 0);
                var tempoSettings = ImmutableArray.Create(tempoSetting);

                // ノート取得
                var position = 0;
                while (true)
                {
                    if (!reader.ReadSection())
                        throw new EndOfStreamException();

                    if (reader.SectionName == "#TRACKEND")
                        break;

                    if (Regex.IsMatch(reader.SectionName, "#[0-9]+"))
                    {
                        var lyric = reader.GetField("Lyric");
                        var length = int.Parse(reader.GetField("Length"), CultureInfo.InvariantCulture);

                        if (lyric != "R") // R は休符
                        {
                            var noteNum = int.Parse(reader.GetField("NoteNum"), CultureInfo.InvariantCulture);
                            correctNoteBlocksBuilder.Add(new CorrectNoteBlockModel(position, length, noteNum, lyric));
                        }

                        position += length;
                    }
                }
            }

            var correctNoteBlocks = correctNoteBlocksBuilder.Capacity == correctNoteBlocksBuilder.Count
                ? correctNoteBlocksBuilder.MoveToImmutable()
                : correctNoteBlocksBuilder.ToImmutable();

            this.ApplyTempoSettings(correctNoteBlocks);

            this._store.TempoSettings.Clear();
            this._store.TempoSettings.Add(tempoSetting);
            this._store.CorrectDataStartPositionInAnalysisUnits = 0;
            this._store.CorrectNoteBlocks = correctNoteBlocks;
        }

        public void AddTempoSetting(double tempo, int position)
        {
            this._store.TempoSettings.Add(new TempoSetting(tempo, position));
            this.ApplyTempoSettings(this._store.CorrectNoteBlocks);
        }

        public void RemoveTempoSetting(int index)
        {
            this._store.TempoSettings.RemoveAt(index);

            // 全部消されたか、初期値が入っていなかったらデフォルト値を代入
            if (this._store.TempoSettings.Count == 0 || this._store.TempoSettings[0].Position != 0)
                this._store.TempoSettings.Add(new TempoSetting(this._correctDataDefaultTempo, 0));

            this.ApplyTempoSettings(this._store.CorrectNoteBlocks);
        }

        private void ApplyTempoSettings(ImmutableArray<CorrectNoteBlockModel> noteBlocks)
        {
            var startPos = this._store.CorrectDataStartPositionInAnalysisUnits;
            var tempoSettingsSnapshot = this._store.TempoSettings.ToArray();

            if (tempoSettingsSnapshot.Length == 0)
                throw new InvalidOperationException();

            foreach (var x in this._store.CorrectNoteBlocks)
            {
                var start = PositionToAnalysisUnits(x.Start);
                var end = PositionToAnalysisUnits(x.Start + x.Length);
                x.StartPositionInAnalysisUnits = start;
                x.LengthInAnalysisUnits = end - start;
            }

            double PositionToAnalysisUnits(int position)
            {
                var baseTime = startPos;

                for (var i = 0; ; i++)
                {
                    var currentTempoSetting = tempoSettingsSnapshot[i];
                    var nextTempoSetting = i < tempoSettingsSnapshot.Length - 1 ? tempoSettingsSnapshot[i + 1] : null;

                    if (nextTempoSetting == null || nextTempoSetting.Position < position)
                    {
                        return baseTime + ConvertLengthToAnalysisUnits(position - currentTempoSetting.Position, currentTempoSetting.Tempo);
                    }

                    baseTime += ConvertLengthToAnalysisUnits(nextTempoSetting.Position - currentTempoSetting.Position, currentTempoSetting.Tempo);
                }
            }

            double ConvertLengthToAnalysisUnits(int length, double tempo)
            {
                var secs = 60.0 / 480.0 / tempo * length;
                return this._player.SampleRate * secs / Logics.AnalysisUnit;
            }
        }
    }
}
