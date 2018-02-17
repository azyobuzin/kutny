using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Accord.Controls;
using NAudio.Wave;
using System.Threading;

namespace ToneSeriesMatching
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            const string scoreFileName = @"C:\Users\azyob\Documents\Visual Studio 2017\Projects\PitchDetector\TrainingData\東京電機大学校歌.ust";
            var matcher = new CompoundToneSeriesMatcher(LoadUtauScript(scoreFileName).ToImmutableArray(), 3);

            var plots = new List<(int x, int y)>();

            const string audioFileName = @"C:\Users\azyob\Documents\Visual Studio 2017\Projects\PitchDetector\TrainingData\校歌 2018-01-17 15-10-46.wav";
            foreach (var pitchUnit in FilterPitchUnits(LoadAudioFile(audioFileName, true)))
            {
                var prev = matcher.CurrentNoteIndex;
                matcher.InputPitch(pitchUnit);

                var current = matcher.CurrentNoteIndex;
                if (current != prev)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("{0}: 位置 {1} -> {2} ({3})", pitchUnit.UnitIndex, prev, current, NoteName(matcher.Score[current].NoteNumber));
                    Console.ResetColor();

                    plots.Add((pitchUnit.UnitIndex, current));
                }
            }

            ScatterplotBox.Show("NoteIndex", plots.Select(t => (double)t.x).ToArray(), plots.Select(t => (double)t.y).ToArray());
            ScatterplotBox.Show("Position", plots.Select(t => (double)t.x).ToArray(), plots.Select(t => (double)matcher.Score[t.y].Position).ToArray());
        }

        private static IEnumerable<UtauNote> LoadUtauScript(string fileName)
        {
            using (var reader = new UtauScriptReader(File.OpenRead(fileName)))
            {
                // ノート取得
                var position = 0;
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

                        if (lyric != "R") // R は休符
                        {
                            var noteNum = int.Parse(reader.GetField("NoteNum"), CultureInfo.InvariantCulture);
                            yield return new UtauNote(position, noteNum % 12);
                        }

                        position += length;
                    }
                }
            }
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

        /// <summary>
        /// 変化があった <see cref="PitchUnit"/> だけを出力する
        /// </summary>
        private static IEnumerable<PitchUnit> FilterPitchUnits(IEnumerable<PitchUnit> source)
        {
            using (var enumerator = source.GetEnumerator())
            {
                if (!enumerator.MoveNext()) yield break;
                var prev = enumerator.Current;

                // 状態: 音声なし
                NoSound:
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
                                    Console.WriteLine("{0}: NoSound -> InSound ({1})", current.UnitIndex, NoteName((int)Math.Round(current.NormalizedPitch)));
                                    prev = current;
                                    yield return prev;
                                    goto InSound;
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
                InSound:
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
                                    Console.WriteLine("{0}: InSound -> NoSound", current.UnitIndex);
                                    prev = current;
                                    goto NoSound;
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
                                    yield return prev;
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

        private static readonly string[] s_noteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B", "C" };
        private static string NoteName(int num) => s_noteNames[num];
    }
}
