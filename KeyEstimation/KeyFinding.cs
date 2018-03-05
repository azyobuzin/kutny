using System;
using Accord.Math;
using Accord.Statistics;

namespace KeyEstimation
{
    public static class KeyFinding
    {
        // http://rnhart.net/articles/key-finding/
        private static readonly double[] s_majorProfile = { 6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88 };
        private static readonly double s_majorProfileMean;
        private static readonly double s_majorProfileVariance;

        private static readonly double[] s_minorProfile = { 6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17 };
        private static readonly double s_minorProfileMean;
        private static readonly double s_minorProfileVariance;

        static KeyFinding()
        {
            s_majorProfileMean = s_majorProfile.Mean();
            s_majorProfileVariance = Variance(s_majorProfile, s_majorProfileMean);

            s_minorProfileMean = s_minorProfile.Mean();
            s_minorProfileVariance = Variance(s_minorProfile, s_minorProfileMean);

            // 要素数で割る必要がないので自分で定義
            double Variance(double[] values, double mean)
            {
                var variance = 0.0;
                foreach (var value in values)
                {
                    var x = value - mean;
                    variance += x * x;
                }
                return variance;
            }
        }

        public static (int, KeyMode) FindKey(double[] chromaVector)
        {
            if (chromaVector == null) throw new ArgumentNullException(nameof(chromaVector));
            if (chromaVector.Length != 12) throw new ArgumentException();

            var correlations = new double[24];
            for (var i = 0; i < 12; i++)
            {
                correlations[i] = CorrelationCoefficient(chromaVector, i, KeyMode.Major);
                correlations[i + 12] = CorrelationCoefficient(chromaVector, i, KeyMode.Minor);
            }

            var argmax = correlations.ArgMax();
            return argmax < 12
                ? (argmax, KeyMode.Major)
                : (argmax - 12, KeyMode.Minor);
        }

        private static double CorrelationCoefficient(double[] chromaVector, int transpose, KeyMode mode)
        {
            double[] profile;
            double profileMean;
            double profileVariance;
            switch (mode)
            {
                case KeyMode.Major:
                    profile = s_majorProfile;
                    profileMean = s_majorProfileMean;
                    profileVariance = s_majorProfileVariance;
                    break;
                case KeyMode.Minor:
                    profile = s_minorProfile;
                    profileMean = s_minorProfileMean;
                    profileVariance = s_minorProfileVariance;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }

            var inputMean = chromaVector.Mean();
            var covariance = 0.0;
            var inputVariance = 0.0;

            for (var i = 0; i < 12; i++)
            {
                var x = chromaVector[(i + transpose) % 12];
                var y = profile[i];

                var diff = x - inputMean;
                covariance += diff * (y - profileMean);
                inputVariance += diff * diff;
            }

            return covariance / Math.Sqrt(inputVariance * profileVariance);
        }
    }

    public enum KeyMode
    {
        Major,
        Minor
    }
}
