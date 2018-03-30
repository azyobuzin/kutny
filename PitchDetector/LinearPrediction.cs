using System;
using System.Numerics;

namespace PitchDetector
{
    public static class LinearPrediction
    {
        private static float Autocorrelation(ReadOnlySpan<float> input, int lag)
        {
            // 0 から N - 1 - lag までの自己相関（短時間自己相関関数）
            var end = input.Length - lag;
            var r = 0f;
            for (var i = 0; i < end; i++)
                r += input[i] * input[i + lag];
            return r;
        }

        public static LinearPredictionResult ForwardLinearPrediction(ReadOnlySpan<float> input, int order)
        {
            // 使う自己相関を用意
            var autocorrelations = new float[order + 1];
            for (var i = 0; i < autocorrelations.Length; i++)
                autocorrelations[i] = Autocorrelation(input, i);

            // 初期値
            var coefficients = new float[order + 1];
            var swapCoefficients = new float[order + 1];
            coefficients[0] = 1;
            coefficients[1] = -autocorrelations[1] / autocorrelations[0];

            var error = autocorrelations[0] + autocorrelations[1] * coefficients[1];

            for (var k = 1; k < order; k++)
            {
                // いい感じに埋め合わせるための係数 λ
                var combinationCoefficient = 0f;
                for (var j = 0; j <= k; j++)
                    combinationCoefficient -= coefficients[j] * autocorrelations[k + 1 - j];
                combinationCoefficient /= error;

                // A = U + λV による更新
                for (var i = 0; i <= k + 1; i++)
                {
                    swapCoefficients[i] = coefficients[i] + combinationCoefficient * coefficients[k + 1 - i];
                }

                // インスタンス交換
                var tmp = swapCoefficients;
                swapCoefficients = coefficients;
                coefficients = tmp;

                // 誤差の更新
                error *= 1f - combinationCoefficient * combinationCoefficient;
            }

            return new LinearPredictionResult(coefficients, error);
        }

        public static Complex[] FirFrequencyResponse(ReadOnlySpan<float> coefficients, int length)
        {
            var result = new Complex[length];
            var angleRate = 2.0 * Math.PI / length;

            for (var i = 0; i < length; i++)
            {
                var x = Complex.Exp(-Complex.ImaginaryOne * (angleRate * i));
                var y = (Complex)coefficients[0];

                var xpow = Complex.One;

                for (var j = 1; j < coefficients.Length; j++)
                {
                    xpow *= x;
                    y += coefficients[j] * xpow;
                }

                result[i] = 1.0 / y;
            }

            return result;
        }
    }

    public struct LinearPredictionResult
    {
        public float[] Coefficients { get; }
        public float Error { get; }

        public LinearPredictionResult(float[] coefficients, float error)
        {
            this.Coefficients = coefficients;
            this.Error = error;
        }
    }
}
