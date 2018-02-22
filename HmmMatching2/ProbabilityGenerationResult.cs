namespace HmmMatching
{
    public class ProbabilityGenerationResult
    {
        public int ToNoteIndex { get; }
        public double DirectProbability { get; }
        public double ProbabilityViaNoSoundState { get; }

        public ProbabilityGenerationResult(int toNoteIndex, double directProbability, double probabilityViaNoSoundState)
        {
            this.ToNoteIndex = toNoteIndex;
            this.DirectProbability = directProbability;
            this.ProbabilityViaNoSoundState = probabilityViaNoSoundState;
        }
    }
}
