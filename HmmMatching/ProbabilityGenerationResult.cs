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

        public static ProbabilityGenerationResult CreateToStartState(double probability, bool viaNoSoundState)
        {
            return new ProbabilityGenerationResult(-1, probability, viaNoSoundState);
        }

        public bool ToStartState => this.ToNoteIndex < 0;
    }
}
