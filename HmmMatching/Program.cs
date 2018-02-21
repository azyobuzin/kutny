using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Accord.Controls;
using Accord.Math;
using NAudio.Wave;
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
            const string scoreFileName = @"C:\Users\azyob\Documents\Visual Studio 2017\Projects\PitchDetector\TrainingData\東京電機大学校歌.ust";
            var (model, startState) = CreateHmm(scoreFileName);

            //using (var sw = new StreamWriter("hmm.js"))
            //    WriteVisDataTo(model, sw);
            //using (var sw = new StreamWriter("hmm.dot"))
            //    WriteDotTo(model, sw);
            // TODO: 全部のグラフを出力しても見えないので、1つの状態（と無音状態）からの行先を表す画像を出力するべきか

            var plots = new List<(int x, int y)>();

            var buffer = new ToneSeriesMatching.Buffer<PitchHmmEmission>(5);
            var stateProbabilities = new double[model.States.Count];
            stateProbabilities[startState.Index] = 1.0;
            var prevNoteIndex = -1;
            var observationIndex = 0;

            const string audioFileName = @"C:\Users\azyob\Documents\Visual Studio 2017\Projects\PitchDetector\TrainingData\校歌 2018-01-17 15-10-46.wav";
            foreach (var observation in ToObservations(LoadAudioFile(audioFileName, false)))
            {
                buffer.Push(observation);

                if (buffer.Count <= 1) continue;

                var full = buffer.Count == buffer.Capacity;

                var (path, ps) = model.ViterbiPath(stateProbabilities, buffer, 1);

                var lastState = path.Last();
                var currentNoteIndex = lastState.Value.ReportingNote?.Index ?? -1;

                if (currentNoteIndex != prevNoteIndex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    var note = lastState.Value.ReportingNote;
                    if (note != null)
                        Console.WriteLine("位置 {0} -> {1} ({2}, {3})", prevNoteIndex, currentNoteIndex, NoteName(note.NoteNumber % 12), note.Lyric);
                    else
                        Console.WriteLine("位置 {0} -> {1}", prevNoteIndex, currentNoteIndex);
                    Console.ResetColor();

                    plots.Add((observationIndex, currentNoteIndex));

                    prevNoteIndex = currentNoteIndex;
                }

                if (full)
                {
                    var total = ps.Sum();
                    for (var i = 0; i < ps.Length; i++)
                    {
                        var p = ps[i];
                        if (p < 0.0) throw new InvalidOperationException();
                        if (p > 0.0) ps[i] = p / total;
                    }
                    stateProbabilities = ps;
                }

                observationIndex++;
            }

            ScatterplotBox.Show("NoteIndex", plots.Select(t => (double)t.x).ToArray(), plots.Select(t => (double)t.y).ToArray()).Hold();
            //ScatterplotBox.Show("Position", plots.Select(t => (double)t.x).ToArray(), plots.Select(t => (double)matcher.Score[t.y].Position).ToArray());
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

                        yield return new UtauNote(index, position, length, noteNum, lyric);

                        index++;
                        position += length;
                    }
                }
            }
        }

        private static (HiddenMarkovModel<PitchHmmState, PitchHmmEmission>, Node) CreateHmm(string fileName)
        {
            var utauNotes = LoadUtauScript(fileName).ToArray();

            var stopwatch = Stopwatch.StartNew();

            var model = new HiddenMarkovModel<PitchHmmState, PitchHmmEmission>();
            var states = utauNotes.Where(x => !x.IsRestNote)
                .Select(x => model.AddState(new PitchHmmState($@"{x.Index}\n{NoteName(x.NoteNumber % 12)} {x.Lyric}", x), NoteProbability(x)))
                .ToArray();

            // 👇👇👇👇👇👇👇👇👇👇👇
            // TODO: リファクタリング
            // 👆👆👆👆👆👆👆👆👆👆👆

            var startState = model.AddState(new PitchHmmState("スタート", null), NoSoundStateEmissionProbability);

            // startState の接続先
            {
                // 自己ループ
                startState.AddIncommingEdge(startState, 0.6);
                // 最初のノートに移動
                states[0].AddIncommingEdge(startState, 0.4);
            }

            const double selfLoopProbability = 0.05;
            var skipProbabilitiesFromNoSound = new List<(Node, double)>();

            // 順番に接続
            for (var i = 0; i < states.Length - 1; i++)
            {
                var state = states[i];
                var note = state.Value.ReportingNote;
                var pNext = 1.0;

                // 自己ループの確率（音程変化の判定が過剰に反応してしまった場合）
                state.AddIncommingEdge(state, selfLoopProbability);
                pNext -= selfLoopProbability;

                // 音を飛ばす確率
                {
                    skipProbabilitiesFromNoSound.Clear();

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

                            var totalLength = (double)midStates.Sum(x => x.Value.ReportingNote.Length);
                            foreach (var midState in midStates)
                            {
                                var noteRate = (1.0 - nextPitchRate) * (midState.Value.ReportingNote.Length / totalLength);

                                // 前の音符の長さ次第で無音を経由する確率が決まる
                                var prevNote = utauNotes[midState.Value.ReportingNote.Index - 1];
                                var throughNoSoundProbability = ProbabilityOfNoSoundAfter(prevNote);

                                skipProbabilitiesFromNoSound.Add((midState, throughNoSoundProbability * noteRate * skipProbability));

                                var p = (1.0 - throughNoSoundProbability) * noteRate * skipProbability;
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
                        const int maxSkipLength = 960;
                        var maxSkipPosition = note.Position + note.Length + maxSkipLength; // 2分音符の長さまでは飛ばされる可能性アリ
                        var midStates = states.Skip(i + 2).TakeWhile(x => x.Value.ReportingNote.Position <= maxSkipPosition).ToArray();

                        if (midStates.Length > 0)
                        {
                            const double maxSkipProbability = 0.1;
                            var totalLength = (double)midStates.Sum(x => x.Value.ReportingNote.Length);
                            var skipProbability = maxSkipProbability * (totalLength / maxSkipLength);

                            foreach (var midState in midStates)
                            {
                                var noteRate = midState.Value.ReportingNote.Length / totalLength;
                                var prevNote = utauNotes[midState.Value.ReportingNote.Index - 1];
                                var throughNoSoundProbability = prevNote.IsRestNote
                                    ? ProbabilityOfNoSoundWhenRestNote(prevNote)
                                    : ProbabilityOfNoSoundAfter(note);

                                skipProbabilitiesFromNoSound.Add((midState, throughNoSoundProbability * noteRate * skipProbability));

                                var p = (1.0 - throughNoSoundProbability) * noteRate * skipProbability;
                                midState.AddIncommingEdge(state, p);
                                pNext -= p;
                            }
                        }
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

                    var noSoundState = model.AddState(new PitchHmmState($@"{note.Index}\n無", note), NoSoundStateEmissionProbability);
                    noSoundState.AddIncommingEdge(state, total);
                    pNext -= total;

                    // 接続
                    const double noSoundSelfLoopProbability = 0.3; // 無音状態をループする確率（雑音に反応してしまったり）
                    noSoundState.AddIncommingEdge(noSoundState, noSoundSelfLoopProbability);

                    var mul = (1.0 - noSoundSelfLoopProbability) / total;
                    startState.AddIncommingEdge(noSoundState, startProbablitiy * mul);
                    states[0].AddIncommingEdge(noSoundState, firstNoteProbability * mul);

                    if (firstNoteInMeasureProbability > 0.0)
                        firstStateInMeasure.AddIncommingEdge(noSoundState, firstNoteInMeasureProbability * mul);

                    if (firstNoteInPreviousMeasureProbability > 0.0)
                        firstStateInPreviousMeasure.AddIncommingEdge(noSoundState, firstNoteInPreviousMeasureProbability * mul);

                    if (nextThroughNoSoundProbability > 0.0)
                        states[i + 1].AddIncommingEdge(noSoundState, nextThroughNoSoundProbability * mul);

                    foreach (var (s, p) in skipProbabilitiesFromNoSound)
                        s.AddIncommingEdge(noSoundState, p * mul);
                }

                // 次につなぐ
                states[i + 1].AddIncommingEdge(state, pNext);
                Console.WriteLine("{0}: {1}", i, pNext);
            }

            // 最後の状態の接続
            {
                var lastState = states[states.Length - 1];
                lastState.AddIncommingEdge(lastState, selfLoopProbability);
                startState.AddIncommingEdge(lastState, 1.0 - selfLoopProbability);
            }

            stopwatch.Stop();
            Console.WriteLine($"HMM生成: {stopwatch.Elapsed.TotalMilliseconds}ms");

            model.VerifyTransitionProbabilities();

            return (model, startState);

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
            const double maxProbability = 0.2;

            // 短いときはほとんど休まない
            if (prevNote.Length <= minLength) return minProbability;

            // 長いほど確率が上がり、全音符なら 0.2
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

            // 長さ 720 程度で 0.65 に到達するくらいの確率
            const int maxLength = 720;
            const double minProbability = 0.3;
            const double maxProbability = 0.65;
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

        private static readonly string[] s_noteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B", "C" };
        private static string NoteName(int num) => s_noteNames[num];

        private static void WriteDotTo(HiddenMarkovModel<PitchHmmState, PitchHmmEmission> model, TextWriter writer)
        {
            writer.WriteLine("digraph {");

            foreach (var state in model.States)
            {
                var label = state.Value.Label;
                writer.WriteLine("    {0} [label=\"{1}\", fontname=Meiryo]", state.Index, label);
            }

            writer.WriteLine("    { rank=same; " + string.Join("; ", model.States.Where(x => x.Value.Label.IndexOf('無') < 0).Select(x => x.Index)) + " }");

            var stateCount = model.States.Count;
            for (var from = 0; from < stateCount; from++)
            {
                for (var to = 0; to < stateCount; to++)
                {
                    var p = model.GetTransitionProbability(from, to);
                    if (p != 0.0)
                    {
                        writer.WriteLine(
                            "    {0} -> {1} [label=\"{2}\"]",
                            model.States[from].Index,
                            model.States[to].Index,
                            p
                        );
                    }
                }
            }

            writer.WriteLine("}");
        }

        private static void WriteVisDataTo(HiddenMarkovModel<PitchHmmState, PitchHmmEmission> model, TextWriter writer)
        {
            writer.WriteLine("var data = {");
            writer.WriteLine("    nodes: new vis.DataSet([");

            foreach (var state in model.States)
            {
                var label = state.Value.Label;
                var group = label == "スタート" ? "スタート"
                    : label.Contains("無") ? "無"
                    : "音符";
                writer.WriteLine(
                    "        {{ id: {0}, label: '{1}', group: '{2}', level: {3}, x: {4} }},",
                    state.Index,
                    label,
                    group,
                    group == "スタート" ? 0 : group == "無" ? 2 : 1,
                    (state.Value.ReportingNote?.Index + 1) ?? 0
                );
            }

            writer.WriteLine("    ]),");
            writer.WriteLine("    edges: new vis.DataSet([");

            var stateCount = model.States.Count;
            for (var from = 0; from < stateCount; from++)
            {
                for (var to = 0; to < stateCount; to++)
                {
                    var p = model.GetTransitionProbability(from, to);
                    if (p != 0.0)
                    {
                        writer.WriteLine(
                            "        {{ from: {0}, to: {1}, arrows: 'to', label: '{2}' }},",
                            model.States[from].Index,
                            model.States[to].Index,
                            p
                        );
                    }
                }
            }

            writer.WriteLine("    ])");
            writer.WriteLine("};");
        }

        private static IEnumerable<PitchUnit> LoadAudioFile(string fileName, bool play)
        {
            using (var playerReader = new AudioFileReader(fileName))
            using (var player = new WaveOutEvent())
            {
                if (play)
                {
                    player.Init(playerReader);
                    player.Play();
                }

                var startTime = Environment.TickCount;

                const int analysisUnit = 4096;
                const int pitchWindowSize = 1024;

                using (var reader = new AudioFileReader(fileName))
                {
                    var provider = reader.ToSampleProvider().ToMono();
                    var sampleRate = provider.WaveFormat.SampleRate;
                    var samples = new float[analysisUnit];

                    for (var unitIndex = 0; ; unitIndex++)
                    {
                        if (play)
                        {
                            var waitTime = (int)(startTime + unitIndex * analysisUnit * 1000.0 / sampleRate) - Environment.TickCount;
                            if (waitTime > 0) Thread.Sleep(waitTime);
                        }

                        for (var readSamples = 0; readSamples < samples.Length;)
                        {
                            var count = provider.Read(samples, readSamples, samples.Length - readSamples);
                            if (count == 0) yield break;
                            readSamples += count;
                        }

                        // 実効値を求める
                        var squared = 0.0;
                        for (var i = 0; i < samples.Length; i++)
                            squared += samples[i] * samples[i];
                        var rms = Math.Sqrt(squared / samples.Length);

                        // 512 ずつずらしながらピッチ検出
                        const int pitchOffsetDelta = 512;
                        var f0s = new List<double>((analysisUnit - pitchOffsetDelta) / pitchOffsetDelta);
                        for (var offset = 0; offset <= analysisUnit - pitchWindowSize; offset += pitchOffsetDelta)
                        {
                            var f = McLeodPitchMethod.EstimateFundamentalFrequency(
                                sampleRate,
                                new ReadOnlySpan<float>(samples, offset, pitchWindowSize)
                            );

                            if (f.HasValue) f0s.Add(f.Value);
                        }

                        if (f0s.Count == 0) continue;

                        f0s.Sort();
                        var f0 = f0s[f0s.Count / 2]; // 中央値

                        var normalizedPitch = NormalizePitch(f0);
                        if (normalizedPitch.HasValue)
                        {
                            yield return new PitchUnit(unitIndex, rms, normalizedPitch.Value);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// <see cref="PitchUnit.NormalizedPitch"/> で使う値に変換
        /// </summary>
        private static double? NormalizePitch(double f)
        {
            var noteNum = 69.0 + 12.0 * Math.Log(f / 440.0, 2.0);
            if (noteNum < 0 || noteNum > 127) return null;
            return noteNum - 12.0 * Math.Floor(noteNum / 12.0);
        }

        private static IEnumerable<PitchHmmEmission> ToObservations(IEnumerable<PitchUnit> source)
        {
            using (var enumerator = source.GetEnumerator())
            {
                if (!enumerator.MoveNext()) yield break;
                var prev = enumerator.Current;

                yield return PitchHmmEmission.Silent;

                // 状態: 音声なし
                SoundOff:
                {
                    const double rmsThreshold = 2.0; // 音量が 2 倍になったら音声ありと判断
                    const int checkCount = 2; // 2 回 rmsThreshold を満たしていたら音声ありと判断

                    while (true)
                    {
                        PitchUnit current;
                        var i = 0;

                        while (true)
                        {
                            if (!enumerator.MoveNext()) yield break;
                            current = enumerator.Current;

                            if (current.Rms > prev.Rms * rmsThreshold)
                            {
                                if (++i == checkCount)
                                {
                                    Console.WriteLine("{0}: SoundOff -> SoundOn ({1})", current.UnitIndex, NoteName((int)Math.Round(current.NormalizedPitch)));
                                    prev = current;
                                    yield return new PitchHmmEmission(prev.NormalizedPitch);
                                    goto SoundOn;
                                }
                            }
                            else
                            {
                                break;
                            }
                        }

                        prev = current;
                    }
                }

                // 状態: 音声あり
                SoundOn:
                {
                    const double rmsThreshold = 0.5; // 音量が半分になったら音声なしと判断
                    const double pitchThreshold = 0.8; // 音高が 0.8 変化したら、音高が変わったと判断
                    const int checkCount = 2; // 2 回連続判断基準を満たしていることが条件

                    while (true)
                    {
                        var rmsCount = 0;
                        var pitchCount = 0;

                        while (true)
                        {
                            if (!enumerator.MoveNext()) yield break;
                            var current = enumerator.Current;

                            var rmsChanged = current.Rms < prev.Rms * rmsThreshold;
                            if (rmsChanged)
                            {
                                if (++rmsCount == checkCount)
                                {
                                    Console.WriteLine("{0}: SoundOn -> SoundOff", current.UnitIndex);
                                    prev = current;
                                    yield return PitchHmmEmission.Silent;
                                    goto SoundOff;
                                }
                            }
                            else
                            {
                                rmsCount = 0;
                            }

                            var pitchDifference = Math.Min(
                                Math.Abs(prev.NormalizedPitch - current.NormalizedPitch),
                                prev.NormalizedPitch + 12 - current.NormalizedPitch
                            );
                            var roundedPrevPitch = (int)Math.Round(prev.NormalizedPitch);
                            if (roundedPrevPitch == 12) roundedPrevPitch = 0;
                            var roundedCurrentPitch = (int)Math.Round(current.NormalizedPitch);
                            if (roundedCurrentPitch == 12) roundedCurrentPitch = 0;
                            var pitchChanged = pitchDifference > pitchThreshold
                                && roundedPrevPitch != roundedCurrentPitch; // 四捨五入したときの音高が変化していることをチェック
                            if (pitchChanged)
                            {
                                if (++pitchCount == checkCount)
                                {
                                    Console.WriteLine("{0}: 音高 {1} -> {2}", current.UnitIndex, NoteName((int)Math.Round(prev.NormalizedPitch)), NoteName((int)Math.Round(current.NormalizedPitch)));
                                    prev = current;
                                    yield return new PitchHmmEmission(prev.NormalizedPitch);
                                    break;
                                }
                            }
                            else
                            {
                                pitchCount = 0;
                            }

                            if (!rmsChanged && !pitchChanged)
                            {
                                prev = current;
                                break;
                            }
                        }
                    }
                }
            }
        }
    }
}
