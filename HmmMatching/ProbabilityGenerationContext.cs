using System.Collections.Generic;

namespace HmmMatching
{
    public class ProbabilityGenerationContext
    {
        public LinkedList<UtauNote> Notes { get; }
        public LinkedListNode<UtauNote> TargetNoteNode { get; }
        public double RemainingProbability { get; }

        public ProbabilityGenerationContext(LinkedList<UtauNote> notes, LinkedListNode<UtauNote> targetNoteNode, double remainingProbability)
        {
            this.Notes = notes;
            this.TargetNoteNode = targetNoteNode;
            this.RemainingProbability = remainingProbability;
        }
    }
}
