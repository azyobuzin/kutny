using System;

namespace HmmMatching
{
    public struct PitchHmmEmission
    {
        /// <summary>
        /// 音高 [0, 12)
        /// </summary>
        public double NormalizedPitch { get; }

        public PitchHmmEmission(double normalizedPitch)
        {
            if (normalizedPitch < 0.0 || normalizedPitch >= 12.0)
                throw new ArgumentOutOfRangeException(nameof(normalizedPitch));

            this.NormalizedPitch = normalizedPitch;
        }

        public bool IsSilent => double.IsNaN(this.NormalizedPitch);

        public static PitchHmmEmission Silent { get; } = new PitchHmmEmission(double.NaN);
    }
}
