namespace ToneSeriesMatching
{
    public struct PitchUnit
    {
        /// <summary>
        /// 音高 [0, 12)
        /// </summary>
        public double NormalizedPitch { get; }

        public PitchUnit(double normalizedPitch)
        {
            this.NormalizedPitch = normalizedPitch;
        }
    }
}
