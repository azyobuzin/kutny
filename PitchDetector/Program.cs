using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using NAudio.Wave;
using OxyPlot;
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
            ClassifyVowel();
        }

        private static void BasicTest()
        {
            const int windowSize = 1024;
            int rate;
            var data = new float[windowSize];

            // 2.5sのところから1024サンプル取得してくる
            using (var reader = new WaveFileReader(Path.Combine(GetTrainingDataDirectory(), "あいうえお 2017-12-18 00-17-09.wav")))
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

            Console.WriteLine("{0} Hz", PitchAccord.EstimatePitch(rate, data));
        }

        private static void PitchGraph()
        {
            const int windowSize = 1024;

            var series = new ScatterSeries();

            using (var reader = new WaveFileReader(Path.Combine(GetTrainingDataDirectory(), "校歌 2018-01-17 15-10-46.wav")))
            {
                var provider = reader.ToSampleProvider().ToMono();
                var rate = provider.WaveFormat.SampleRate;

                var history = new LinkedList<float>();

                var data = new float[windowSize];
                {
                    // 1 回目
                    for (var readSamples = 0; readSamples < data.Length;)
                    {
                        var count = provider.Read(data, readSamples, data.Length - readSamples);
                        if (count == 0) return;
                        readSamples += count;
                    }
                    var pitch = PitchAccord.EstimatePitch(rate, data);
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

                    var pitch = PitchAccord.EstimatePitch(rate, data);

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

                            var note = Math.Round(69.0 + 12.0 * Math.Log(med / 440.0, 2.0));
                            series.Points.Add(new ScatterPoint((double)i / rate, note));
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
            using (var reader = new WaveFileReader(Path.Combine(GetTrainingDataDirectory(), "あいうえお 2017-12-18 00-17-09.wav")))
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

            var mfcc = new MfccAccord(rate, windowSize);
            foreach (var x in mfcc.ComputeMfcc12D(data))
                Console.WriteLine(x);
        }

        private static void ClassifyVowel()
        {
            var classifier = new VowelClassifier();

            var dir = GetTrainingDataDirectory();
            classifier.AddTrainingDataAsync(Path.Combine(dir, "あいうえお 2017-12-18 00-17-09.csv")).Wait();

            classifier.Learn();

            {
                // 識別テスト
                const int windowSize = 2048;
                int rate;
                var data = new float[windowSize];

                using (var reader = new WaveFileReader(Path.Combine(dir, "あいうえお 2018-01-20 16-48-52.wav")))
                {
                    var provider = reader.ToSampleProvider()
                        .Skip(TimeSpan.FromSeconds(24))
                        .ToMono();

                    rate = provider.WaveFormat.SampleRate;

                    for (var readSamples = 0; readSamples < data.Length;)
                    {
                        var delta = provider.Read(data, readSamples, data.Length - readSamples);
                        if (delta == 0) throw new EndOfStreamException();
                        readSamples += delta;
                    }
                }

                var mfcc = new MfccAccord(rate, windowSize).ComputeMfcc12D(data);
                Console.WriteLine(classifier.Decide(mfcc));
            }
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

        private static string GetTrainingDataDirectory()
        {
            const string directoryName = "TrainingData";

            var inCurrentDir = Path.Combine(Directory.GetCurrentDirectory(), directoryName);
            if (Directory.Exists(inCurrentDir)) return inCurrentDir;

            // アセンブリの場所からさかのぼってみる
            for (var path = Path.GetDirectoryName(typeof(Program).Assembly.Location);
                path != null;
                path = Path.GetDirectoryName(path))
            {
                var dir = Path.Combine(path, directoryName);
                if (Directory.Exists(dir)) return dir;
            }

            throw new DirectoryNotFoundException(directoryName + " ディレクトリが見つかりませんでした。");
        }
    }
}
