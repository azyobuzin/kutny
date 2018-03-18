using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
            TimeStretch2();
        }

        private static (string, Func<ISampleProvider, double[]>)[] s_algorithms =
        {
            ("Pitch1", Pitch1),
            ("Pitch2", Pitch2),
            ("NSDF1", ChromaVectorFromNsdf),
            ("NSDF2", ChromaVectorFromNsdf2),
            ("PCP", PitchClassProfile),
            ("HPCP", Hpcp),
        };

        private static void FindKeysInFiles()
        {
            var files = new[]
            {
                @"C:\Users\azyob\Documents\Jupyter\chroma\BEYOND THE STARLIGHT.wav",
                @"C:\Users\azyob\Documents\Jupyter\chroma\BEYOND THE STARLIGHT2.wav",
                @"C:\Users\azyob\Documents\Jupyter\chroma\irony.wav",
                @"C:\Users\azyob\Documents\Jupyter\chroma\irony2.wav",
                @"C:\Users\azyob\Documents\Jupyter\chroma\LoveDestiny.wav",
                @"C:\Users\azyob\Documents\Jupyter\chroma\ONLY MY NOTE.wav",
                @"C:\Users\azyob\Documents\Jupyter\chroma\きっと、また逢える….wav",
                @"C:\Users\azyob\Documents\Jupyter\chroma\校歌.wav",
                @"C:\Users\azyob\Documents\Jupyter\chroma\情熱ファンファンファーレ.wav",
                @"C:\Users\azyob\Documents\Jupyter\chroma\負けないで.wav",
            };

            var results = new Key[files.Length * s_algorithms.Length];

            Parallel.For(0, results.Length, i =>
            {
                var file = files[i / s_algorithms.Length];
                var algorithm = s_algorithms[i % s_algorithms.Length].Item2;

                double[] chromaVector;
                using (var reader = new AudioFileReader(file))
                {
                    var provider = reader.ToSampleProvider().ToMono();
                    chromaVector = algorithm(provider);
                }

                results[i] = KeyFinding.FindKey(chromaVector);
            });

            Console.Write("ファイル");
            Console.WriteLine(string.Concat(s_algorithms.Select(x => "," + x.Item1)));

            for (var i = 0; i < files.Length; i++)
            {
                Console.Write("{0}", Path.GetFileName(files[i]));

                for (var j = 0; j < s_algorithms.Length; j++)
                {
                    Console.Write("," + results[s_algorithms.Length * i + j].ToString());
                }

                Console.WriteLine();
            }
        }

        private static void ChromaVectorGraph()
        {
            var results = s_algorithms.Select(x => Run(x.Item2)).ToArray();
            var audioFileName = results[0].Item2;

            DataBarBox
                .Show(
                    new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" },
                    Array.ConvertAll(results, x => x.Item1.Divide(x.Item1.Sum()))
                )
                .SetTitle(audioFileName)
                .SetGraph(x =>
                {
                    for (var i = 0; i < s_algorithms.Length; i++)
                        x.CurveList[i].Label.Text = s_algorithms[i].Item1;
                })
                .Hold();
        }

        private static (double[], string) Run(Func<ISampleProvider, double[]> action)
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

                Console.WriteLine("{0} ms", stopwatch.Elapsed.TotalMilliseconds);
            }

            return (result, Path.GetFileName(audioFileName));
        }

        private const int PitchWindowSize = 1024;

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

        private static double[] Pitch1(ISampleProvider provider)
        {
            var result = new int[12];
            var pitches = EnumeratePitch(provider).ToArray();

            foreach (var (chroma, time) in Filter(pitches, provider.WaveFormat.SampleRate))
            {
                //Console.WriteLine(CommonUtils.ToNoteName(chroma));
                result[chroma]++;
            }

            return result.Select(x => (double)x).ToArray();
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

        private static double[] Hpcp(ISampleProvider provider)
        {
            var samples = new float[1048576];
            var readSamples = 0;

            while (true)
            {
                var count = provider.Read(samples, readSamples, samples.Length - readSamples);
                if (count == 0) break;
                readSamples += count;
                if (readSamples == samples.Length)
                    Array.Resize(ref samples, readSamples * 2);
            }

            var hpcp = HarmonicPitchClassProfile.AverageHpcp(
                new ReadOnlySpan<float>(samples, 0, readSamples),
                provider.WaveFormat.SampleRate,
                12,
                estimateReferenceFrequency: true
            );

            // C を最初に持ってくる
            var chromaVector = new double[12];
            for (var i = 0; i < chromaVector.Length; i++)
                chromaVector[i] = hpcp[(i + 3) % 12];

            return chromaVector;
        }

        private static void TimeStretch()
        {
            var audioFileName = CommonUtils.GetTrainingFile("校歌 2018-01-17 15-10-46.wav");

            using (var reader = new AudioFileReader(audioFileName))
            {
                var provider = reader.ToSampleProvider().ToMono();
                var timeStretcher = new PsolaWithMpm();
                var samples = new float[4096];

                using (var writer = new WaveFileWriter("timestretch.wav", new WaveFormat(provider.WaveFormat.SampleRate, 1)))
                {
                    while (true)
                    {
                        for (var readSamples = 0; readSamples < samples.Length;)
                        {
                            var count = provider.Read(samples, readSamples, samples.Length - readSamples);
                            if (count == 0) return;
                            readSamples += count;
                        }

                        var delay = McLeodPitchMethod.EstimateFundamentalDelay(samples);
                        var result = timeStretcher.Stretch(samples, delay, 1.3);

                        writer.WriteSamples(result, 0, result.Length);
                    }
                }
            }
        }

        private static void TimeStretch2()
        {
            var audioFileName = CommonUtils.GetTrainingFile("校歌 2018-01-17 15-10-46.wav");

            using (var reader = new AudioFileReader(audioFileName))
            {
                var provider = reader.ToSampleProvider().ToMono()/*.Skip(TimeSpan.FromSeconds(7))*/;

                const int frameSize = 1024;
                const int stretchedTime = (int)(frameSize * 1.3);
                const double minPitchMilliseconds = 2;
                const double maxPitchMilliseconds = 10;
                const double templateSizeMilliseconds = 2;

                int MsToSamples(double ms) => (int)(provider.WaveFormat.SampleRate * (ms / 1000.0));

                var timeStretcher = new TimeStretcherWithAutocorrelation2(
                    MsToSamples(minPitchMilliseconds),
                    MsToSamples(maxPitchMilliseconds),
                    MsToSamples(templateSizeMilliseconds)
                );

                var samples = new float[frameSize];

                using (var writer = new WaveFileWriter("timestretch2.wav", new WaveFormat(provider.WaveFormat.SampleRate, 1)))
                //using (var writer2 = new WaveFileWriter("nostretch.wav", new WaveFormat(provider.WaveFormat.SampleRate, 1)))
                {
                    while (true)
                    {
                        for (var readSamples = 0; readSamples < samples.Length;)
                        {
                            var count = provider.Read(samples, readSamples, samples.Length - readSamples);
                            if (count == 0) return;
                            readSamples += count;
                        }

                        //for (var i = 0; i < samples.Length; i++)
                        //    samples[i] = (float)Math.Cos(i / 50.0 * Math.PI);

                        var result = timeStretcher.Stretch(samples, stretchedTime);

                        writer.WriteSamples(result, 0, result.Length);
                        //writer2.WriteSamples(samples, 0, samples.Length);

                        //break;
                    }
                }
            }
        }

        private static void PitchShift()
        {
            var audioFileName = CommonUtils.GetTrainingFile("校歌 2018-01-17 15-10-46.wav");

            using (var reader = new AudioFileReader(audioFileName))
            {
                var provider = reader.ToSampleProvider().ToMono();
                const int nfft = 2048;
                var pitchShifter = new PitchShifterWithStft(nfft);
                var samples = new float[nfft];

                using (var writer = new WaveFileWriter("pitchshift.wav", new WaveFormat(provider.WaveFormat.SampleRate, 1)))
                {
                    while (true)
                    {
                        for (var readSamples = 0; readSamples < samples.Length;)
                        {
                            var count = provider.Read(samples, readSamples, samples.Length - readSamples);
                            if (count == 0) return;
                            readSamples += count;
                        }

                        var result = pitchShifter.InputFrame(samples, 1.2);
                        if (result != null)
                            writer.WriteSamples(result, 0, result.Length);
                    }
                }
            }
        }
    }
}
