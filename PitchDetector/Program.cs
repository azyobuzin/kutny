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
    class Program
    {
        static Dispatcher s_dispatcher;

        [STAThread]
        static void Main(string[] args)
        {
            var app = new System.Windows.Application();
            s_dispatcher = app.Dispatcher;

            new Thread(Run).Start();

            app.Run();
        }

        static void Run()
        {
            Mfcc();
        }

        static void BasicTest()
        {
            const int samples = 1024;
            int rate;
            var data = new float[samples];

            // 2.5sのところから1024サンプル取得してくる
            using (var reader = new WaveFileReader(@"E:\azyobuzin\GoogleDrive\音声録音 2017-12-18 00-17-09.wav"))
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

        static void PitchGraph()
        {
            const int samples = 1024;

            var series = new ScatterSeries();

            using (var reader = new WaveFileReader(@"E:\azyobuzin\GoogleDrive\音声録音 2018-01-17 15-10-46.wav"))
            {
                var provider = reader.ToSampleProvider().ToMono();
                var rate = provider.WaveFormat.SampleRate;

                var history = new LinkedList<float>();

                var data = new float[samples];
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

                for (var i = samples; ; i += samples / 2)
                {
                    // 半分ずらして読み出し
                    Array.Copy(data, samples / 2, data, 0, samples / 2);

                    for (var readSamples = samples / 2; readSamples < data.Length;)
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

        static void Mfcc()
        {
            const int samples = 2048;
            int rate;
            var data = new float[samples];

            // 2.5sのところから1024サンプル取得してくる
            using (var reader = new WaveFileReader(@"E:\azyobuzin\GoogleDrive\音声録音 2017-12-18 00-17-09.wav"))
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

            foreach (var x in MfccAccord.ComputeMfcc12D(rate, data))
                Console.WriteLine(x);
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
