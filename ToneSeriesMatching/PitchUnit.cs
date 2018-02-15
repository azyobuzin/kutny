namespace ToneSeriesMatching
{
    public struct PitchUnit
    {
        public int UnitIndex { get; }

        /// <summary>
        /// 振幅の実効値
        /// </summary>
        public double Rms { get; }

        /// <summary>
        /// 音高 [0, 12)
        /// </summary>
        public double NormalizedPitch { get; }

        public PitchUnit(int unitIndex, double rms, double normalizedPitch)
        {
            this.UnitIndex = unitIndex;
            this.Rms = rms;
            this.NormalizedPitch = normalizedPitch;
        }
    }
}
