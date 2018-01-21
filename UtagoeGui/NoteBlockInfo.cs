using PitchDetector;

namespace UtagoeGui
{
    internal class NoteBlockInfo
    {
        public NoteBlockInfo(int start, int noteNumber, VowelType vowelType)
        {
            this.Start = start;
            this.NoteNumber = noteNumber;
            this.VowelType = vowelType;
            this.Span = 1;
        }

        public int Start { get; }
        public int NoteNumber { get; }
        public VowelType VowelType { get; }
        public int Span { get; set; }

        public bool MergeIfPossible(NoteBlockInfo other)
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
