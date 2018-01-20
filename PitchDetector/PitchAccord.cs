using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Accord.Math;
using Accord.Math.Transforms;

namespace PitchDetector
{
    public static class PitchAccord
    {
        public static double? EstimateBasicFrequency(int sampleRate, ReadOnlySpan<float> samples)
        {
            // SDF
            // 1. zero pad
            var sdf = new Complex[samples.Length * 2];
            for (var i = 0; i < samples.Length; i++)
                sdf[i] = samples[i];

            // 2. FFT
            FourierTransform2.FFT(sdf, FourierTransform.Direction.Forward);

            // 3. パワースペクトル
            for (var i = 0; i < sdf.Length; i++)
            {
                var x = sdf[i];
                sdf[i] = x.Real * x.Real + x.Imaginary * x.Imaginary;
            }

            // 4. inverse FFT
            FourierTransform2.FFT(sdf, FourierTransform.Direction.Backward);

            // NSDF
            var nsdf = new double[samples.Length];
            var m = 0.0;
            for (var i = 0; i < samples.Length; i++)
            {
                var x = (double)samples[i];
                m += x * x;
                var inv = samples.Length - i - 1;
                x = samples[inv];
                m += x * x;

                nsdf[inv] = 2.0 * sdf[inv].Real / m;
            }

            // ピーク検出
            // TODO: parabolic interpolation
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
            var delay = peaks.Find(x => x.Correlation >= threshold).Delay;
            return (double)sampleRate / delay;
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
