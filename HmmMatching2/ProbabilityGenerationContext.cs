using System.Collections.Immutable;

namespace HmmMatching
{
    public class ProbabilityGenerationContext
    {
        public ImmutableArray<UtauNote> Notes { get; }
        public int TargetNoteIndex { get; }
        public double RemainingProbability { get; }

        public ProbabilityGenerationContext(ImmutableArray<UtauNote> notes, int targetNoteIndex, double remainingProbability)
        {
            this.Notes = notes;
            this.TargetNoteIndex = targetNoteIndex;
            this.RemainingProbability = remainingProbability;
        }
    }
}
