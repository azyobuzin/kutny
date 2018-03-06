using System;
using System.Numerics;
using Accord.Audio.Windows;
using Accord.Math;
using Accord.Math.Transforms;

namespace KeyEstimation
{
    public class PitchShifter
    {
        private float[] _nextBuffer;

        public float[] ShiftPitch(ReadOnlySpan<float> input, double rate)
        {
            var inputLength = input.Length;

            var resampleLength = inputLength * 1.5;
            var stretchedLength = resampleLength * rate;
            var neededLength = (int)Math.Ceiling(stretchedLength);

            if (inputLength < neededLength)
            {
                // 伸ばす必要あり
                //input = MakeConcatenatedSignals(input, neededLength);
                return Make3xSignals(input);
            }

            var resampled = new float[(int)Math.Ceiling(resampleLength)];
            var c = stretchedLength / (resampleLength * resampleLength);
            for (var i = 0; i < resampled.Length; i++)
            {
                // inputPos = (i / resampleLength) * (stretchedLength / resampleLength)
                var inputPos = (float)(i * (stretchedLength / resampleLength));
                var ai = (int)inputPos;
                var av = input[ai];
                float v;

                if (ai == input.Length - 1 || ai == inputPos)
                {
                    v = av;
                }
                else
                {
                    // 補間
                    var bv = input[ai + 1];
                    v = av + (bv - av) * (inputPos - ai);
                }

                resampled[i] = v * TrapezoidalWindow(resampled.Length, i);
            }

            var nextBuffer = this._nextBuffer;
            var output = new float[inputLength];
            for (var i = 0; i < output.Length; i++)
            {
                output[i] = i < nextBuffer?.Length
                    ? nextBuffer[i] + resampled[i]
                    : resampled[i];
            }

            nextBuffer = new float[resampled.Length - output.Length];
            Array.Copy(resampled, output.Length, nextBuffer, 0, nextBuffer.Length);
            this._nextBuffer = nextBuffer;

            return output;
        }


        /// <summary>
        /// フェーズボコーダで 3 倍に延ばす
        /// </summary>
        /// <remarks>
        /// http://web.uvic.ca/~mpierce/elec484/final_project/Report/FinalReport.pdf
        /// </remarks>
        private static float[] Make3xSignals(ReadOnlySpan<float> input)
        {
            var nhop = input.Length / 2; // スライド量
            var omega = new double[input.Length];
            for (var i = 0; i < omega.Length; i++)
                omega[i] = 2.0 * Math.PI * nhop * i / input.Length;

            var window = RaisedCosineWindow.Hann(input.Length);
            var fft = new Complex[input.Length];
            for (var i = 0; i < input.Length; i++)
            {
                fft[(fft.Length / 2 + i) % fft.Length] = input[i] * window[i];
            }

            FourierTransform2.FFT(fft, FourierTransform.Direction.Forward);

            var magnitudeSpectrum = fft.Magnitude();
            var phaseSpectrum = fft.Phase();

            var lastPhase = phaseSpectrum;
            var targetPhase = new double[phaseSpectrum.Length];
            for (var i = 0; i < targetPhase.Length; i++)
                targetPhase[i] = phaseSpectrum[i] + omega[i];

            var outputFrame = new float[input.Length];
            var output = new float[input.Length * 3];

            const int loopTimes = 7; // 7回合成する
            for (var i = 0; i < loopTimes; i++)
            {
                if (i >= 1)
                {
                    for (var j = 0; j < input.Length; j++)
                    {
                        var principleArg = phaseSpectrum[j] - targetPhase[j];
                        var unwrappedPhaseDiff = (principleArg + Math.PI) % (-2.0 * Math.PI) + Math.PI;
                        var instantaneousFreq = omega[j] + unwrappedPhaseDiff;
                        lastPhase[j] += instantaneousFreq;
                        targetPhase[j] = phaseSpectrum[j] + omega[j];
                        fft[j] = magnitudeSpectrum[j] * Complex.Exp(Complex.ImaginaryOne * lastPhase[j]);
                    }
                }

                FourierTransform2.FFT(fft, FourierTransform.Direction.Backward);

                for (var j = 0; j < outputFrame.Length; j++)
                    outputFrame[j] = (float)fft[(fft.Length / 2 + j) % fft.Length].Real * window[j];

                var copyDestStart = nhop * (i - 1);
                for (var j = 0; j < outputFrame.Length; j++)
                {
                    var copyDestIndex = copyDestStart + j;
                    if (copyDestIndex < 0) continue;
                    if (copyDestIndex >= output.Length) break;
                    output[copyDestIndex] = outputFrame[j];
                }
            }

            return output;
        }

        //private static float[] MakeConcatenatedSignals(ReadOnlySpan<float> input, int neededLength)
        //{
        //    var times = neededLength / input.Length + 1;

        //    var windowed = new float[input.Length];
        //    for (var i = 0; i < windowed.Length; i++)
        //        windowed[i] = input[i] * TrapezoidalWindow(windowed.Length, i);

        //    var output = new float[input.Length * times];
        //    for (var i = -1; ; i++)
        //    {
        //        const double hop = 2.0 / 3.0;
        //        var copyDestStart = (int)(input.Length * hop * i);
        //        for (var j = 0; j < windowed.Length; j++)
        //        {
        //            var copyDestIndex = copyDestStart + j;
        //            if (copyDestIndex < 0) continue;
        //            if (copyDestIndex >= output.Length) return output;
        //            output[copyDestIndex] += windowed[j];
        //        }
        //    }
        //}

        /// <summary>
        /// 台形型の窓関数
        /// </summary>
        private static float TrapezoidalWindow(int length, int n)
        {
            var p = length / 3.0;

            if (n < p)
            {
                return (float)(n * (1.0 / p));
            }
            else if (n > p * 2.0)
            {
                return (float)(1 - ((n - p * 2.0) * (1.0 / p)));
            }
            else
            {
                return 1.0f;
            }
        }
    }
}
