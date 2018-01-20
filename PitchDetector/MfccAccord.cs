using System;
using System.Linq;
using System.Numerics;
using Accord.Math;
using Accord.Math.Transforms;
using OxyPlot;
using OxyPlot.Series;

namespace PitchDetector
{
    public static class MfccAccord
    {
        public static double[] ComputeMfcc12D(int sampleRate, ReadOnlySpan<float> samples)
        {
            // プリエンファシス → 窓関数
            var fft = new Complex[samples.Length];
            fft[0] = samples[0] * (0.54 - 0.46);
            var cosDenom = (double)(samples.Length - 1);
            for (var i = 1; i < fft.Length; i++)
            {
                var emphasized = samples[i] - 0.97 * samples[i - 1];
                var hamming = 0.54 - 0.46 * Math.Cos(2.0 * Math.PI * i / cosDenom);
                fft[i] = emphasized * hamming;
            }

            //var windowedSeries = new LineSeries();
            //windowedSeries.Points.AddRange(fft.Select((x, i) => new DataPoint((double)i / sampleRate, x.Real)));
            //Program.ShowPlot(new PlotModel() { Title = "入力", Series = { windowedSeries } });

            FourierTransform2.FFT(fft, FourierTransform.Direction.Forward);

            // 振幅スペクトル
            var spec = Array.ConvertAll(fft, x => x.Real * x.Real + x.Imaginary * x.Imaginary);

            //var specSeries = new LineSeries();
            //specSeries.Points.AddRange(spec.Select((x, i) => new DataPoint((double)sampleRate / samples.Length * i, x)));
            //Program.ShowPlot(new PlotModel() { Title = "振幅スペクトル", Series = { specSeries } });

            //var powerSpecSeries = new LineSeries();
            //powerSpecSeries.Points.AddRange(spec.Select((x, i) => new DataPoint((double)sampleRate / samples.Length * i, Math.Log10(x))));
            //Program.ShowPlot(new PlotModel() { Title = "パワースペクトル", Series = { powerSpecSeries } });

            // メルフィルターバンクをかける
            const double lowerHz = 0, upperHz = 8000;
            const int filterCount = 24;
            var melFilterbank = CreateMelFilterbank(sampleRate, samples.Length, lowerHz, upperHz, filterCount);
            //ShowFilterbank("Mel Filterbank", melFilterbank, sampleRate);

            var filtered = spec.Dot(melFilterbank);
            for (var i = 0; i < filtered.Length; i++)
                filtered[i] = Math.Log10(filtered[i]);

            //var filteredSeries = new LineSeries() { MarkerType = MarkerType.Circle };
            //filteredSeries.Points.AddRange(filtered.Select((x, i) => new DataPoint(MelToHz(HzToMel(lowerHz) + HzToMel(upperHz - lowerHz) / (filterCount + 1) * (i + 1)), x)));
            //Program.ShowPlot(new PlotModel() { Title = "フィルター結果", Series = { filteredSeries } });

            CosineTransform.DCT(filtered);

            const int resultDimension = 12;
            var result = new double[resultDimension];
            Array.Copy(filtered, 1, result, 0, resultDimension);

            // 逆変換結果を表示
            //var inv = new double[resultDimension + 1];
            //Array.Copy(result, 0, inv, 1, resultDimension);
            //CosineTransform.IDCT(inv);
            //var invSeries = new LineSeries() { MarkerType = MarkerType.Circle };
            //invSeries.Points.AddRange(inv.Select((x, i) => new DataPoint(MelToHz(HzToMel(lowerHz) + HzToMel(upperHz - lowerHz) / (resultDimension + 1) * (i + 1)), x)));
            //Program.ShowPlot(new PlotModel() { Title = "逆変換", Series = { invSeries } });

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
