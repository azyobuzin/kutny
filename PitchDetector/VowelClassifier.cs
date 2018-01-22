using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Accord.IO;
using NAudio.Wave;

namespace PitchDetector
{
    public abstract class VowelClassifier
    {
        protected List<(double[], VowelType)> TrainingData { get; } = new List<(double[], VowelType)>();

        public virtual Task AddTrainingDataAsync(string csvFileName)
        {
            float[] samples;
            int rate;

            // 同じファイル名の .wav ファイルをロード
            using (var wavReader = new WaveFileReader(Path.ChangeExtension(csvFileName, ".wav")))
            {
                var provider = wavReader.ToSampleProvider().ToMono();
                rate = provider.WaveFormat.SampleRate;

                // 全サンプル読み込んじゃえ
                samples = new float[wavReader.SampleCount];
                for (var readSamples = 0; readSamples < samples.Length;)
                {
                    var count = provider.Read(samples, readSamples, samples.Length - readSamples);
                    if (count == 0) break;
                    readSamples += count;
                }
            }

            var tasks = new List<Task>();
            using (var csvReader = new CsvReader(csvFileName, true))
            {
                const int windowSize = 2048;
                var mfcc = new MfccAccord(rate, windowSize);

                while (csvReader.ReadNextRecord())
                {
                    var time = double.Parse(csvReader["Time"], CultureInfo.InvariantCulture);
                    var vowelType = ParseVowelType(csvReader["Class"]);

                    // どうしよう
                    //if (vowelType == VowelType.Other) continue;

                    // 並列でばんばか投げていくぞ
                    tasks.Add(Task.Run(() =>
                    {
                        var v = mfcc.ComputeMfcc12D(new ReadOnlySpan<float>(samples, (int)(time * rate), windowSize));

                        lock (this.TrainingData)
                            this.TrainingData.Add((v, vowelType));
                    }));
                }
            }

            return Task.WhenAll(tasks);
        }

        public abstract void Learn();

        public abstract VowelType Decide(double[] input);

        public static VowelType ParseVowelType(string klass)
        {
            switch (klass)
            {
                case "あ": return VowelType.A;
                case "い": return VowelType.I;
                case "う": return VowelType.U;
                case "え": return VowelType.E;
                case "お": return VowelType.O;
                case "ん": return VowelType.N;
                case "他": return VowelType.Other;
                default: throw new ArgumentException();
            }
        }

        public static string VowelTypeToString(VowelType x)
        {
            switch (x)
            {
                case VowelType.A: return "あ";
                case VowelType.I: return "い";
                case VowelType.U: return "う";
                case VowelType.E: return "え";
                case VowelType.O: return "お";
                case VowelType.N: return "ん";
                case VowelType.Other: return "他";
                default: throw new ArgumentException();
            }
        }
    }

    public enum VowelType
    {
        A,
        I,
        U,
        E,
        O,
        N,
        Other
    }
}
