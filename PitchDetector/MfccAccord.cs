using System;
using Accord.Audio.Windows;
using Accord.Math;
using Accord.Math.Transforms;
using OxyPlot;
using OxyPlot.Series;

namespace PitchDetector
{
    public class MfccAccord
    {
        public int SampleRate { get; }
        public int SampleCount { get; }
        public double LowerHz { get; }
        public double UpperHz { get; }
        public int FilterCount { get; }
        private RaisedCosineWindow _windowFunc;
        private readonly double[,] _melFilterbank;

        public MfccAccord(int sampleRate, int sampleCount, double lowerHz, double upperHz, int filterCount)
        {
            this.SampleRate = sampleRate;
            this.SampleCount = sampleCount;
            this.LowerHz = lowerHz;
            this.UpperHz = upperHz;
            this.FilterCount = filterCount;

            // 事前に計算しておけるもの
            this._windowFunc = RaisedCosineWindow.Hamming(sampleCount);
            this._melFilterbank = CreateMelFilterbank(sampleRate, sampleCount, lowerHz, upperHz, filterCount);
        }

        public double[] ComputeMfcc12D(ReadOnlySpan<float> samples)
        {
            var filtered = MelSpectrum(samples);
            CosineTransform.DCT(filtered);

            const int resultDimension = 12;
            var result = new double[resultDimension];
            // Min は応急処置（頭が悪い）
            Array.Copy(filtered, 1, result, 0, Math.Min(resultDimension, filtered.Length - 1));

            return result;
        }

        public double[] MelSpectrum(ReadOnlySpan<float> samples)
        {
            if (samples.Length != this.SampleCount)
                throw new ArgumentException("samples の長さが違います。");

            var real = new double[samples.Length];
            var imag = new double[samples.Length];

            // プリエンファシス → 窓関数
            real[0] = samples[0] * this._windowFunc[0];
            for (var i = 1; i < real.Length; i++)
            {
                var emphasized = samples[i] - 0.97 * samples[i - 1];
                real[i] = emphasized * this._windowFunc[i];
            }

            FourierTransform2.FFT(real, imag, FourierTransform.Direction.Forward);

            // 振幅スペクトル
            for (var i = 0; i < real.Length; i++)
                real[i] = real[i] * real[i] + imag[i] * imag[i];

            // メルフィルターバンクをかける
            var filtered = real.Dot(this._melFilterbank);
            for (var i = 0; i < filtered.Length; i++)
                filtered[i] = Math.Log10(filtered[i]);

            return filtered;
        }

        public double[] HzAxisOfMelSpectrum()
        {
            var result = new double[this._melFilterbank.GetLength(1)];
            for (var i = 0; i < result.Length; i++)
                result[i] = MelToHz(HzToMel(this.LowerHz) + HzToMel(this.UpperHz - this.LowerHz) / (this.FilterCount + 1) * (i + 1));
            return result;
        }

        private static double HzToMel(double f)
        {
            return 1125.0 * Math.Log(1.0 + f / 700.0);
        }

        private static double MelToHz(double m)
        {
            return 700.0 * (Math.Exp(m / 1125.0) - 1.0);
        }

        private static double[,] CreateMelFilterbank(int sampleRate, int nfft, double lowerHz, double upperHz, int filterCount)
        {
            // http://practicalcryptography.com/miscellaneous/machine-learning/guide-mel-frequency-cepstral-coefficients-mfccs/#computing-the-mel-filterbank

            var lowerMel = HzToMel(lowerHz);
            var upperMel = HzToMel(upperHz);

            var indexes = new int[filterCount + 2];
            var melDelta = (upperMel - lowerMel) / (filterCount + 1);
            var mel = lowerMel;
            for (var i = 0; i < indexes.Length; i++)
            {
                var freq = MelToHz(mel);
                indexes[i] = (int)((nfft + 1) * freq / sampleRate);
                mel += melDelta;
            }

            var filterbank = new double[nfft, filterCount];

            for (var i = 1; i <= filterCount; i++)
            {
                var filterIndex = i - 1;
                var lowerIndex = indexes[i - 1];
                var centerIndex = indexes[i];
                var upperIndex = indexes[i + 1];

                var denom = (double)(centerIndex - lowerIndex);
                for (var k = lowerIndex; k <= centerIndex; k++)
                    filterbank[k, filterIndex] = (k - lowerIndex) / denom;

                denom = upperIndex - centerIndex;
                for (var k = centerIndex + 1; k <= upperIndex; k++)
                    filterbank[k, filterIndex] = (upperIndex - k) / denom;
            }

            return filterbank;
        }

        private static void ShowFilterbank(string title, double[,] filterbank, int sampleRate)
        {
            var model = new PlotModel() { Title = title };

            var nfft = filterbank.GetLength(0);
            var filterCount = filterbank.GetLength(1);
            for (var i = 0; i < filterCount; i++)
            {
                var series = new LineSeries();
                for (var j = 0; j < nfft; j++)
                {
                    series.Points.Add(new DataPoint(
                        (double)sampleRate / nfft * j,
                        filterbank[j, i]
                    ));
                }
                model.Series.Add(series);
            }

            Program.ShowPlot(model);
        }
    }
}
