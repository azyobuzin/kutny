using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Accord.IO;
using Accord.MachineLearning.VectorMachines.Learning;
using Accord.Math;
using Accord.Statistics;
using Accord.Statistics.Kernels;
using NAudio.Wave;

namespace PitchDetector
{
    public class MelSpectrumVowelClassifier : VowelClassifier
    {
        private readonly MulticlassSupportVectorLearning<Linear> _teacher = new MulticlassSupportVectorLearning<Linear>();

        public override Task AddTrainingDataAsync(string csvFileName)
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
                var mfcc = new MfccAccord(rate, windowSize, 0, 6000, 8);

                while (csvReader.ReadNextRecord())
                {
                    var time = double.Parse(csvReader["Time"], CultureInfo.InvariantCulture);
                    var vowelType = ParseVowelType(csvReader["Class"]);

                    // どうしよう
                    if (vowelType == VowelType.Other) continue;

                    // 並列でばんばか投げていくぞ
                    tasks.Add(Task.Run(() =>
                    {
                        var v = mfcc.MelSpectrum(new ReadOnlySpan<float>(samples, (int)(time * rate), windowSize));
                        v.Subtract(v.Mean(), v);

                        lock (this.TrainingData)
                            this.TrainingData.Add((v, vowelType));
                    }));
                }
            }

            return Task.WhenAll(tasks);
        }

        public override void Learn()
        {
            var inputs = new double[this.TrainingData.Count][];
            var classes = new int[this.TrainingData.Count];

            for (var i = 0; i < this.TrainingData.Count; i++)
            {
                var (x, y) = this.TrainingData[i];
                inputs[i] = x;
                classes[i] = (int)y;
            }

            this._teacher.Learn(inputs, classes);
        }

        public override VowelType Decide(ReadOnlySpan<float> samples, int sampleRate)
        {
            var input = new MfccAccord(sampleRate, samples.Length, 0, 6000, 8).MelSpectrum(samples);
            return (VowelType)this._teacher.Model.Decide(input);
        }
    }
}
