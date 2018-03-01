using System;
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
            return (int)Math.Round(69.0 + 12.0 * Math.Log(f / 440.0, 2.0));
        }
    }
}
