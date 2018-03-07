using Kutny.Common;

namespace KeyEstimation
{
    public struct Key
    {
        public int Tonic { get; }
        public KeyMode Mode { get; }

        public Key(int tonic, KeyMode mode)
        {
            this.Tonic = tonic;
            this.Mode = mode;
        }

        public override string ToString()
        {
            var s = CommonUtils.ToNoteName(this.Tonic);
            return this.Mode == KeyMode.Major ? s : s + "m";
        }
    }
}
