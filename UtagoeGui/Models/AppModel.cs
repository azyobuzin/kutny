using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using NAudio.Wave;
using PitchDetector;

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
        void OpenCorrectScoreWindow();
        void CloseCorrectScoreWindow();
        void OpenCorrectScore(string fileName);
        void MoveCorrectScoreStartPosition(double startPositionInAnalysisUnits);
        void AddTempoSetting(double tempo, int position);
        void RemoveTempoSetting(int index);
        void CloseCorrectScore();
    }

    public class AppModel : IAppActions
    {
        private readonly AppStore _store = new AppStore();
        public IAppStore Store => this._store;

        private bool _isInitialized;
        private VoiceAnalyzer _voiceAnalyzer;
        private PlayerModel _player;
        private double _correctScoreDefaultTempo;

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

                this.CalculateConcordanceRate();
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

        public void OpenCorrectScoreWindow()
        {
            this._store.IsEditingCorrectScore = true;
        }

        public void CloseCorrectScoreWindow()
        {
            this._store.IsEditingCorrectScore = false;
        }

        public void OpenCorrectScore(string fileName)
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
                        this._correctScoreDefaultTempo = double.Parse(reader.GetField("Tempo"), CultureInfo.InvariantCulture);
                        break;
                    }
                }

                tempoSetting = new TempoSetting(this._correctScoreDefaultTempo, 0);
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

            this._store.TempoSettings.Clear();
            this._store.TempoSettings.Add(tempoSetting);
            this.ApplyTempoSettings(correctNoteBlocks);

            this._store.CorrectScoreFileName = Path.GetFileName(fileName);
            this._store.CorrectScoreStartPositionInAnalysisUnits = 0;
            this._store.CorrectNoteBlocks = correctNoteBlocks;

            this.CalculateConcordanceRate();
        }

        public void MoveCorrectScoreStartPosition(double startPositionInAnalysisUnits)
        {
            this._store.CorrectScoreStartPositionInAnalysisUnits = startPositionInAnalysisUnits;
            this.ApplyTempoSettings(this._store.CorrectNoteBlocks);
            this.CalculateConcordanceRate();
        }

        public void AddTempoSetting(double tempo, int position)
        {
            this._store.TempoSettings.Add(new TempoSetting(tempo, position));
            this.ApplyTempoSettings(this._store.CorrectNoteBlocks);
            this.CalculateConcordanceRate();
        }

        public void RemoveTempoSetting(int index)
        {
            this._store.TempoSettings.RemoveAt(index);

            // 全部消されたか、初期値が入っていなかったらデフォルト値を代入
            if (this._store.TempoSettings.Count == 0 || this._store.TempoSettings[0].Position != 0)
                this._store.TempoSettings.Add(new TempoSetting(this._correctScoreDefaultTempo, 0));

            this.ApplyTempoSettings(this._store.CorrectNoteBlocks);
            this.CalculateConcordanceRate();
        }

        private void ApplyTempoSettings(ImmutableArray<CorrectNoteBlockModel> noteBlocks)
        {
            this.EnsurePlayerInitialized(); // ConvertTicksToAnalysisUnits で使うので

            var startPos = this._store.CorrectScoreStartPositionInAnalysisUnits;
            var tempoSettingsSnapshot = this._store.TempoSettings.ToArray();

            if (tempoSettingsSnapshot.Length == 0)
                throw new InvalidOperationException();

            foreach (var x in noteBlocks)
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

                    if (nextTempoSetting == null || position < nextTempoSetting.Position)
                    {
                        return baseTime + ConvertTicksToAnalysisUnits(position - currentTempoSetting.Position, currentTempoSetting.Tempo);
                    }

                    baseTime += ConvertTicksToAnalysisUnits(nextTempoSetting.Position - currentTempoSetting.Position, currentTempoSetting.Tempo);
                }
            }

            double ConvertTicksToAnalysisUnits(int ticks, double tempo)
            {
                var secs = 60.0 / 480.0 / tempo * ticks;
                return this._player.SampleRate * secs / Logics.AnalysisUnit;
            }
        }

        private void CalculateConcordanceRate()
        {
            var correctNoteBlocks = this._store.CorrectNoteBlocks; // ソート済みと仮定

            if (correctNoteBlocks.IsDefaultOrEmpty)
            {
                // 正解データなし
                this._store.PitchConcordanceRate = 0;
                this._store.VowelConcordanceRate = 0;
                this._store.VowelConcordanceRate2 = 0;
                return;
            }

            var checkedCount = 0;
            var pitchMatchedCount = 0;
            var vowelMatchedCount = 0;
            var vowelMatchedCount2 = 0;

            foreach (var noteBlock in this._store.VoiceAnalysisResult.NoteBlocks)
            {
                for (var i = 0; i < noteBlock.Span; i++)
                {
                    var correctNoteBlock = BinarySearch(noteBlock.Start + i + 0.5);

                    if (correctNoteBlock != null) // 該当するノートがない場合は毒にも薬にもしない
                    {
                        checkedCount++;

                        // オクターブ差は考慮しない
                        if (noteBlock.NoteNumber % 12 == correctNoteBlock.NoteNumber % 12)
                            pitchMatchedCount++;

                        if (noteBlock.VowelType == correctNoteBlock.VowelType)
                        {
                            vowelMatchedCount++;
                            vowelMatchedCount2++;
                        }
                        else if ((noteBlock.VowelType == VowelType.U || noteBlock.VowelType == VowelType.N)
                            && (correctNoteBlock.VowelType == VowelType.U || correctNoteBlock.VowelType == VowelType.N))
                        {
                            // 「う」と「ん」の区別なしでの結果
                            vowelMatchedCount2++;
                        }
                    }
                }
            }

            if (checkedCount == 0)
            {
                // 分母が 0 になるので特別扱い
                this._store.PitchConcordanceRate = 0;
                this._store.VowelConcordanceRate = 0;
                this._store.VowelConcordanceRate2 = 0;
                return;
            }

            this._store.PitchConcordanceRate = 100.0 * pitchMatchedCount / checkedCount;
            this._store.VowelConcordanceRate = 100.0 * vowelMatchedCount / checkedCount;
            this._store.VowelConcordanceRate2 = 100.0 * vowelMatchedCount2 / checkedCount;

            CorrectNoteBlockModel BinarySearch(double targetPosition)
            {
                var start = 0;
                var end = correctNoteBlocks.Length;
                while (start < end)
                {
                    var index = start + (end - start) / 2;
                    var correctNoteBlock = correctNoteBlocks[index];

                    if (targetPosition < correctNoteBlock.StartPositionInAnalysisUnits)
                    {
                        end = index;
                    }
                    else if (targetPosition > correctNoteBlock.StartPositionInAnalysisUnits + correctNoteBlock.LengthInAnalysisUnits)
                    {
                        start = index + 1;
                    }
                    else
                    {
                        return correctNoteBlock;
                    }
                }

                return null;
            }
        }

        public void CloseCorrectScore()
        {
            this._store.CorrectScoreFileName = null;
            this._store.CorrectNoteBlocks = ImmutableArray<CorrectNoteBlockModel>.Empty;
        }
    }
}
