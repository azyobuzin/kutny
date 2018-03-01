using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Accord.Math;
using Accord.Math.Transforms;

namespace Kutny.Common
{
    public static class McLeodPitchMethod
    {
        /// <summary>
        /// サンプル単位での基本周期を求める
        /// </summary>
        public static double? EstimateFundamentalDelay(ReadOnlySpan<float> samples)
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

            // ピーク検出
            var maxCorrelation = 0.0;
            var peaks = new List<PeakPoint>();
            for (var i = 1; i < samples.Length; i++)
            {
                if (nsdf[i] >= 0) continue; // 最初に負になるところまでスキップ

                i++;
                for (; i < samples.Length; i++)
                {
                    if (nsdf[i] > 0)
                    {
                        var currentMaxIndex = i;
                        var currentMax = nsdf[i];
                        i++;

                        for (; i < samples.Length; i++)
                        {
                            if (nsdf[i] > currentMax)
                            {
                                currentMaxIndex = i;
                                currentMax = nsdf[i];
                            }
                            else if (nsdf[i] < 0)
                            {
                                // 0 未満になったので一山終了
                                break;
                            }
                        }

                        var peak = currentMaxIndex < nsdf.Length - 1
                            && Interporate(
                                currentMaxIndex - 1, nsdf[currentMaxIndex - 1],
                                currentMaxIndex, currentMax,
                                currentMaxIndex + 1, nsdf[currentMaxIndex + 1]
                            ) is ValueTuple<double, double> interpolated
                            ? new PeakPoint(interpolated.Item1, interpolated.Item2)
                            : new PeakPoint(currentMaxIndex, currentMax);

                        peaks.Add(peak);

                        if (peak.Correlation > maxCorrelation)
                            maxCorrelation = peak.Correlation;
                    }
                }

                break;
            }

            if (peaks.Count == 0) return null; // 推定失敗

            var threshold = maxCorrelation * 0.8;
            var mainPeak = peaks.Find(x => x.Correlation >= threshold);

            return mainPeak.Delay;
        }

        public static double? EstimateFundamentalFrequency(int sampleRate, ReadOnlySpan<float> samples)
        {
            return sampleRate / EstimateFundamentalDelay(samples);
        }

        /// <summary>
        /// 2次関数として放物線補間して頂点の座標を求める
        /// </summary>
        private static (double, double)? Interporate(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            // y = ax^2 + bx + c として、 a, b, c を解く
            var x12 = x1 * x1;
            var x22 = x2 * x2;
            var x32 = x3 * x3;

            var det = x12 * (x2 - x3) + x22 * (x3 - x1) + x32 * (x1 - x2);
            if (det == 0) return null;

            var a = (y1 * (x2 - x3) + y2 * (x3 - x1) + y3 * (x1 - x2)) / det;
            var b = (y1 * (x32 - x22) + y2 * (x12 - x32) + y3 * (x22 - x12)) / det;
            var c = (y1 * (x22 * x3 - x32 * x2) + y2 * (x32 * x1 - x12 * x3) + y3 * (x12 * x2 - x22 * x1)) / det;

            // 頂点の公式
            var x = -b / (2.0 * a);
            return (x, a * x * x + b * x + c);
        }

        [StructLayout(LayoutKind.Auto)]
        private struct PeakPoint
        {
            public double Delay { get; set; }
            public double Correlation { get; set; }

            public PeakPoint(double delay, double correlation)
            {
                this.Delay = delay;
                this.Correlation = correlation;
            }
        }
    }
}
