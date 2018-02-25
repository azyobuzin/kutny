namespace HmmMatching
{
    public class ProbabilityGenerationResult
    {
        public int ToNoteIndex { get; }
        public double Probability { get; }
        public bool ViaNoSoundState { get; }

        public ProbabilityGenerationResult(int toNoteIndex, double probability, bool viaNoSoundState)
        {
            this.ToNoteIndex = toNoteIndex;
            this.Probability = probability;
            this.ViaNoSoundState = viaNoSoundState;
        }
    }
}
