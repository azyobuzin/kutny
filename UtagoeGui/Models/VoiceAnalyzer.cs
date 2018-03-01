using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kutny.Common;
using NAudio.Wave;
using PitchDetector;

namespace UtagoeGui.Models
{
    internal class VoiceAnalyzer
    {
        private readonly Dictionary<VowelClassifierType, Task<VowelClassifier>> _vowelClassifierTasks = new Dictionary<VowelClassifierType, Task<VowelClassifier>>();

        public void StartLearning()
        {
            var dir = CommonUtils.GetTrainingDataDirectory();
            var trainingData = new[] {
                Path.Combine(dir, "あいうえお 2017-12-18 00-17-09.csv"),
                Path.Combine(dir, "あいうえお 2018-01-20 16-48-52.csv")
            };

            this._vowelClassifierTasks[VowelClassifierType.MfccSupportVectorMachine] =
                Task.Run(async () =>
                {
                    var classifier = new SvmVowelClassifier();
                    await Task.WhenAll(trainingData.Select(classifier.AddTrainingDataAsync)).ConfigureAwait(false);
                    classifier.Learn();
                    return (VowelClassifier)classifier;
                });

            this._vowelClassifierTasks[VowelClassifierType.MfccNeuralNetwork] =
                Task.Run(async () =>
                {
                    var classifier = new NeuralNetworkVowelClassifier();
                    await Task.WhenAll(trainingData.Select(classifier.AddTrainingDataAsync)).ConfigureAwait(false);
                    classifier.Learn();
                    return (VowelClassifier)classifier;
                });

            this._vowelClassifierTasks[VowelClassifierType.MelSpectrumSupportVectorMachine] =
                Task.Run(async () =>
                {
                    var classifier = new MelSpectrumVowelClassifier();
                    await Task.WhenAll(trainingData.Select(classifier.AddTrainingDataAsync)).ConfigureAwait(false);
                    classifier.Learn();
                    return (VowelClassifier)classifier;
                });
        }

        public async ValueTask<VoiceAnalysisResult> Analyze(VowelClassifierType classifierType, ISampleProvider provider)
        {
            // TODO: あまりにも無駄な再計算が多い & 並列化しろ

            const int vowelWindowSize = 2048;
            const int pitchWindowSize = 1024;

            provider = provider.ToMono();
            var sampleRate = provider.WaveFormat.SampleRate;

            var blocks = new List<NoteBlockModel>();

            var classifier = await this._vowelClassifierTasks[classifierType].ConfigureAwait(false);
            var samples = new float[Logics.AnalysisUnit];

            for (var unitCount = 0; ; unitCount++)
            {
                // 4096 サンプルを読み込み
                for (var readSamples = 0; readSamples < samples.Length;)
                {
                    var count = provider.Read(samples, readSamples, samples.Length - readSamples);
                    if (count == 0) return new VoiceAnalysisResult(blocks.ToImmutableArray(), unitCount);
                    readSamples += count;
                }

                var maxPower = 0f;
                foreach (var x in samples)
                {
                    if (x > maxPower)
                        maxPower = x;
                }

                // 音量小さすぎ
                if (maxPower < 0.15) continue;

                // 512 ずつずらしながら母音認識
                var vowelCandidates = new int[(int)VowelType.Other + 1];
                for (var offset = 0; offset <= Logics.AnalysisUnit - vowelWindowSize; offset += 512)
                {
                    vowelCandidates[(int)classifier.Decide(new ReadOnlySpan<float>(samples, offset, vowelWindowSize), sampleRate)]++;
                }

                var vowelCandidate = default(VowelType?);
                var maxNumOfVotes = 0;
                for (var j = 0; j < vowelCandidates.Length; j++)
                {
                    if (vowelCandidates[j] > maxNumOfVotes)
                    {
                        maxNumOfVotes = vowelCandidates[j];
                        vowelCandidate = (VowelType)j;
                    }
                    else if (vowelCandidates[j] == maxNumOfVotes)
                    {
                        vowelCandidate = null;
                    }
                }

                // 母音が定まらなかったので、終了
                if (!vowelCandidate.HasValue || vowelCandidate.Value == VowelType.Other)
                    continue;

                // 512 ずつずらしながらピッチ検出
                const int pitchOffsetDelta = 512;
                var basicFreqs = new List<double>((Logics.AnalysisUnit - pitchOffsetDelta) / pitchOffsetDelta);
                for (var offset = 0; offset <= Logics.AnalysisUnit - pitchWindowSize; offset += pitchOffsetDelta)
                {
                    var f = McLeodPitchMethod.EstimateFundamentalFrequency(
                        sampleRate,
                        new ReadOnlySpan<float>(samples, offset, pitchWindowSize)
                    );

                    if (f.HasValue) basicFreqs.Add(f.Value);
                }

                // ピッチ検出に失敗したので終了
                if (basicFreqs.Count == 0) continue;

                basicFreqs.Sort();
                var basicFreq = basicFreqs[basicFreqs.Count / 2]; // 中央値
                var noteNum = CommonUtils.HzToMidiNote(basicFreq);

                var block = new NoteBlockModel(unitCount, noteNum, vowelCandidate.Value);

                if (blocks.Count == 0 || !blocks[blocks.Count - 1].MergeIfPossible(block))
                    blocks.Add(block);
            }
        }
    }
}
