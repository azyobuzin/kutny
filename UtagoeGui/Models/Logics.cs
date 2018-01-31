using System;
using PitchDetector;

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

        public static VowelType SyllabaryToVowelType(string s)
        {
            switch (s[s.Length - 1])
            {
                case 'あ':
                case 'ぁ':
                case 'か':
                case 'が':
                case 'さ':
                case 'ざ':
                case 'た':
                case 'だ':
                case 'な':
                case 'は':
                case 'ば':
                case 'ぱ':
                case 'ま':
                case 'や':
                case 'ゃ':
                case 'ら':
                case 'わ':
                    return VowelType.A;
                case 'い':
                case 'ぃ':
                case 'き':
                case 'ぎ':
                case 'し':
                case 'じ':
                case 'ち':
                case 'ぢ':
                case 'に':
                case 'ひ':
                case 'び':
                case 'ぴ':
                case 'み':
                case 'り':
                    return VowelType.I;
                case 'う':
                case 'ぅ':
                case 'く':
                case 'ぐ':
                case 'す':
                case 'ず':
                case 'つ':
                case 'づ':
                case 'ぬ':
                case 'ふ':
                case 'ぶ':
                case 'ぷ':
                case 'む':
                case 'ゆ':
                case 'ゅ':
                case 'る':
                    return VowelType.U;
                case 'え':
                case 'ぇ':
                case 'け':
                case 'げ':
                case 'せ':
                case 'ぜ':
                case 'て':
                case 'で':
                case 'ね':
                case 'へ':
                case 'べ':
                case 'ぺ':
                case 'め':
                case 'れ':
                    return VowelType.E;
                case 'お':
                case 'ぉ':
                case 'こ':
                case 'ご':
                case 'そ':
                case 'ぞ':
                case 'と':
                case 'ど':
                case 'の':
                case 'ほ':
                case 'ぼ':
                case 'ぽ':
                case 'も':
                case 'よ':
                case 'ょ':
                case 'ろ':
                case 'を':
                    return VowelType.O;
                case 'ん':
                    return VowelType.N;
                default:
                    throw new ArgumentException();
            }
        }
    }
}
