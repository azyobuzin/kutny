using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Accord.Math;
using McLeodPitchMethod = ToneSeriesMatching.McLeodPitchMethod;
using PitchUnit = ToneSeriesMatching.PitchUnit;
using UtauScriptReader = ToneSeriesMatching.UtauScriptReader;

namespace HmmMatching
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

                    if (Regex.IsMatch(reader.SectionName, "^#[0-9]+$"))
                    {
                        var lyric = reader.GetField("Lyric");
                        var length = int.Parse(reader.GetField("Length"), CultureInfo.InvariantCulture);
                        var noteNum = lyric == "R"
                            ? -1 // R は休符
                            : int.Parse(reader.GetField("NoteNum"), CultureInfo.InvariantCulture);

                        yield return new UtauNote(position, length, noteNum);

                        position += length;
                    }
                }
            }
        }

        private static HiddenMarkovModel<PitchHmmState, PitchHmmEmission> CreateHmm(string fileName)
        {
            var utauNotes = LoadUtauScript(fileName).ToArray();
            var model = new HiddenMarkovModel<PitchHmmState, PitchHmmEmission>();
            var states = Array.ConvertAll(utauNotes, x => model.AddState(new PitchHmmState(x.Position), NoteProbability(x)));

            var startState = model.AddState(new PitchHmmState(-1), StartStateEmissionProbability);

            // TODO: 接続
            throw new NotImplementedException();
        }

        private static double StartStateEmissionProbability(PitchHmmEmission emission)
        {
            // 無音 0.9、それ以外 0.1
            return emission.IsSilent ? 0.9 : 0.1 / 12.0;
        }

        private static Func<PitchHmmEmission, double> NoteProbability(UtauNote note)
        {
            if (note.IsRestNote)
            {
                // 無音 0.95、それ以外 0.05
                return e => e.IsSilent ? 0.95 : 0.05 / 12.0;
            }

            // 正規分布
            const double stdDev = 0.5;
            var mean = note.NoteNumber % 12;
            return e => e.IsSilent ? 0.0
                : Math.Max(
                    Math.Max(
                        NormalDistribution(mean, stdDev, e.NormalizedPitch),
                        NormalDistribution(mean - 12, stdDev, e.NormalizedPitch)
                    ),
                    NormalDistribution(mean + 12, stdDev, e.NormalizedPitch)
                );
        }

        private static double NormalDistribution(double mean, double stdDev, double x)
        {
            return Normal.Function((x - mean) / stdDev);
        }
    }
}
