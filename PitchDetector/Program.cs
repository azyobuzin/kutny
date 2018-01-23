using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Accord.Math;
using Accord.Statistics;
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
            MelFilteredSpectrum();
        }

        private static void BasicTest()
        {
            const int windowSize = 1024;
            int rate;
            var data = new float[windowSize];

            // 2.5sのところから1024サンプル取得してくる
            using (var reader = new WaveFileReader(Path.Combine(Utils.GetTrainingDataDirectory(), "あいうえお 2017-12-18 00-17-09.wav")))
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

            // 440Hz
            //rate = 44100;
            //for (var i = 0; i < samples; i++)
            //{
            //    data[i] = (float)Math.Sin(2 * Math.PI * 440 * i / rate);
            //}

            Console.WriteLine("{0} Hz", PitchAccord.EstimateBasicFrequency(rate, data));
        }

        private static void PitchGraph()
        {
            const int windowSize = 1024;

            var series = new ScatterSeries();

            using (var reader = new WaveFileReader(Path.Combine(Utils.GetTrainingDataDirectory(), "校歌 2018-01-17 15-10-46.wav")))
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
                    var pitch = PitchAccord.EstimateBasicFrequency(rate, data);
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

                    var pitch = PitchAccord.EstimateBasicFrequency(rate, data);

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

                            series.Points.Add(new ScatterPoint((double)i / rate, Utils.HzToMidiNote(med)));
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
            using (var reader = new WaveFileReader(Path.Combine(Utils.GetTrainingDataDirectory(), "あいうえお 2017-12-18 00-17-09.wav")))
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
            var dir = Utils.GetTrainingDataDirectory();

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

            using (var reader = new WaveFileReader(Path.Combine(Utils.GetTrainingDataDirectory(), "校歌 2018-01-17 15-10-46.wav")))
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

            using (var wavReader = new WaveFileReader(Path.Combine(Utils.GetTrainingDataDirectory(), "校歌 2018-01-17 15-10-46.wav")))
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
                    var f = PitchAccord.EstimateBasicFrequency(
                        rate,
                        new ReadOnlySpan<float>(samples, offset, pitchWindowSize)
                    );

                    if (f.HasValue) basicFreqs.Add(f.Value);
                }

                // ピッチ検出に失敗したので終了
                if (basicFreqs.Count == 0) continue;

                basicFreqs.Sort();
                var basicFreq = basicFreqs[basicFreqs.Count / 2]; // 中央値
                var noteNum = Utils.HzToMidiNote(basicFreq);

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
                using (var wavReader = new WaveFileReader(Path.Combine(Utils.GetTrainingDataDirectory(), fileName)))
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

        public static void ShowPlot(PlotModel plot)
        {
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
