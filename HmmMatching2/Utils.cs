namespace HmmMatching
{
    internal static class Utils
    {
        private static readonly string[] s_noteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B", "C" };
        public static string ToNoteName(int num) => s_noteNames[num];
    }
}
