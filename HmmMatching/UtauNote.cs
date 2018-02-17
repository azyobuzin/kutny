using System.Runtime.InteropServices;

namespace HmmMatching
{
    [StructLayout(LayoutKind.Auto)]
    public struct UtauNote
    {
        /// <summary>
        /// 開始位置（4分音符 480 として計算）
        /// </summary>
        public int Position { get; }

        /// <summary>
        /// 長さ（4分音符 480 として計算）
        /// </summary>
        public int Length { get; }

        /// <summary>
        /// 音高を表す MIDI 相当の番号。負の値のとき休符を表す。
        /// </summary>
        public int NoteNumber { get; }

        public UtauNote(int position, int length, int noteNumber)
        {
            this.Position = position;
            this.Length = length;
            this.NoteNumber = noteNumber;
        }

        public bool IsRestNote => this.NoteNumber < 0;
    }
}
