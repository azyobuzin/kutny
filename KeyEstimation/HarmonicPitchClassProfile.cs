using System;
using System.Collections.Generic;
using System.Linq;
using Accord.Math;
using Accord.Math.Transforms;
using Kutny.Common;

namespace KeyEstimation
{
    public static class HarmonicPitchClassProfile
    {
        public static double[] MagnitudeSpectrum(ReadOnlySpan<float> frame, int nfft)
        {
            var spec = new double[nfft];

            for (var i = 0; i < frame.Length; i++)
            {
                // Blackman Harris 62 dB
                var w = (0.44859 + 0.49364 * Math.Cos(2.0 * Math.PI * i / frame.Length)
                    + 0.05677 * Math.Cos(4.0 * Math.PI * i / frame.Length)) / frame.Length;

                // センタリング
                spec[(nfft - frame.Length) / 2 + i] = frame[i] * w;
            }

            var imag = new double[nfft];
            FourierTransform2.FFT(spec, imag, FourierTransform.Direction.Forward);

            for (var i = 0; i < spec.Length; i++)
                spec[i] = Math.Sqrt(spec[i] * spec[i] + imag[i] * imag[i]);

            return spec;
        }

        public static IReadOnlyList<SpectrumPeak> PickPeaks(double[] spectrum, int sampleRate, int nPeaks, double minSpace)
        {
            var peakCandidateIndexes = new List<int>();

            const double lowerHz = 100;
            const double upperHz = 5000;

            var lowerIndex = (int)Math.Ceiling(lowerHz / ((double)sampleRate / spectrum.Length));
            var upperIndex = (int)(upperHz / ((double)sampleRate / spectrum.Length));

            for (var i = lowerIndex; i <= upperIndex; i++)
            {
                if (spectrum[i] >= spectrum[i - 1] && spectrum[i] >= spectrum[i + 1])
                    peakCandidateIndexes.Add(i);
            }

            if (peakCandidateIndexes.Count == 0) return Array.Empty<SpectrumPeak>();

            peakCandidateIndexes.Sort((x, y) => spectrum[x].CompareTo(spectrum[y]));
            var maxMagnitude = spectrum[peakCandidateIndexes[peakCandidateIndexes.Count - 1]];
            var thresholdDb = MagnitudeToDb(maxMagnitude) - 100.0;

            var peaks = new List<SpectrumPeak>(nPeaks);

            while (peakCandidateIndexes.Count > 0)
            {
                var index = peakCandidateIndexes[peakCandidateIndexes.Count - 1];
                var mag = spectrum[index];

                // 放物線補間
                var a = MagnitudeToDb(spectrum[index - 1]);
                var b = MagnitudeToDb(mag);
                var c = MagnitudeToDb(spectrum[index + 1]);
                var p = (a - c) / (a - 2.0 * b + c) / 2.0;

                var db = b - (a - c) * p / 4.0;

                // 補間に失敗したら、値をそのまま使う
                if (double.IsNaN(db) || double.IsInfinity(db))
                    db = b;

                if (db <= thresholdDb) break; // 小さすぎるので終わり

                var f = (index + p) * (sampleRate / spectrum.Length);
                peaks.Add(new SpectrumPeak(f, DbToMagnitude(db)));

                // minSpace より差が小さいものを削除
                var i = peakCandidateIndexes.Count - 1;
                while (true)
                {
                    var j = i - 1;
                    if (j >= 0 && mag - spectrum[peakCandidateIndexes[j]] <= minSpace)
                        i--;
                    else
                        break;
                }
                peakCandidateIndexes.RemoveRange(i, peakCandidateIndexes.Count - i);
            }

            return peaks;
        }

        public static double[] HpcpVector(IEnumerable<SpectrumPeak> peaks, int size, double fRef)
        {
            var weightWindowSize = 4.0 / 3.0 * (size / 12.0);
            var hpcp = new double[size];

            foreach (var peak in peaks)
            {
                for (var tone = 0; tone < size; tone++)
                {
                    // 基準周波数
                    var fn = fRef * Math.Pow(2.0, (double)tone / size);

                    //基準との距離
                    var t = 12 * Math.Log(peak.Frequency / fn, 2);
                    var d1 = t - 12.0 * Math.Floor(t / 12.0);
                    var d2 = t - 12.0 * Math.Ceiling(t / 12.0);
                    var distance = Math.Abs(d1) <= Math.Abs(d2) ? d1 : d2;

                    if (Math.Abs(distance) <= weightWindowSize / 2.0)
                    {
                        var weight = Math.Cos(Math.PI * distance / weightWindowSize);
                        weight *= weight;

                        var power = peak.Magnitude;
                        power *= power;

                        hpcp[tone] += weight * power;
                    }
                }
            }

            // 正規化
            var max = hpcp.Max();
            for (var i = 0; i < hpcp.Length; i++)
                hpcp[i] = hpcp[i] / max;

            return hpcp;
        }

        public static double[] AverageHpcp(ReadOnlySpan<float> samples, int sampleRate, int size, int nFrame = 4096, int nHop = 512, bool estimateReferenceFrequency = true)
        {
            var nfft = 4096;
            while (nfft <= nFrame) nfft *= 2;

            // よくわからないので、 size = 12 のとき半音を 10 等分
            // size が 2 倍になったら、 2 倍細かくする
            var histRes = 1.0 / (10.0 * (size / 12.0));

            // 2 秒分持っておく
            var devBuffer = new Buffer<FramDeviation>((int)(2.0 * sampleRate / nHop));

            var hpcp = new double[size];

            var count = 0;
            for (var start = 0; start + nFrame <= samples.Length; start += nHop)
            {
                count++;

                // スペクトル分析
                var spec = MagnitudeSpectrum(samples.Slice(start, nFrame), nfft);
                var peaks = PickPeaks(spec, sampleRate, 100, 2);

                double fref;
                if (estimateReferenceFrequency)
                {
                    // 瞬時基準周波数
                    var dev = InstantaneousDeviation(peaks, histRes);
                    devBuffer.Push(new FramDeviation(dev, peaks.Sum(x => x.Magnitude * x.Magnitude)));

                    // 大域基準周波数
                    dev = devBuffer.Count == 1 ? dev : GlobalDeviation(devBuffer, histRes);
                    fref = DeviationToFrequency(dev);
                }
                else
                {
                    fref = 440.0;
                }

                var v = HpcpVector(peaks, size, fref);

                for (var i = 0; i < size; i++)
                    hpcp[i] += v[i];
            }

            if (count >= 2)
            {
                // 平均
                for (var i = 0; i < hpcp.Length; i++)
                    hpcp[i] /= count;
            }

            return hpcp;
        }

        public static double InstantaneousDeviation(IEnumerable<SpectrumPeak> peaks, double resolution)
        {
            var classCount = (int)Math.Ceiling(1.0 / resolution);
            var hist = new double[classCount];

            foreach (var peak in peaks)
            {
                var b = 12.0 * Math.Log(peak.Frequency / 440.0, 2);
                var localDistance = b - Math.Round(b);

                hist[(int)((localDistance + 0.5) / resolution)] += peak.Magnitude;
            }

            return -0.5 + resolution * (hist.ArgMax() + 1);
        }

        public static double GlobalDeviation(IEnumerable<FramDeviation> frames, double resolution)
        {
            var classCount = (int)Math.Ceiling(1.0 / resolution);
            var hist = new double[classCount];

            foreach (var frame in frames)
            {
                var c = (frame.Deviation + 0.5) / resolution;
                // resolution が InstantaneousDeviation と同じときに、選ばれるべき階級が 1 大きくなるので補正
                hist[c % 1 == 0 ? (int)c - 1 : (int)c] += frame.Energy;
            }

            return -0.5 + resolution * (hist.ArgMax() + 1);
        }

        public static double DeviationToFrequency(double deviation) => 440.0 * Math.Pow(2.0, deviation / 12.0);

        private static double MagnitudeToDb(double magnitude) => 20.0 * Math.Log10(magnitude);
        private static double DbToMagnitude(double db) => Math.Pow(10.0, db / 20.0);

        public struct SpectrumPeak
        {
            public double Frequency { get; }
            public double Magnitude { get; }

            public SpectrumPeak(double frequency, double magnitude)
            {
                this.Frequency = frequency;
                this.Magnitude = magnitude;
            }
        }

        public struct FramDeviation
        {
            public double Deviation { get; }
            public double Energy { get; }

            public FramDeviation(double deviation, double energy)
            {
                this.Deviation = deviation;
                this.Energy = energy;
            }
        }
    }
}
