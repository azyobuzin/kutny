using System;
using PitchDetector;
using UtagoeGui.Infrastructures;

namespace UtagoeGui.Models
{
    public class CorrectNoteBlockModel : NotificationObject2
    {
        public CorrectNoteBlockModel(int start, int length, int noteNumber, string lyric)
        {
            if (noteNumber > Logics.MaximumNoteNumber || noteNumber < Logics.MinimumNoteNumber)
                throw new ArgumentOutOfRangeException(nameof(noteNumber));

            if (string.IsNullOrEmpty(lyric))
                throw new ArgumentNullException(nameof(lyric));

            this.Start = start;
            this.Length = length;
            this.NoteNumber = noteNumber;
            this.Lyric = lyric;
            this.VowelType = Logics.SyllabaryToVowelType(lyric);
        }

        public int Start { get; }
        public int Length { get; }
        public int NoteNumber { get; }
        public string Lyric { get; }
        public VowelType VowelType { get; }

        private double _startPositionInAnalysisUnits;
        public double StartPositionInAnalysisUnits
        {
            get => this._startPositionInAnalysisUnits;
            set => this.Set(ref this._startPositionInAnalysisUnits, value);
        }

        private double _lengthInAnalysisUnits;
        public double LengthInAnalysisUnits
        {
            get => this._lengthInAnalysisUnits;
            set => this.Set(ref this._lengthInAnalysisUnits, value);
        }
    }
}
