using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Accord.Audio.Windows;
using Accord.Math;
using Accord.Math.Transforms;
using Accord.Statistics;
using Kutny.Common;
using NAudio.Wave;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace PitchDetector
{
    public static class Program
    {
        private static Dispatcher s_dispatcher;

        [STAThread]
        static void Main(string[] args)
        {
            var app = new System.Windows.Application();
            s_dispatcher = app.Dispatcher;

            new Thread(Run).Start();

            app.Run();
        }

        private static void Run()
        {
            LpcSpectrum2();
        }

        private static void BasicTest()
        {
            const int windowSize = 1024;
            int rate;
            var data = new float[windowSize];

            // 2.5sのところから1024サンプル取得してくる
            using (var reader = new AudioFileReader(@"C:\Users\azyob\Documents\Jupyter\chroma\BEYOND THE STARLIGHT.wav"))
            {
                var provider = reader.ToSampleProvider()
                    .Skip(TimeSpan.FromSeconds(5.2))
                    .ToMono();

                rate = provider.WaveFormat.SampleRate;

                for (var readSamples = 0; readSamples < data.Length;)
                {
                    var delta = provider.Read(data, readSamples, data.Length - readSamples);
                    if (delta == 0) throw new EndOfStreamException();
                    readSamples += delta;
                }
            }

            var fft = Array.ConvertAll(data, x => (Complex)x);
            FourierTransform2.FFT(fft, FourierTransform.Direction.Forward);
            var fftSeries = new LineSeries();
            fftSeries.Points.AddRange(fft.Take(fft.Length / 2).Select((x, i) => new DataPoint(i, Math.Log(x.SquaredMagnitude()))));
            ShowPlot(new PlotModel() { Title = "スペクトル", Series = { fftSeries } });

            var nsdf = McLeodPitchMethod.NormalizedSquareDifference(data);
            var series = new LineSeries();
            series.Points.AddRange(nsdf.Select((x, i) => new DataPoint(i, x)));
            ShowPlot(new PlotModel() { Title = "NSDF", Series = { series } });

            Console.WriteLine("{0} Hz", McLeodPitchMethod.EstimateFundamentalFrequency(rate, data));
        }

        private static void PitchGraph()
        {
            const int windowSize = 1024;

            var series = new ScatterSeries();

            using (var reader = new WaveFileReader(Path.Combine(CommonUtils.GetTrainingDataDirectory(), "校歌 2018-01-17 15-10-46.wav")))
            {
                var provider = reader.ToSampleProvider().ToMono();
                var rate = provider.WaveFormat.SampleRate;

                var history = new LinkedList<double>();

                var data = new float[windowSize];
                {
                    // 1 回目
                    for (var readSamples = 0; readSamples < data.Length;)
                    {
                        var count = provider.Read(data, readSamples, data.Length - readSamples);
                        if (count == 0) return;
                        readSamples += count;
                    }
                    var pitch = McLeodPitchMethod.EstimateFundamentalFrequency(rate, data);
                    if (pitch.HasValue) history.AddLast(pitch.Value);
                }

                for (var i = windowSize; ; i += windowSize / 2)
                {
                    // 半分ずらして読み出し
                    Array.Copy(data, windowSize / 2, data, 0, windowSize / 2);

                    for (var readSamples = windowSize / 2; readSamples < data.Length;)
                    {
                        var count = provider.Read(data, readSamples, data.Length - readSamples);
                        if (count == 0) goto Show;
                        readSamples += count;
                    }

                    var pitch = McLeodPitchMethod.EstimateFundamentalFrequency(rate, data);

                    if (pitch.HasValue)
                    {
                        history.AddLast(pitch.Value);
                        if (history.Count >= 16)
                        {
                            if (history.Count > 16)
                                history.RemoveFirst();

                            var h = history.ToArray();
                            Array.Sort(h);
                            var med = h[h.Length / 2];

                            series.Points.Add(new ScatterPoint((double)i / rate, CommonUtils.HzToMidiNote(med)));
                        }
                    }
                }
            }

            Show:
            ShowPlot(new PlotModel()
            {
                Title = "ピッチ",
                Series = { series }
            });
        }

        private static void PitchGraph2()
        {
            const int windowSize = 1024;

            var series = new LineSeries();

            using (var reader = new WaveFileReader(Path.Combine(CommonUtils.GetTrainingDataDirectory(), "校歌 2018-01-17 15-10-46.wav")))
            {
                var provider = reader.ToSampleProvider().ToMono();
                var rate = provider.WaveFormat.SampleRate;

                var history = new LinkedList<double>();

                var data = new float[windowSize];
                {
                    // 1 回目
                    for (var readSamples = 0; readSamples < data.Length;)
                    {
                        var count = provider.Read(data, readSamples, data.Length - readSamples);
                        if (count == 0) return;
                        readSamples += count;
                    }
                    var pitch = McLeodPitchMethod.EstimateFundamentalFrequency(rate, data);
                    if (pitch.HasValue) history.AddLast(pitch.Value);
                }

                for (var i = windowSize; ; i += windowSize / 2)
                {
                    // 半分ずらして読み出し
                    Array.Copy(data, windowSize / 2, data, 0, windowSize / 2);

                    for (var readSamples = windowSize / 2; readSamples < data.Length;)
                    {
                        var count = provider.Read(data, readSamples, data.Length - readSamples);
                        if (count == 0) goto Show;
                        readSamples += count;
                    }

                    var maxPower = 0f;
                    foreach (var x in data)
                    {
                        if (x > maxPower)
                            maxPower = x;
                    }

                    if (maxPower < 0.15) continue;

                    var pitch = McLeodPitchMethod.EstimateFundamentalFrequency(rate, data);

                    if (pitch.HasValue)
                    {
                        history.AddLast(pitch.Value);
                        if (history.Count >= 16)
                        {
                            if (history.Count > 16)
                                history.RemoveFirst();

                            var h = history.ToArray();
                            Array.Sort(h);
                            var med = h[h.Length / 2];

                            series.Points.Add(new DataPoint((double)i / rate, 69.0 + 12.0 * Math.Log(med / 440.0, 2.0)));
                        }
                    }
                }
            }

            Show:
            ShowPlot(new PlotModel()
            {
                Title = "ピッチ",
                Series = { series }
            });
        }

        private static void Mfcc()
        {
            const int windowSize = 2048;
            int rate;
            var data = new float[windowSize];

            // 2.5s のところから 2048 サンプル取得してくる
            using (var reader = new WaveFileReader(Path.Combine(CommonUtils.GetTrainingDataDirectory(), "あいうえお 2017-12-18 00-17-09.wav")))
            {
                var provider = reader.ToSampleProvider()
                    .Skip(TimeSpan.FromSeconds(2.5))
                    .ToMono();

                rate = provider.WaveFormat.SampleRate;

                for (var readSamples = 0; readSamples < data.Length;)
                {
                    var delta = provider.Read(data, readSamples, data.Length - readSamples);
                    if (delta == 0) throw new EndOfStreamException();
                    readSamples += delta;
                }
            }

            var mfcc = new MfccAccord(rate, windowSize, 0, 8000, 24);
            foreach (var x in mfcc.ComputeMfcc12D(data))
                Console.WriteLine(x);
        }

        private static VowelClassifier PrepareVowelClassifier()
        {
            var classifier = new NeuralNetworkVowelClassifier();
            var dir = CommonUtils.GetTrainingDataDirectory();

            Task.WaitAll(
                classifier.AddTrainingDataAsync(Path.Combine(dir, "あいうえお 2017-12-18 00-17-09.csv")),
                classifier.AddTrainingDataAsync(Path.Combine(dir, "あいうえお 2018-01-20 16-48-52.csv"))
            );

            classifier.Learn();

            return classifier;
        }

        private static void ClassifyVowel()
        {
            var classifier = PrepareVowelClassifier();

            // 識別テスト
            const int windowSize = 2048;
            int rate;
            var data = new float[windowSize];

            using (var reader = new WaveFileReader(Path.Combine(CommonUtils.GetTrainingDataDirectory(), "校歌 2018-01-17 15-10-46.wav")))
            {
                var provider = reader.ToSampleProvider()
                    .Skip(TimeSpan.FromSeconds(11))
                    .ToMono();

                rate = provider.WaveFormat.SampleRate;

                for (var readSamples = 0; readSamples < data.Length;)
                {
                    var delta = provider.Read(data, readSamples, data.Length - readSamples);
                    if (delta == 0) throw new EndOfStreamException();
                    readSamples += delta;
                }
            }

            Console.WriteLine(classifier.Decide(data, rate));
        }

        private static void PitchAndLyric()
        {
            float[] samples;
            int rate;

            using (var wavReader = new WaveFileReader(Path.Combine(CommonUtils.GetTrainingDataDirectory(), "校歌 2018-01-17 15-10-46.wav")))
            {
                var provider = wavReader.ToSampleProvider().ToMono();
                rate = provider.WaveFormat.SampleRate;

                samples = new float[wavReader.SampleCount];
                for (var readSamples = 0; readSamples < samples.Length;)
                {
                    var count = provider.Read(samples, readSamples, samples.Length - readSamples);
                    if (count == 0) break;
                    readSamples += count;
                }
            }

            const int analysisUnit = 4096; // 4096 サンプルを 1 まとまりとする
            const int vowelWindowSize = 2048;
            const int pitchWindowSize = 1024;

            var classifier = PrepareVowelClassifier();
            var series = new IntervalBarSeries();
            var secsPerAnalysisUnit = (double)analysisUnit / rate;
            var analysisUnitCount = samples.Length / analysisUnit;

            for (var i = 0; i < analysisUnitCount; i++)
            {
                var startIndex = analysisUnit * i;
                var endIndex = startIndex + analysisUnit;

                var maxPower = 0f;
                for (var j = startIndex + 1; j < endIndex - 1; j++)
                {
                    if (samples[j] > maxPower)
                        maxPower = samples[j];
                }

                // 音量小さすぎ
                if (maxPower < 0.15) continue;

                // 512 ずつずらしながら母音認識
                var vowelCandidates = new int[(int)VowelType.Other + 1];
                for (var offset = startIndex; offset <= endIndex - vowelWindowSize; offset += 512)
                {
                    vowelCandidates[(int)classifier.Decide(new ReadOnlySpan<float>(samples, offset, vowelWindowSize), rate)]++;
                }

                var vowelCandidate = default(VowelType?);
                var maxNumOfVotes = 0;
                for (var j = 0; j < vowelCandidates.Length; j++)
                {
                    if (vowelCandidates[j] > maxNumOfVotes)
                    {
                        maxNumOfVotes = vowelCandidates[j];
                        vowelCandidate = (VowelType)j;
                    }
                    else if (vowelCandidates[j] == maxNumOfVotes)
                    {
                        vowelCandidate = null;
                    }
                }

                // 母音が定まらなかったので、終了
                if (!vowelCandidate.HasValue || vowelCandidate.Value == VowelType.Other)
                    continue;

                // 512 ずつずらしながらピッチ検出
                const int pitchOffsetDelta = 512;
                var basicFreqs = new List<double>(analysisUnit / pitchOffsetDelta);
                for (var offset = startIndex; offset <= endIndex - pitchWindowSize; offset += pitchOffsetDelta)
                {
                    var f = McLeodPitchMethod.EstimateFundamentalFrequency(
                        rate,
                        new ReadOnlySpan<float>(samples, offset, pitchWindowSize)
                    );

                    if (f.HasValue) basicFreqs.Add(f.Value);
                }

                // ピッチ検出に失敗したので終了
                if (basicFreqs.Count == 0) continue;

                basicFreqs.Sort();
                var basicFreq = basicFreqs[basicFreqs.Count / 2]; // 中央値
                var noteNum = CommonUtils.HzToMidiNote(basicFreq);

                var plotItem = new IntervalBarItem()
                {
                    Start = secsPerAnalysisUnit * i,
                    End = secsPerAnalysisUnit * (i + 1),
                    Title = vowelCandidate.ToString(),
                    CategoryIndex = noteNum
                };

                var items = series.Items;
                if (items.Count > 0)
                {
                    var lastItem = items[items.Count - 1];
                    if (lastItem.End == plotItem.Start
                        && lastItem.CategoryIndex == plotItem.CategoryIndex
                        && lastItem.Title == plotItem.Title)
                    {
                        // マージできる
                        lastItem.End = plotItem.End;
                        continue;
                    }
                }

                items.Add(plotItem);
            }

            var categoryAxis = new CategoryAxis() { Position = AxisPosition.Left };
            var noteNames = new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            for (var i = 0; i <= 127; i++)
                categoryAxis.Labels.Add(noteNames[i % 12] + (i / 12).ToString(CultureInfo.InvariantCulture));

            ShowPlot(new PlotModel()
            {
                Title = "ピッチと母音",
                Axes = { categoryAxis },
                Series = { series }
            });
        }

        private static void MelFilteredSpectrum()
        {
            const int windowSize = 2048;
            const int sampleRate = 44100;

            var mfccComputer = new MfccAccord(sampleRate, windowSize, 0, 8000, 8);
            var hzAxis = mfccComputer.HzAxisOfMelSpectrum();

            float[] ReadSamples(string fileName, double secs)
            {
                float[] xs;
                using (var wavReader = new WaveFileReader(Path.Combine(CommonUtils.GetTrainingDataDirectory(), fileName)))
                {
                    var provider = wavReader.ToSampleProvider().Skip(TimeSpan.FromSeconds(secs)).ToMono();

                    if (provider.WaveFormat.SampleRate != sampleRate)
                        throw new Exception();

                    xs = new float[windowSize];
                    for (var readSamples = 0; readSamples < xs.Length;)
                    {
                        var count = provider.Read(xs, readSamples, xs.Length - readSamples);
                        if (count == 0) break;
                        readSamples += count;
                    }
                }
                return xs;
            }

            void ShowSpectrum(string title, params (string, double)[] inputs)
            {
                var model = new PlotModel() { Title = title };

                var i = 0;
                foreach (var (fileName, secs) in inputs)
                {
                    var samples = ReadSamples(fileName, secs);
                    var spec = mfccComputer.MelSpectrum(samples);
                    var mean = spec.Mean();
                    spec.Subtract(mean, spec);
                    var series = new LineSeries() { Title = (++i).ToString() };
                    series.Points.AddRange(hzAxis.Zip(spec, (x, y) => new DataPoint(x, y)));
                    model.Series.Add(series);
                }

                ShowPlot(model);
            }

            ShowSpectrum(
                "あ",
                ("あいうえお 2017-12-18 00-17-09.wav", 2.5),
                ("あいうえお 2018-01-20 16-48-52.wav", 3.5),
                ("あいうえお 2018-01-20 16-48-52.wav", 9),
                ("あいうえお 2018-01-20 16-48-52.wav", 13),
                ("校歌 2018-01-17 15-10-46.wav", 9)
            );

            ShowSpectrum(
                "い",
                ("あいうえお 2017-12-18 00-17-09.wav", 5.7),
                ("あいうえお 2018-01-20 16-48-52.wav", 20),
                ("あいうえお 2018-01-20 16-48-52.wav", 24),
                ("あいうえお 2018-01-20 16-48-52.wav", 29)
            );

            ShowSpectrum(
                "う",
                ("あいうえお 2017-12-18 00-17-09.wav", 9.8),
                ("あいうえお 2018-01-20 16-48-52.wav", 34),
                ("あいうえお 2018-01-20 16-48-52.wav", 37),
                ("あいうえお 2018-01-20 16-48-52.wav", 41)
            );

            ShowSpectrum(
                "え",
                ("あいうえお 2017-12-18 00-17-09.wav", 13),
                ("あいうえお 2018-01-20 16-48-52.wav", 46.5),
                ("あいうえお 2018-01-20 16-48-52.wav", 49.5),
                ("あいうえお 2018-01-20 16-48-52.wav", 53)
            );

            ShowSpectrum(
                "お",
                ("あいうえお 2017-12-18 00-17-09.wav", 17),
                ("あいうえお 2018-01-20 16-48-52.wav", 57),
                ("あいうえお 2018-01-20 16-48-52.wav", 60),
                ("あいうえお 2018-01-20 16-48-52.wav", 63)
            );

            ShowSpectrum(
                "ん",
                ("あいうえお 2018-01-20 16-48-52.wav", 67),
                ("あいうえお 2018-01-20 16-48-52.wav", 70),
                ("あいうえお 2018-01-20 16-48-52.wav", 73.5),
                ("あいうえお 2018-01-20 16-48-52.wav", 78)
            );
        }

        private static void Nsdf()
        {
            const int windowSize = 1024;
            int rate;
            var data = new float[windowSize];

            // 2.5sのところから1024サンプル取得してくる
            using (var reader = new WaveFileReader(Path.Combine(CommonUtils.GetTrainingDataDirectory(), "あいうえお 2017-12-18 00-17-09.wav")))
            {
                var provider = reader.ToSampleProvider()
                    .Skip(TimeSpan.FromSeconds(2.5))
                    .ToMono();

                rate = provider.WaveFormat.SampleRate;

                for (var readSamples = 0; readSamples < data.Length;)
                {
                    var delta = provider.Read(data, readSamples, data.Length - readSamples);
                    if (delta == 0) throw new EndOfStreamException();
                    readSamples += delta;
                }
            }

            using (var writer = new StreamWriter("samples.txt", false, new UTF8Encoding(false)))
            {
                for (var i = 0; i < data.Length; i++)
                {
                    writer.WriteLine("{0} {1}", i, data[i]);
                }
            }

            float Acf(int t)
            {
                float result = 0f;
                for (var j = 0; j < windowSize - t; j++)
                    result += data[j] * data[j + t];
                return result;
            }

            using (var writer = new StreamWriter("acf.txt", false, new UTF8Encoding(false)))
            {
                var acfSeries = new LineSeries();
                for (var i = 0; i < windowSize; i++)
                {
                    var v = Acf(i);
                    acfSeries.Points.Add(new DataPoint(i, v));
                    writer.WriteLine("{0} {1}", i, v);
                }
                ShowPlot(new PlotModel()
                {
                    Title = "Autocorrelation Function",
                    Series = { acfSeries }
                });
            }

            float Sdf(int t)
            {
                float result = 0f;
                for (var j = 0; j < windowSize - t; j++)
                {
                    var d = data[j] - data[j + t];
                    result += d * d;
                }
                return result;
            }

            using (var writer = new StreamWriter("sdf.txt", false, new UTF8Encoding(false)))
            {
                var sdfSeries = new LineSeries();
                for (var i = 0; i < windowSize; i++)
                {
                    var v = Sdf(i);
                    sdfSeries.Points.Add(new DataPoint(i, v));
                    writer.WriteLine("{0} {1}", i, v);
                }
                ShowPlot(new PlotModel()
                {
                    Title = "Square Difference Function",
                    Series = { sdfSeries }
                });
            }

            float M(int t)
            {
                var result = 0f;
                for (var j = 0; j < windowSize - t; j++)
                    result += data[j] * data[j] + data[j + t] * data[j + t];
                return result;
            }

            using (var writer = new StreamWriter("nsdf.txt", false, new UTF8Encoding(false)))
            {
                var nsdfSeries = new LineSeries();
                for (var i = 0; i < windowSize; i++)
                {
                    var v = 2 * Acf(i) / M(i);
                    nsdfSeries.Points.Add(new DataPoint(i, v));
                    writer.WriteLine("{0} {1}", i, v);
                }
                ShowPlot(new PlotModel()
                {
                    Title = "Normalized Square Difference Function",
                    Series = { nsdfSeries }
                });
            }

            BasicTest();
        }

        private static void LpcSpectrum()
        {
            const int windowSize = 2048;
            int rate;
            var rawData = new float[windowSize];

            using (var reader = new WaveFileReader(Path.Combine(CommonUtils.GetTrainingDataDirectory(), "あいうえお 2017-12-18 00-17-09.wav")))
            {
                var provider = reader.ToSampleProvider()
                    .Skip(TimeSpan.FromSeconds(2.5))
                    .ToMono();

                rate = provider.WaveFormat.SampleRate;

                for (var readSamples = 0; readSamples < rawData.Length;)
                {
                    var delta = provider.Read(rawData, readSamples, rawData.Length - readSamples);
                    if (delta == 0) throw new EndOfStreamException();
                    readSamples += delta;
                }
            }

            var data = new float[windowSize];
            //var data = rawData;

            // プリエンファシス → 窓関数
            var windowFunc = RaisedCosineWindow.Hamming(windowSize);
            data[0] = rawData[0] * windowFunc[0];
            for (var i = 1; i < data.Length; i++)
            {
                var emphasized = rawData[i] - 0.97f * rawData[i - 1];
                data[i] = emphasized * windowFunc[i];
            }

            var fft = Array.ConvertAll(data, x => (Complex)x);
            FourierTransform2.FFT(fft, FourierTransform.Direction.Forward);
            var fftSeries = new LineSeries();
            for (var i = 0; i < fft.Length; i++)
                fftSeries.Points.Add(new DataPoint(((double)rate / windowSize) * i, 20.0 * Math.Log10(fft[i].Magnitude)));

            var lpc = LinearPrediction.ForwardLinearPrediction(data, 24);
            var lpcSpec = LinearPrediction.FirFrequencyResponse(lpc.Coefficients, windowSize);
            var lpcSeries = new LineSeries();
            for (var i = 0; i < lpcSpec.Length; i++)
                lpcSeries.Points.Add(new DataPoint(((double)rate / windowSize) * i, 20.0 * Math.Log10(lpcSpec[i].Magnitude)));

            ShowPlot(new PlotModel()
            {
                Title = "LPC",
                Series = { fftSeries, lpcSeries }
            });
        }

        private static void LpcSpectrum2()
        {
            LineSeries CreateLpcSpectrum(string fileName, double startSecs)
            {
                const int windowSize = 2048;
                int rate;
                var rawData = new float[windowSize];

                using (var reader = new WaveFileReader(Path.Combine(CommonUtils.GetTrainingDataDirectory(), fileName)))
                {
                    var provider = reader.ToSampleProvider()
                        .Skip(TimeSpan.FromSeconds(startSecs))
                        .ToMono();

                    rate = provider.WaveFormat.SampleRate;

                    for (var readSamples = 0; readSamples < rawData.Length;)
                    {
                        var delta = provider.Read(rawData, readSamples, rawData.Length - readSamples);
                        if (delta == 0) throw new EndOfStreamException();
                        readSamples += delta;
                    }
                }

                var data = new float[windowSize];

                // プリエンファシス → 窓関数
                var windowFunc = RaisedCosineWindow.Hamming(windowSize);
                data[0] = rawData[0] * windowFunc[0];
                for (var i = 1; i < data.Length; i++)
                {
                    var emphasized = rawData[i] - 0.97f * rawData[i - 1];
                    data[i] = emphasized * windowFunc[i];
                }

                var lpc = LinearPrediction.ForwardLinearPrediction(data, 24);
                var lpcSpec = LinearPrediction.FirFrequencyResponse(lpc.Coefficients, windowSize);
                var lpcSeries = new LineSeries();
                for (var i = 0; i < lpcSpec.Length; i++)
                    lpcSeries.Points.Add(new DataPoint(((double)rate / windowSize) * i, 20.0 * Math.Log10(lpcSpec[i].Magnitude)));

                return lpcSeries;
            }

            void ShowSpectrum(string title, params (string, double)[] inputs)
            {
                var model = new PlotModel()
                {
                    Title = title,
                    Axes =
                    {
                        new LinearAxis() { Position = AxisPosition.Bottom, Minimum = 0, Maximum = 10000 }
                    }
                };

                var i = 0;
                foreach (var (fileName, secs) in inputs)
                {
                    var series = CreateLpcSpectrum(fileName, secs);
                    series.Title = (++i).ToString();
                    model.Series.Add(series);
                }

                ShowPlot(model);
            }

            ShowSpectrum(
                "あ",
                ("あいうえお 2017-12-18 00-17-09.wav", 2.5),
                ("あいうえお 2018-01-20 16-48-52.wav", 3.5),
                ("あいうえお 2018-01-20 16-48-52.wav", 9),
                ("あいうえお 2018-01-20 16-48-52.wav", 13),
                ("校歌 2018-01-17 15-10-46.wav", 9)
            );

            ShowSpectrum(
                "い",
                ("あいうえお 2017-12-18 00-17-09.wav", 5.7),
                ("あいうえお 2018-01-20 16-48-52.wav", 20),
                ("あいうえお 2018-01-20 16-48-52.wav", 24),
                ("あいうえお 2018-01-20 16-48-52.wav", 29)
            );

            ShowSpectrum(
                "う",
                ("あいうえお 2017-12-18 00-17-09.wav", 9.8),
                ("あいうえお 2018-01-20 16-48-52.wav", 34),
                ("あいうえお 2018-01-20 16-48-52.wav", 37),
                ("あいうえお 2018-01-20 16-48-52.wav", 41)
            );

            ShowSpectrum(
                "え",
                ("あいうえお 2017-12-18 00-17-09.wav", 13),
                ("あいうえお 2018-01-20 16-48-52.wav", 46.5),
                ("あいうえお 2018-01-20 16-48-52.wav", 49.5),
                ("あいうえお 2018-01-20 16-48-52.wav", 53)
            );

            ShowSpectrum(
                "お",
                ("あいうえお 2017-12-18 00-17-09.wav", 17),
                ("あいうえお 2018-01-20 16-48-52.wav", 57),
                ("あいうえお 2018-01-20 16-48-52.wav", 60),
                ("あいうえお 2018-01-20 16-48-52.wav", 63)
            );

            ShowSpectrum(
                "ん",
                ("あいうえお 2018-01-20 16-48-52.wav", 67),
                ("あいうえお 2018-01-20 16-48-52.wav", 70),
                ("あいうえお 2018-01-20 16-48-52.wav", 73.5),
                ("あいうえお 2018-01-20 16-48-52.wav", 78)
            );
        }

        public static void ShowPlot(PlotModel plot)
        {
            if (s_dispatcher == null) return; // UtagoeGui から呼ばれたら厳しい

            s_dispatcher.InvokeAsync(() =>
            {
                var window = new System.Windows.Window()
                {
                    Title = plot.Title,
                    Width = 600,
                    Height = 500,
                    Content = new OxyPlot.Wpf.PlotView() { Model = plot }
                };

                window.Show();
            });
        }
    }
}
