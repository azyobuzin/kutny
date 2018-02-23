using System.Collections.Generic;

namespace HmmMatching
{
    public class ProbabilityGenerationContext
    {
        public LinkedListNode<UtauNote> TargetNoteNode { get; }
        public double RemainingProbability { get; }

        public ProbabilityGenerationContext(LinkedListNode<UtauNote> targetNoteNode, double remainingProbability)
        {
            this.TargetNoteNode = targetNoteNode;
            this.RemainingProbability = remainingProbability;
        }
    }
}
