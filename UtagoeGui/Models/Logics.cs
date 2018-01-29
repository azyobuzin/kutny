using System;

namespace UtagoeGui.Models
{
    public static class Logics
    {
        public const int AnalysisUnit = 4096;

        public const int MinimumNoteNumber = 0;
        public const int MaximumNoteNumber = 127;
        private static readonly string[] s_noteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

        public static string ToNoteName(int noteNumber)
        {
            if (noteNumber < MinimumNoteNumber || noteNumber > MaximumNoteNumber)
                throw new ArgumentOutOfRangeException(nameof(noteNumber));

            return s_noteNames[noteNumber % 12] + (noteNumber / 12).ToString();
        }
    }
}
