using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ToneSeriesMatching
{
    public static class Program
    {
        public static void Main(string[] args)
        {
        }

        private static IEnumerable<UtauNote> LoadUtauScript(string fileName)
        {
            using (var reader = new UtauScriptReader(File.OpenRead(fileName)))
            {
                // ノート取得
                var position = 0;
                while (true)
                {
                    if (!reader.ReadSection())
                        throw new EndOfStreamException();

                    if (reader.SectionName == "#TRACKEND")
                        break;

                    if (Regex.IsMatch(reader.SectionName, "#[0-9]+"))
                    {
                        var lyric = reader.GetField("Lyric");
                        var length = int.Parse(reader.GetField("Length"), CultureInfo.InvariantCulture);

                        if (lyric != "R") // R は休符
                        {
                            var noteNum = int.Parse(reader.GetField("NoteNum"), CultureInfo.InvariantCulture);
                            yield return new UtauNote(position, noteNum);
                        }

                        position += length;
                    }
                }
            }
        }
    }
}
