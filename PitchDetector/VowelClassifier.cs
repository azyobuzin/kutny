using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Accord.IO;
using Accord.MachineLearning.VectorMachines.Learning;
using Accord.Statistics.Kernels;
using NAudio.Wave;

namespace PitchDetector
{
    public class VowelClassifier
    {
        private readonly List<(double[], VowelType)> _trainingData = new List<(double[], VowelType)>();
        private readonly MulticlassSupportVectorLearning<Gaussian> _teacher = new MulticlassSupportVectorLearning<Gaussian>();

        public Task AddTrainingDataAsync(string csvFileName)
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

                    // 並列でばんばか投げていくぞ
                    tasks.Add(Task.Run(() =>
                    {
                        var v = mfcc.ComputeMfcc12D(new ReadOnlySpan<float>(samples, (int)(time * rate), windowSize));

                        lock (this._trainingData)
                            this._trainingData.Add((v, vowelType));
                    }));
                }
            }

            return Task.WhenAll(tasks);
        }

        private static VowelType ParseVowelType(string klass)
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

        public void Learn()
        {
            var inputs = new double[this._trainingData.Count][];
            var classes = new int[this._trainingData.Count];

            for (var i = 0; i < this._trainingData.Count; i++)
            {
                var (x, y) = this._trainingData[i];
                inputs[i] = x;
                classes[i] = (int)y;
            }

            this._teacher.Learn(inputs, classes);
        }

        public VowelType Decide(double[] input)
        {
            return (VowelType)this._teacher.Model.Decide(input);
        }
    }

    public enum VowelType
    {
        Other,
        A,
        I,
        U,
        E,
        O,
        N,
    }
}
