﻿using System;
using System.Collections.Generic;
using Accord.Math;

namespace HmmMatching
{
    using State = HmmState<PitchHmmState, PitchHmmEmission>;

    public class PitchHmmGenerator
    {
        public static PitchHmmGenerator Default { get; }

        static PitchHmmGenerator()
        {
            var x = new PitchHmmGenerator();
            x.AddProbabiltiyGenerator(ProbabilityGenerators.SelfLoop);
            //x.AddProbabiltiyGenerator(ProbabilityGenerators.StopSinging);
            //x.AddProbabiltiyGenerator(ProbabilityGenerators.MoveToFirstNoteInMeasure);
            //x.AddProbabiltiyGenerator(ProbabilityGenerators.MoveToFirstNoteInPreviousMeasure);
            x.AddProbabiltiyGenerator(ProbabilityGenerators.MoveToForwardNotes);
            x.AddProbabiltiyGenerator(ProbabilityGenerators.MoveToStartFromLastNote);
            Default = x;
        }

        private readonly List<ProbabilityGenerator> _generators = new List<ProbabilityGenerator>();

        public void AddProbabiltiyGenerator(ProbabilityGenerator generator)
        {
            this._generators.Add(generator ?? throw new ArgumentNullException(nameof(generator)));
        }

        public HmmState<PitchHmmState, PitchHmmEmission> Generate(IEnumerable<UtauNote> notes)
        {
            var noteList = new LinkedList<UtauNote>(notes);
            var model = new HiddenMarkovModel<PitchHmmState, PitchHmmEmission>();
            var startState = new State(PitchHmmState.CreateStartState(), NoSoundStateEmissionProbability);
            model.AddState(startState);

            var statesByNotes = new Dictionary<int, State>(noteList.Count);
            var noSoundStatesByNotes = new Dictionary<int, State>(noteList.Count);
            var node = noteList.First;
            while (node != null)
            {
                var note = node.Value;

                if (!note.IsRestNote)
                {
                    var s = new State(PitchHmmState.CreateEmittingSoundState(note), NoteProbability(note));
                    model.AddState(s);
                    statesByNotes.Add(note.Index, s);

                    if (node.Next != null)
                    {
                        var ns = new State(PitchHmmState.CreateNoSoundState(note), NoSoundStateEmissionProbability);
                        model.AddState(ns);
                        noSoundStatesByNotes.Add(note.Index, ns);
                    }
                }

                node = node.Next;
            }

            // スタート状態からの遷移確率を生成
            RunGenerators(startState, null);

            // 他の状態の遷移確率を生成
            node = noteList.First;
            while (node != null)
            {
                var note = node.Value;

                if (!note.IsRestNote)
                {
                    var state = statesByNotes[note.Index];
                    RunGenerators(state, node);
                }

                node = node.Next;
            }

            return startState;

            void RunGenerators(State fromState, LinkedListNode<UtauNote> noteNode)
            {
                var viaNoSoundEdges = new List<(int, double)>();
                var remainingProbability = 1.0;
                var totalProbabilityViaNoSoundState = 0.0;

                // 順番にジェネレータを回していく
                foreach (var generator in this._generators)
                {
                    var results = generator(new ProbabilityGenerationContext(noteList, noteNode, remainingProbability));
                    if (results == null) continue;

                    foreach (var result in results)
                    {
                        if (result == null || result.Probability == 0.0) continue;

                        if (result.Probability != 0.0)
                        {
                            if (!(result.Probability >= 0.0 && result.Probability <= 1.0)) // NaN 対応のために not を使う
                                throw new Exception("確率として不正な値です。");

                            if (result.ViaNoSoundState)
                            {
                                if (noteNode == null)
                                    throw new Exception("スタート状態から無音状態を経由することはできません。");

                                // 無音経由はあとでまとめて
                                viaNoSoundEdges.Add((result.ToNoteIndex, result.Probability));
                                totalProbabilityViaNoSoundState += result.Probability;
                            }
                            else
                            {
                                // 直接遷移の接続
                                var toState = result.ToNoteIndex < 0 ? startState : statesByNotes[result.ToNoteIndex];
                                fromState.AddOutgoingEdge(toState, Math.Log(result.Probability));
                            }

                            remainingProbability -= result.Probability;
                        }
                    }
                }

                const double errorMargin = 0.01;
                if (remainingProbability < -errorMargin)
                    throw new Exception("合計確率が 1 を超えました。");
                if (remainingProbability > errorMargin)
                    throw new Exception("合計確率が 1 になっていません。");

                if (viaNoSoundEdges.Count > 0)
                {
                    // 無音を経由した接続
                    var logTotalProbabilityViaNoSoundState = Math.Log(totalProbabilityViaNoSoundState);
                    var noSoundState = noSoundStatesByNotes[fromState.Value.ReportingNote.Index];
                    fromState.AddOutgoingEdge(noSoundState, logTotalProbabilityViaNoSoundState);

                    const double noSoundSelfLoopProbability = 0.3; // 無音状態をループする確率（雑音に反応してしまったり）
                    noSoundState.AddOutgoingEdge(noSoundState, Math.Log(noSoundSelfLoopProbability));

                    // (1.0 - noSoundSelfLoopProbability) / totalProbabilityViaNoSoundState
                    var adj = Math.Log(1.0 - noSoundSelfLoopProbability) - logTotalProbabilityViaNoSoundState;

                    foreach (var (toNoteIndex, probability) in viaNoSoundEdges)
                    {
                        // probability * (1.0 - noSoundSelfLoopProbability) / totalProbabilityViaNoSoundState
                        var logP = Math.Log(probability) + adj;

                        var toState = toNoteIndex < 0 ? startState : statesByNotes[toNoteIndex];
                        noSoundState.AddOutgoingEdge(toState, logP);
                    }
                }
            }
        }

        private static double NoSoundStateEmissionProbability(PitchHmmEmission emission)
        {
            // 無音 0.3、それ以外 0.7
            // 変化したタイミングで入力が来るので、無音でないパターンもそれなりに可能性があると考える
            return Math.Log(emission.IsSilent ? 0.3 : 0.7 / 12.0);
        }

        private static Func<PitchHmmEmission, double> NoteProbability(UtauNote note)
        {
            if (note == null) throw new ArgumentNullException(nameof(note));
            if (note.IsRestNote) throw new ArgumentException();

            // 正規分布
            const double stdDev = 0.6;
            var mean = note.NoteNumber % 12;
            return e =>
            {
                if (e.IsSilent) return double.NegativeInfinity;

                var p = Math.Max(
                    Math.Max(
                        NormalDistribution(mean, stdDev, e.NormalizedPitch),
                        NormalDistribution(mean - 12, stdDev, e.NormalizedPitch)
                    ),
                    NormalDistribution(mean + 12, stdDev, e.NormalizedPitch)
                );
                return Math.Log(p);
            };
        }

        private static double NormalDistribution(double mean, double stdDev, double x)
        {
            return Normal.Function((x - mean) / stdDev);
        }
    }

    public delegate IEnumerable<ProbabilityGenerationResult> ProbabilityGenerator(ProbabilityGenerationContext context);
}
