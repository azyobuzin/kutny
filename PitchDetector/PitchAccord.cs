using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Accord.Math;
using Accord.Math.Transforms;
using OxyPlot;
using OxyPlot.Series;

namespace PitchDetector
{
    public static class PitchAccord
    {
        public static double? EstimateBasicFrequency(int sampleRate, ReadOnlySpan<float> samples)
        {
            // ACF
            // 1. zero pad
            var acf = new Complex[samples.Length * 2];
            for (var i = 0; i < samples.Length; i++)
                acf[i] = samples[i];

            // 2. FFT
            FourierTransform2.FFT(acf, FourierTransform.Direction.Forward);

            // 3. パワースペクトル
            for (var i = 0; i < acf.Length; i++)
            {
                var x = acf[i];
                acf[i] = x.Real * x.Real + x.Imaginary * x.Imaginary;
            }

            // 4. inverse FFT
            FourierTransform2.FFT(acf, FourierTransform.Direction.Backward);

            // NSDF
            var nsdf = new double[samples.Length];
            var m = 0.0;
            for (var i = samples.Length - 1; i >= 0; i--)
            {
                var x = (double)samples[i];
                m += x * x;
                var inv = samples.Length - i - 1;
                x = samples[inv];
                m += x * x;

                nsdf[i] = 2.0 * acf[i].Real / m;
            }

            //var nsdfSeries = new LineSeries();
            //nsdfSeries.Points.AddRange(nsdf.Select((x, i) => new DataPoint(1.0 / sampleRate * i, x)));
            //var nsdfPlot = new PlotModel()
            //{
            //    Title = "NSDF",
            //    Series = { nsdfSeries }
            //};
            //Program.ShowPlot(nsdfPlot);

            // ピーク検出
            var maxCorrelation = 0.0;
            var peaks = new List<PeakPoint>();
            for (var i = 1; i < samples.Length; i++)
            {
                if (nsdf[i] > 0 && nsdf[i - 1] <= 0)
                {
                    var currentMax = new PeakPoint(i, nsdf[i]);
                    i++;

                    for (; i < samples.Length; i++)
                    {
                        if (nsdf[i] > currentMax.Correlation)
                        {
                            currentMax.Delay = i;
                            currentMax.Correlation = nsdf[i];
                        }
                        else if (nsdf[i] < 0)
                        {
                            // 0 未満になったので一山終了
                            break;
                        }
                    }

                    peaks.Add(currentMax);
                    if (currentMax.Correlation > maxCorrelation)
                        maxCorrelation = currentMax.Correlation;
                }
            }

            if (peaks.Count == 0) return null; // 推定失敗

            var threshold = maxCorrelation * 0.8;
            var mainPeak = peaks.Find(x => x.Correlation >= threshold);

            var delay = mainPeak.Delay == nsdf.Length - 1
                ? mainPeak.Delay
                : GetTopX(
                    mainPeak.Delay - 1, nsdf[mainPeak.Delay - 1],
                    mainPeak.Delay, mainPeak.Correlation,
                    mainPeak.Delay + 1, nsdf[mainPeak.Delay + 1]
                );

            Debug.Assert(delay >= mainPeak.Delay - 1 && delay <= mainPeak.Delay + 1);
            return sampleRate / delay;
        }

        /// <summary>
        /// 2次関数として放物線補間して頂点のX座標を求める
        /// </summary>
        private static double GetTopX(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            // y = ax^2 + bx + c

            var m = new[,]
            {
                { x1 * x1, x1, 1.0 },
                { x2 * x2, x2, 1.0 },
                { x3 * x3, x3, 1.0 }
            };

            // 行列で連立方程式を解くやつ
            var solution = m.Inverse(true).Dot(new[,] { { y1 }, { y2 }, { y3 } });

            var a = solution[0, 0];
            var b = solution[1, 0];
            //var c = solution[2, 0];

            // 頂点の公式
            return -b / (2.0 * a);
        }

        [StructLayout(LayoutKind.Auto)]
        private struct PeakPoint
        {
            public int Delay { get; set; }
            public double Correlation { get; set; }

            public PeakPoint(int delay, double correlation)
            {
                this.Delay = delay;
                this.Correlation = correlation;
            }
        }
    }
}
