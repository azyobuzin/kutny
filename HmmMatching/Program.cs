using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Accord.Controls;
using Accord.Math;
using NAudio.Wave;
using McLeodPitchMethod = ToneSeriesMatching.McLeodPitchMethod;
using PitchUnit = ToneSeriesMatching.PitchUnit;
using UtauScriptReader = ToneSeriesMatching.UtauScriptReader;

namespace HmmMatching
{
    using State = HmmState<PitchHmmState, PitchHmmEmission>;

    public static class Program
    {
        public static void Main(string[] args)
        {
            const string scoreFileName = @"C:\Users\azyob\Documents\Visual Studio 2017\Projects\PitchDetector\TrainingData\東京電機大学校歌.ust";
            var startState = CreateHmm(scoreFileName);

            //OutputGraphs(startState.Model);

            var plots = new List<(int x, int y)>();

            var tracker = new SingingPositionTracker(startState);
            var prevNoteIndex = -1;
            var observationIndex = 0;

            const string audioFileName = @"C:\Users\azyob\Documents\Visual Studio 2017\Projects\PitchDetector\TrainingData\校歌 2018-01-17 15-10-46.wav";
            foreach (var observation in ToObservations(LoadAudioFile(audioFileName, false)))
            {
                tracker.InputObservation(observation);

                var note = tracker.CurrentNote;
                var currentNoteIndex = note?.Index ?? -1;

                if (currentNoteIndex != prevNoteIndex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    if (note != null)
                        Console.WriteLine("位置 {0} -> {1} ({2}, {3})", prevNoteIndex, currentNoteIndex, Utils.ToNoteName(note.NoteNumber % 12), note.Lyric);
                    else
                        Console.WriteLine("位置 {0} -> {1}", prevNoteIndex, currentNoteIndex);
                    Console.ResetColor();

                    plots.Add((observationIndex, currentNoteIndex));

                    prevNoteIndex = currentNoteIndex;
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

        private static State CreateHmm(string fileName)
        {
            var utauNotes = LoadUtauScript(fileName).ToArray();


            var stopwatch = Stopwatch.StartNew();

            var startState = PitchHmmGenerator.Default.Generate(utauNotes);

            stopwatch.Stop();
            Console.WriteLine($"HMM生成: {stopwatch.Elapsed.TotalMilliseconds}ms");

            startState.Model.VerifyEdges();

            return startState;
        }

        private static void WriteDotTo(IEnumerable<HmmState<PitchHmmState, PitchHmmEmission>> states, TextWriter writer)
        {
            writer.WriteLine("digraph {");

            var statesInGraph = new HashSet<HmmState<PitchHmmState, PitchHmmEmission>>();

            foreach (var state in states)
            {
                statesInGraph.Add(state);

                foreach (var (to, lp) in state.LogProbabilitiesByOutgoingStateIndexes)
                {
                    statesInGraph.Add(state.Model.States[to]);

                    writer.WriteLine(
                        "    {0} -> {1} [label=\"{2}\"]",
                        state.Index,
                        to,
                        Math.Exp(lp)
                    );
                }
            }

            foreach (var state in statesInGraph.OrderBy(x => x.Index))
            {
                var label = state.Value.ToString().Replace("\n", "\\n");
                writer.WriteLine("    {0} [label=\"{1}\", fontname=Meiryo]", state.Index, label);
            }

            writer.WriteLine("}");
        }

        private static void OutputGraphs(HiddenMarkovModel<PitchHmmState, PitchHmmEmission> model)
        {
            Directory.CreateDirectory("graphs");

            var maxDegreeOfParallelism = Environment.ProcessorCount;
            var semaphore = new SemaphoreSlim(maxDegreeOfParallelism, maxDegreeOfParallelism);
            var dotTasks = new List<Task>();

            foreach (var g in model.States.GroupBy(x => x.Value.ReportingNote))
            {
                semaphore.Wait();

                var fileName = "graphs/" + (g.Key?.Index.ToString() ?? "start") + ".png";
                Console.WriteLine("dot: " + fileName);

                var process = new Process()
                {
                    StartInfo = new ProcessStartInfo("dot", "-Tpng -o" + fileName)
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardInput = true
                    },
                    EnableRaisingEvents = true
                };

                var tcs = new TaskCompletionSource<int>();
                process.Exited += (_, __) =>
                {
                    semaphore.Release();
                    tcs.TrySetResult(process.ExitCode);
                };

                process.Start();

                using (var stdin = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false)))
                    WriteDotTo(g, stdin);

                dotTasks.Add(tcs.Task);
            }

            Task.WaitAll(dotTasks.ToArray());
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
                                    Console.WriteLine("{0}: SoundOff -> SoundOn ({1})", current.UnitIndex, Utils.ToNoteName((int)Math.Round(current.NormalizedPitch)));
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
                                    Console.WriteLine("{0}: 音高 {1} -> {2}", current.UnitIndex, Utils.ToNoteName((int)Math.Round(prev.NormalizedPitch)), Utils.ToNoteName((int)Math.Round(current.NormalizedPitch)));
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
