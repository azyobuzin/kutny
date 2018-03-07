using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Kutny.Common
{
    public static class CommonUtils
    {
        private static string s_trainingDataDirectory;

        public static string GetTrainingDataDirectory()
        {
            if (s_trainingDataDirectory != null)
                return s_trainingDataDirectory;

            const string directoryName = "TrainingData";

            var inCurrentDir = Path.Combine(Directory.GetCurrentDirectory(), directoryName);
            if (Directory.Exists(inCurrentDir)) return inCurrentDir;

            // アセンブリの場所からさかのぼってみる
            for (var path = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                path != null;
                path = Path.GetDirectoryName(path))
            {
                var dir = Path.Combine(path, directoryName);
                if (Directory.Exists(dir))
                {
                    s_trainingDataDirectory = dir;
                    return dir;
                }
            }

            throw new DirectoryNotFoundException(directoryName + " ディレクトリが見つかりませんでした。");
        }

        public static string GetTrainingFile(string fileName)
        {
            return Path.Combine(GetTrainingDataDirectory(), fileName);
        }

        public static int HzToMidiNote(double f)
        {
            return (int)Math.Round(HzToMidiNoteDouble(f));
        }

        public static double HzToMidiNoteDouble(double f)
        {
            return 69.0 + 12.0 * Math.Log(f / 440.0, 2.0);
        }

        public static int ToChromaIndex(int i)
        {
            var m = i % 12;
            if (m < 0) m += 12;
            return m;
        }

        public static double MidiNoteToHz(double d)
        {
            return Math.Pow(2.0, (d - 69.0) / 12.0) * 440.0;
        }

        private static readonly string[] s_noteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        public static string ToNoteName(int num) => s_noteNames[num % 12];

        public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> kvp, out TKey key, out TValue value)
        {
            key = kvp.Key;
            value = kvp.Value;
        }

        public static int FindIndex<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            var i = 0;
            foreach (var element in source)
            {
                if (predicate(element)) return i;
                i++;
            }

            return -1;
        }
    }
}
