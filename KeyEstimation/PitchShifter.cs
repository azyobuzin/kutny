using System;

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
                input = MakeConcatenatedSignals(input, neededLength);
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

        private static float[] MakeConcatenatedSignals(ReadOnlySpan<float> input, int neededLength)
        {
            var times = neededLength / input.Length + 1;

            var windowed = new float[input.Length];
            for (var i = 0; i < windowed.Length; i++)
                windowed[i] = input[i] * TrapezoidalWindow(windowed.Length, i);

            var output = new float[input.Length * times];
            for (var i = -1; ; i++)
            {
                const double hop = 2.0 / 3.0;
                var copyDestStart = (int)(input.Length * hop * i);
                for (var j = 0; j < windowed.Length; j++)
                {
                    var copyDestIndex = copyDestStart + j;
                    if (copyDestIndex < 0) continue;
                    if (copyDestIndex >= output.Length) return output;
                    output[copyDestIndex] += windowed[j];
                }
            }
        }

        /// <summary>
        /// 台形型の窓関数
        /// </summary>
        private static float TrapezoidalWindow(int length, int n)
        {
            var p = length / 3.0;

            if (n < p)
            {
                var r = n / p;
                return 0.5f - (float)Math.Cos(r * Math.PI) / 2.0f;
            }
            else if (n > p * 2.0)
            {
                var r = n / p - 2.0;
                return 0.5f + (float)Math.Cos(r * Math.PI) / 2.0f;
            }
            else
            {
                return 1.0f;
            }
        }
    }
}
