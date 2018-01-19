using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace PitchDetector
{
    public static class PitchMathNet
    {
        public static float? EstimatePitch(int sampleRate, ReadOnlySpan<float> samples)
        {
            // SDF
            // 1. zero pad
            var sdf = new Complex32[samples.Length * 2];
            for (var i = 0; i < samples.Length; i++)
                sdf[i] = samples[i];

            // 2. FFT
            Fourier.Forward(sdf, FourierOptions.AsymmetricScaling);

            // 3. パワースペクトル
            for (var i = 0; i < sdf.Length; i++)
            {
                sdf[i] = sdf[i].MagnitudeSquared;
            }

            // 4. inverse FFT
            Fourier.Inverse(sdf, FourierOptions.AsymmetricScaling);

            // NSDF
            var nsdf = new float[samples.Length];
            var m = 0f;
            for (var i = 0; i < samples.Length; i++)
            {
                var x = samples[i];
                m += x * x;
                var inv = samples.Length - i - 1;
                x = samples[inv];
                m += x * x;

                nsdf[inv] = 2f * sdf[inv].Real / m;
            }

            // ピーク検出
            // TODO: parabolic interpolation
            var maxCorrelation = 0f;
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

            var threshold = maxCorrelation * 0.8f;
            var delay = peaks.Find(x => x.Correlation >= threshold).Delay;
            return (float)sampleRate / delay;
        }

        [StructLayout(LayoutKind.Auto)]
        private struct PeakPoint
        {
            public int Delay { get; set; }
            public float Correlation { get; set; }

            public PeakPoint(int delay, float correlation)
            {
                this.Delay = delay;
                this.Correlation = correlation;
            }
        }
    }
}
