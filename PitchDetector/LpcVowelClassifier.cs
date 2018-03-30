using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Accord.IO;
using Accord.MachineLearning.VectorMachines.Learning;
using Accord.Math;
using Accord.Statistics.Kernels;
using NAudio.Wave;

namespace PitchDetector
{
    public class LpcVowelClassifier : VowelClassifier
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

                while (csvReader.ReadNextRecord())
                {
                    var time = double.Parse(csvReader["Time"], CultureInfo.InvariantCulture);
                    var vowelType = ParseVowelType(csvReader["Class"]);

                    // どうしよう
                    if (vowelType == VowelType.Other) continue;

                    // 並列でばんばか投げていくぞ
                    tasks.Add(Task.Run(() =>
                    {
                        var v = GetFormants(new ReadOnlySpan<float>(samples, (int)(time * rate), windowSize), rate);

                        lock (this.TrainingData)
                            this.TrainingData.Add((v, vowelType));
                    }));
                }
            }

            return Task.WhenAll(tasks);
        }

        private static double[] GetFormants(ReadOnlySpan<float> samples, int sampleRate)
        {
            // もっとうまい方法があるんだろうけど、思考停止して周波数特性から山を探す
            var lpcResult = LinearPrediction.ForwardLinearPrediction(samples, 24);

            const int lpcSpectrumLength = 1024;
            var lpcSpectrum = Array.ConvertAll(
                LinearPrediction.FirFrequencyResponse(lpcResult.Coefficients, lpcSpectrumLength),
                x => x.SquaredMagnitude()
            );

            // ピーク検出
            var peaksFreqs = new List<(double, double)>();
            const double minFreq = 500;
            for (var i = (int)(minFreq * lpcSpectrumLength / sampleRate); i < lpcSpectrumLength / 2; i++)
            {
                var val = lpcSpectrum[i];
                if (val >= lpcSpectrum[i - 1] && val > lpcSpectrum[i + 1])
                {
                    var freq = sampleRate * ((double)i / lpcSpectrumLength);
                    peaksFreqs.Add((freq, val));
                }
            }

            // 近すぎるピークをまとめる
            for (var i = 1; i < peaksFreqs.Count;)
            {
                var (prevFreq, prevMag) = peaksFreqs[i - 1];
                var (currentFreq, currentMag) = peaksFreqs[i];

                if (currentFreq - prevFreq < 300)
                {
                    // 300Hz より狭いなら大きいほうにまとめる
                    if (prevMag >= currentMag)
                        peaksFreqs.RemoveAt(i);
                    else
                        peaksFreqs.RemoveAt(i - 1);
                }
                else
                {
                    i++;
                }
            }

            // 最初の 2 フォルマントを返す
            var formants = new double[2];
            for (var i = 0; i < formants.Length && i < peaksFreqs.Count; i++)
                formants[i] = peaksFreqs[i].Item1;

            return formants;
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
            var input = GetFormants(samples, sampleRate);
            return (VowelType)this._teacher.Model.Decide(input);
        }
    }
}
