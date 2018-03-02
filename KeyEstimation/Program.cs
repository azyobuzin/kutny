using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Accord.Controls;
using Accord.Math;
using Accord.Math.Transforms;
using Accord.Statistics;
using Kutny.Common;
using NAudio.Wave;

namespace KeyEstimation
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            var result1 = Run(provider =>
            {
                var result = new int[12];
                var pitches = EnumeratePitch(provider).ToArray();

                foreach (var (chroma, time) in Filter(pitches, provider.WaveFormat.SampleRate))
                {
                    //Console.WriteLine(CommonUtils.ToNoteName(chroma));
                    result[chroma]++;
                }

                return result.Select(x => (double)x).ToArray();
            });

            var result2 = Run(Pitch2);
            var result3 = Run(ChromaVectorFromNsdf);
            var result4 = Run(ChromaVectorFromNsdf2);
            var result5 = Run(PitchClassProfile);

            DataBarBox
                .Show(
                    new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" },
                    new[]
                    {
                        result1.Divide(result1.Sum()),
                        result2.Divide(result2.Sum()),
                        result3.Divide(result3.Sum()),
                        result4.Divide(result4.Sum()),
                        result5.Divide(result5.Sum())
                    }
                )
                .SetGraph(x =>
                {
                    var labels = new[]
                    {
                        "Pitch",
                        "Pitch2",
                        "NSDF",
                        "NSDF2",
                        "PCP"
                    };

                    for (var i = 0; i < labels.Length; i++)
                        x.CurveList[i].Label.Text = labels[i];
                })
                .Hold();
        }

        private static double[] Run(Func<ISampleProvider, double[]> action)
        {
            double[] result;

            var audioFileName = CommonUtils.GetTrainingFile("校歌 2018-01-17 15-10-46.wav");
            //var audioFileName = @"C:\Users\azyob\Documents\Jupyter\chroma\irony.wav";

            using (var reader = new AudioFileReader(audioFileName))
            {
                var provider = reader.ToSampleProvider().ToMono()
                    .Skip(TimeSpan.FromSeconds(5))
                    .Take(TimeSpan.FromSeconds(50.5))
                    ;
                var sampleRate = provider.WaveFormat.SampleRate;

                var stopwatch = Stopwatch.StartNew();
                result = action(provider);
                stopwatch.Stop();

                Console.WriteLine("{0} ms", stopwatch.Elapsed.Milliseconds);
            }

            return result;
        }

        private const int PitchWindowSize = 4096;

        private static IEnumerable<double?> EnumeratePitch(ISampleProvider provider)
        {
            var sampleRate = provider.WaveFormat.SampleRate;
            var samples = new float[PitchWindowSize];

            for (var unitIndex = 0; ; unitIndex++)
            {
                for (var readSamples = 0; readSamples < samples.Length;)
                {
                    var count = provider.Read(samples, readSamples, samples.Length - readSamples);
                    if (count == 0) yield break;
                    readSamples += count;
                }

                var f0 = McLeodPitchMethod.EstimateFundamentalFrequency(sampleRate, samples);
                yield return f0.HasValue ? 69.0 + 12.0 * Math.Log(f0.Value / 440.0, 2.0) : (double?)null;
            }
        }

        private static IEnumerable<(int, int)> Filter(IReadOnlyList<double?> noteNums, int sampleRate)
        {
            // 0.2 秒以上同じ音高を保っていることを確認する
            const double checkSecs = 0.2;
            var timesToCheck = (int)Math.Ceiling(sampleRate * checkSecs / PitchWindowSize);

            var maxI = noteNums.Count - timesToCheck;
            for (var i = 0; i <= maxI;)
            {
                var e = noteNums.Skip(i).Take(timesToCheck);

                // null があるなら諦める
                if (e.All(x => x.HasValue))
                {
                    var values = e.Select(x => x.Value).ToArray();
                    Array.Sort(values);

                    var min = values[0];
                    var max = values[values.Length - 1];
                    var med = values.Median(alreadySorted: true, inPlace: true);

                    const double margin = 1.0;
                    if (med - min < margin && max - med < margin)
                    {
                        var m = (int)Math.Round(med) % 12;
                        yield return (m < 0 ? m + 12 : m, i + timesToCheck - 1);

                        i += timesToCheck;
                        continue;
                    }
                }

                i++;
            }
        }

        private static double[] Pitch2(ISampleProvider provider)
        {
            var chromaVector = new double[12];

            var sampleRate = provider.WaveFormat.SampleRate;
            var samples = new float[PitchWindowSize];

            while (true)
            {
                for (var readSamples = 0; readSamples < samples.Length;)
                {
                    var count = provider.Read(samples, readSamples, samples.Length - readSamples);
                    if (count == 0) return chromaVector;
                    readSamples += count;
                }

                var rms = 0.0;
                foreach (var x in samples)
                    rms += x * x;
                rms = Math.Sqrt(rms / PitchWindowSize);

                var f0 = McLeodPitchMethod.EstimateFundamentalFrequency(sampleRate, samples);

                if (f0.HasValue)
                {
                    var noteNum = 69.0 + 12.0 * Math.Log(f0.Value / 440.0, 2.0);
                    var m = (int)Math.Round(noteNum) % 12;
                    if (m < 0) m += 12;

                    chromaVector[m] += rms;
                }
            }
        }

        private static double[] ChromaVectorFromNsdf(ISampleProvider provider)
        {
            var chromaVector = new double[12];

            var sampleRate = provider.WaveFormat.SampleRate;
            var samples = new float[PitchWindowSize];

            while (true)
            {
                for (var readSamples = 0; readSamples < samples.Length;)
                {
                    var count = provider.Read(samples, readSamples, samples.Length - readSamples);
                    if (count == 0) return chromaVector;
                    readSamples += count;
                }

                var rms = 0.0;
                foreach (var x in samples)
                    rms += x * x;
                rms = Math.Sqrt(rms / PitchWindowSize);

                var peaks = McLeodPitchMethod.FindPeaks(
                    McLeodPitchMethod.NormalizedSquareDifference(samples),
                    out _
                );

                foreach (var peak in peaks)
                {
                    var f = sampleRate / peak.Delay;
                    var noteNum = 69.0 + 12.0 * Math.Log(f / 440.0, 2.0);
                    var m = (int)Math.Round(noteNum) % 12;
                    if (m < 0) m += 12;

                    chromaVector[m] += peak.Correlation * rms;
                }
            }
        }

        private static double[] ChromaVectorFromNsdf2(ISampleProvider provider)
        {
            var chromaVector = new double[12];

            var sampleRate = provider.WaveFormat.SampleRate;
            var samples = new float[PitchWindowSize];

            while (true)
            {
                for (var readSamples = 0; readSamples < samples.Length;)
                {
                    var count = provider.Read(samples, readSamples, samples.Length - readSamples);
                    if (count == 0) return chromaVector;
                    readSamples += count;
                }

                var rms = 0.0;
                foreach (var x in samples)
                    rms += x * x;
                rms = Math.Sqrt(rms / PitchWindowSize);

                var peaks = McLeodPitchMethod.FindPeaks(
                    McLeodPitchMethod.NormalizedSquareDifference(samples),
                    out _
                );

                if (peaks.Count == 0) continue;

                // 基本周波数までのピークをカウント
                var threshold = peaks.Max(x => x.Correlation) * 0.8;
                var endIndex = peaks.FindIndex(x => x.Correlation >= threshold);

                for (var i = 0; i <= endIndex; i++)
                {
                    var peak = peaks[i];
                    var f = sampleRate / peak.Delay;
                    var noteNum = 69.0 + 12.0 * Math.Log(f / 440.0, 2.0);
                    var m = (int)Math.Round(noteNum) % 12;
                    if (m < 0) m += 12;

                    chromaVector[m] += peak.Correlation * rms;
                }
            }
        }

        private static double[] PitchClassProfile(ISampleProvider provider)
        {
            var chromaVector = new double[12];

            var sampleRate = provider.WaveFormat.SampleRate;
            var samples = new float[PitchWindowSize];

            while (true)
            {
                for (var readSamples = 0; readSamples < samples.Length;)
                {
                    var count = provider.Read(samples, readSamples, samples.Length - readSamples);
                    if (count == 0) return chromaVector;
                    readSamples += count;
                }

                var rms = 0.0;
                foreach (var x in samples)
                    rms += x * x;
                rms = Math.Sqrt(rms / PitchWindowSize);

                var spectrum = Array.ConvertAll(samples, x => (double)x);
                var imag = new double[PitchWindowSize];

                FourierTransform2.FFT(spectrum, imag, FourierTransform.Direction.Forward);

                for (var i = 0; i < spectrum.Length; i++)
                    spectrum[i] = spectrum[i] * spectrum[i] + imag[i] * imag[i];

                for (var i = 1; i < PitchWindowSize / 2; i++)
                {
                    var f = (double)(i * sampleRate) / PitchWindowSize;
                    var noteNum = 69.0 + 12.0 * Math.Log(f / 440.0, 2.0);
                    var m = (int)Math.Round(noteNum) % 12;
                    if (m < 0) m += 12;

                    chromaVector[m] += 1;
                }
            }
        }
    }
}
