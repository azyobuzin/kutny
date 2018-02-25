using System.Collections.Generic;

namespace HmmMatching
{
    public class ProbabilityGenerationContext
    {
        public LinkedListNode<UtauNote> TargetNoteNode { get; }
        public int StartStateIndex { get; }
        public double RemainingProbability { get; }

        public ProbabilityGenerationContext(LinkedListNode<UtauNote> targetNoteNode, int startStateIndex, double remainingProbability)
        {
            this.TargetNoteNode = targetNoteNode;
            this.StartStateIndex = startStateIndex;
            this.RemainingProbability = remainingProbability;
        }
    }
}
