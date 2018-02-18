using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Accord.Math;
using McLeodPitchMethod = ToneSeriesMatching.McLeodPitchMethod;
using PitchUnit = ToneSeriesMatching.PitchUnit;
using UtauScriptReader = ToneSeriesMatching.UtauScriptReader;

namespace HmmMatching
{
    using Node = HmmState<PitchHmmState, PitchHmmEmission>;

    public static class Program
    {
        public static void Main(string[] args)
        {
        }

        private static IEnumerable<UtauNote> LoadUtauScript(string fileName)
        {
            using (var reader = new UtauScriptReader(File.OpenRead(fileName)))
            {
                // ノート取得
                var position = 0;
                var index = 0;
                while (true)
                {
                    if (!reader.ReadSection())
                        throw new EndOfStreamException();

                    if (reader.SectionName == "#TRACKEND")
                        break;

                    if (Regex.IsMatch(reader.SectionName, "^#[0-9]+$"))
                    {
                        var lyric = reader.GetField("Lyric");
                        var length = int.Parse(reader.GetField("Length"), CultureInfo.InvariantCulture);
                        var noteNum = lyric == "R"
                            ? -1 // R は休符
                            : int.Parse(reader.GetField("NoteNum"), CultureInfo.InvariantCulture);

                        yield return new UtauNote(index, position, length, noteNum);

                        index++;
                        position += length;
                    }
                }
            }
        }

        private static HiddenMarkovModel<PitchHmmState, PitchHmmEmission> CreateHmm(string fileName)
        {
            var utauNotes = LoadUtauScript(fileName).ToArray();
            var model = new HiddenMarkovModel<PitchHmmState, PitchHmmEmission>();
            var states = utauNotes.Where(x => !x.IsRestNote)
                .Select(x => model.AddState(new PitchHmmState(x), NoteProbability(x)))
                .ToArray();

            var startState = model.AddState(new PitchHmmState(null), NoSoundStateEmissionProbability);

            // startState の接続先
            {
                // 自己ループ
                startState.AddIncommingEdge(startState, 0.6);
                // 最初のノートに移動
                states[0].AddIncommingEdge(startState, 0.4);
            }

            // 順番に接続
            for (var i = 0; i < states.Length; i++)
            {
                var state = states[i];
                var note = state.Value.ReportingNote;
                var pNext = 1.0;

                // 自己ループの確率（音程変化の判定が過剰に反応してしまった場合）
                const double selfLoopProbability = 0.05;
                state.AddIncommingEdge(state, selfLoopProbability);
                pNext -= selfLoopProbability;

                // 音を飛ばす確率
                var skipProbabilitiesFromNoSound = new List<(Node, double)>();
                {
                    var samePitchRange = utauNotes.Skip(note.Index)
                        .TakeWhile(x => x.NoteNumber == note.NoteNumber)
                        .Last().Index;

                    if (samePitchRange > note.Index
                        && samePitchRange < utauNotes.Length - 1
                        && Array.FindIndex(utauNotes, samePitchRange + 1, x => !x.IsRestNote) is var nextPitchNoteIndex
                        && nextPitchNoteIndex > samePitchRange // 次のノートの存在確認
                    )
                    {
                        // 同じ音高が連続するので、認識しにくい
                        const double skipProbability = 0.1;
                        // TODO: インデックスを使うのか参照を使うのかはっきりしろ
                        var nextPitchStateIndex = Array.FindIndex(states, i + 1, x => x.Value.ReportingNote.Index == nextPitchNoteIndex);
                        var nextPitchState = states[nextPitchStateIndex];
                        var midStates = new ArraySegment<Node>(states, i + 2, nextPitchStateIndex - (i + 2)); // i+1 は通常ルートで計算されるので i+2 から

                        double nextPitchRate;
                        if (midStates.Count == 0)
                        {
                            nextPitchRate = 1.0;
                        }
                        else
                        {
                            // nextPitchState までの距離があるほど、割合が下がる
                            const int maxLength = 960;
                            const double maxNextPitchRate = 0.5;
                            const double minNextPitchRate = 0.3;
                            var distanceToNextPitch = nextPitchState.Value.ReportingNote.Position - (note.Position + note.Length);
                            nextPitchRate = Math.Max(
                                maxNextPitchRate - distanceToNextPitch * ((maxNextPitchRate - minNextPitchRate) / maxLength),
                                minNextPitchRate
                            );

                            var totalLength = midStates.Sum(x => x.Value.ReportingNote.Length);
                            foreach (var midState in midStates)
                            {
                                // 前の音符の長さ次第で無音を経由する確率が決まる
                                var prevNote = utauNotes[midState.Value.ReportingNote.Index - 1];
                                var throughNoSoundProbability = ProbabilityOfNoSoundAfter(prevNote);

                                skipProbabilitiesFromNoSound.Add((midState, throughNoSoundProbability * (1.0 - nextPitchRate) * skipProbability));

                                var p = (1.0 - throughNoSoundProbability) * (1.0 - nextPitchRate) * skipProbability;
                                midState.AddIncommingEdge(state, p);
                                pNext -= p;
                            }
                        }

                        // 次の音高のノートへの確率
                        var previousNoteOfNextPitch = utauNotes[nextPitchNoteIndex - 1];
                        var nextPitchThroughNoSoundProbability = previousNoteOfNextPitch.IsRestNote
                            ? ProbabilityOfNoSoundWhenRestNote(previousNoteOfNextPitch) // 前が休符なので、無音状態を経由させる確率が高くなる
                            : ProbabilityOfNoSoundAfter(previousNoteOfNextPitch); // 前の音符の長さ次第で無音を経由する確率が決まる

                        skipProbabilitiesFromNoSound.Add((nextPitchState, nextPitchThroughNoSoundProbability * nextPitchRate * skipProbability));

                        var nextPitchProbability = (1.0 - nextPitchThroughNoSoundProbability) * nextPitchRate * skipProbability;
                        nextPitchState.AddIncommingEdge(state, nextPitchProbability);
                        pNext -= nextPitchProbability;
                    }
                    else
                    {
                        // 短い音は飛ばしやすい
                        // ****************************************
                        // TODO
                        // ****************************************
                    }
                }

                // 無音状態を経由するもの
                {
                    // 歌唱をやめてスタートに戻る確率
                    const double startProbablitiy = 0.007;

                    // スタート状態を経由せず最初のノートに移動する確率
                    const double firstNoteProbability = 0.003;

                    // 小節の最初に戻る確率
                    var measureStartPosition = note.Position - note.Position % 1920;
                    var firstStateInMeasure = Array.Find(states, x => x.Value.ReportingNote.Position >= measureStartPosition);
                    var firstNoteInMeasureProbability = firstStateInMeasure.Value.ReportingNote.Position < note.Position ? 0.012 : 0.0;

                    // 1 小節前の最初に戻る確率
                    var previousMeasureStartPosition = measureStartPosition - 1920;
                    var firstStateInPreviousMeasure = Array.Find(states, x => x.Value.ReportingNote.Position >= previousMeasureStartPosition);
                    var firstNoteInPreviousMeasureProbability = firstStateInPreviousMeasure.Value.ReportingNote.Position < measureStartPosition ? 0.01 : 0.0;

                    // 次の音に無音を経由する確率
                    var nextNote = NextNote(note);
                    double nextThroughNoSoundProbability;
                    if (nextNote == null)
                    {
                        // 次のノートがないので経由しようがない
                        nextThroughNoSoundProbability = 0.0;
                    }
                    else
                    {
                        nextThroughNoSoundProbability = nextNote.IsRestNote
                            ? ProbabilityOfNoSoundWhenRestNote(nextNote) // 次のノートが休符なら高い確率で無音を経由する
                            : ProbabilityOfNoSoundAfter(note); // 音符の長さ次第で無音を経由する確率が決まる
                    }

                    var total = startProbablitiy + firstNoteProbability + firstNoteInMeasureProbability + firstNoteInPreviousMeasureProbability + nextThroughNoSoundProbability
                        + skipProbabilitiesFromNoSound.Sum(x => x.Item2);

                    var noSoundState = model.AddState(new PitchHmmState(note), NoSoundStateEmissionProbability);
                    noSoundState.AddIncommingEdge(state, total);
                    pNext -= total;

                    // 接続
                    const double noSoundSelfLoopProbability = 0.3; // 無音状態をループする確率（雑音に反応してしまったり）
                    noSoundState.AddIncommingEdge(noSoundState, noSoundSelfLoopProbability);

                    var mul = total / (1.0 - noSoundSelfLoopProbability);
                    startState.AddIncommingEdge(noSoundState, startProbablitiy * mul);
                    states[0].AddIncommingEdge(noSoundState, firstNoteProbability * mul);

                    if (firstNoteInMeasureProbability > 0.0)
                        firstStateInMeasure.AddIncommingEdge(noSoundState, firstNoteInMeasureProbability * mul);

                    if (firstNoteInPreviousMeasureProbability > 0.0)
                        firstStateInPreviousMeasure.AddIncommingEdge(noSoundState, firstNoteInPreviousMeasureProbability * mul);

                    if (nextThroughNoSoundProbability > 0.0)
                        states[i + 1].AddIncommingEdge(noSoundState, nextThroughNoSoundProbability * mul);

                    foreach (var (s, p) in skipProbabilitiesFromNoSound)
                        s.AddIncommingEdge(noSoundState, p);
                }
            }

            // 最後の状態の接続
            // ****************************************
            // TODO
            // ****************************************

            return model;

            UtauNote NextNote(UtauNote x)
            {
                var i = x.Index + 1;
                return i < utauNotes.Length ? utauNotes[i] : null;
            }
        }

        /// <summary>
        /// <paramref name="prevNote"/> のあとに無音状態になる確率
        /// </summary>
        private static double ProbabilityOfNoSoundAfter(UtauNote prevNote)
        {
            // ノートが長いほど、そのあとは休みがち
            const int minLength = 480;
            const double minProbability = 0.05;
            const double maxProbability = 0.3;

            // 短いときはほとんど休まない
            if (prevNote.Length <= minLength) return minProbability;

            // 長いほど確率が上がり、全音符なら 0.3
            return Math.Min(
                minProbability + (prevNote.Length - minLength) * ((maxProbability - minProbability) / (1920 - minLength)),
                maxProbability
            );
        }

        /// <summary>
        /// <paramref name="restNote"/> を通過するときに無音状態になる確率
        /// </summary>
        private static double ProbabilityOfNoSoundWhenRestNote(UtauNote restNote)
        {
            if (!restNote.IsRestNote) throw new ArgumentException();

            // 長さ 720 程度で 0.7 に到達するくらいの確率
            const int maxLength = 720;
            const double minProbability = 0.3;
            const double maxProbability = 0.7;
            return Math.Min(
                minProbability + ((maxProbability - minProbability) / maxLength) * restNote.Length,
                maxProbability
            );
        }

        private static double NoSoundStateEmissionProbability(PitchHmmEmission emission)
        {
            // 無音 0.7、それ以外 0.3
            // 変化したタイミングで入力が来るので、無音でないパターンもそれなりに可能性があると考える
            return emission.IsSilent ? 0.7 : 0.3 / 12.0;
        }

        private static Func<PitchHmmEmission, double> NoteProbability(UtauNote note)
        {
            if (note.IsRestNote)
            {
                // 無音 0.95、それ以外 0.05
                return e => e.IsSilent ? 0.95 : 0.05 / 12.0;
            }

            // 正規分布
            const double stdDev = 0.5;
            var mean = note.NoteNumber % 12;
            return e => e.IsSilent ? 0.0
                : Math.Max(
                    Math.Max(
                        NormalDistribution(mean, stdDev, e.NormalizedPitch),
                        NormalDistribution(mean - 12, stdDev, e.NormalizedPitch)
                    ),
                    NormalDistribution(mean + 12, stdDev, e.NormalizedPitch)
                );
        }

        private static double NormalDistribution(double mean, double stdDev, double x)
        {
            return Normal.Function((x - mean) / stdDev);
        }
    }
}
