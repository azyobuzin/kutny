using System;
using PitchDetector;

namespace UtagoeGui.Models
{
    public class NoteBlockModel
    {
        public NoteBlockModel(int start, int noteNumber, VowelType vowelType)
        {
            if (noteNumber > Logics.MaximumNoteNumber || noteNumber < Logics.MinimumNoteNumber)
                throw new ArgumentOutOfRangeException(nameof(noteNumber));

            this.Start = start;
            this.NoteNumber = noteNumber;
            this.VowelType = vowelType;
            this.Span = 1;
        }

        public int Start { get; }
        public int NoteNumber { get; }
        public VowelType VowelType { get; }
        public int Span { get; internal set; }

        internal bool MergeIfPossible(NoteBlockModel other)
        {
            if (other.Start == this.Start + this.Span
                && other.NoteNumber == this.NoteNumber
                && other.VowelType == this.VowelType)
            {
                this.Span++;
                return true;
            }

            return false;
        }
    }
}
