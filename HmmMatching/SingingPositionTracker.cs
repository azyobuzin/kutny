using System;
using System.Collections.Generic;
using Accord.Math;
using Kutny.Common;

namespace HmmMatching
{
    public class SingingPositionTracker
    {
        private readonly HiddenMarkovModel<PitchHmmState, PitchHmmEmission> _model;
        private readonly List<KeyValuePair<int, double>> _stateLogProbabilities;
        public UtauNote CurrentNote { get; private set; }

        public SingingPositionTracker(HmmState<PitchHmmState, PitchHmmEmission> startState)
        {
            this._model = startState.Model;

            // 初期状態はスタートが 1
            this._stateLogProbabilities = new List<KeyValuePair<int, double>>(this._model.States.Count)
            {
                new KeyValuePair<int, double>(startState.Index, 0.0)
            };
        }

        public void InputObservation(PitchHmmEmission observation)
        {
            var states = this._model.States;

            var maxLogProbabilities = new double[states.Count];
            for (var i = 0; i < maxLogProbabilities.Length; i++)
                maxLogProbabilities[i] = double.NegativeInfinity;

            // 初期状態確率 * 遷移確率 * 生成確率 を計算する
            foreach (var (fromStateIndex, stateLogProbability) in this._stateLogProbabilities)
            {
                foreach (var (toStateIndex, transitionLogProbability) in states[fromStateIndex].LogProbabilitiesByOutgoingStateIndexes)
                {
                    var lp = stateLogProbability + transitionLogProbability + states[toStateIndex].EmissionLogProbability(observation);

                    if (!(lp <= 0.0)) throw new Exception("確率が不正な値になりました。"); // NaN チェックも含めるため not <=

                    if (lp > maxLogProbabilities[toStateIndex])
                        maxLogProbabilities[toStateIndex] = lp;
                }
            }

            var stateLogProbabilities = this._stateLogProbabilities;
            stateLogProbabilities.Clear();

            var logSum = double.NegativeInfinity;
            var maxP = double.NegativeInfinity; // 最大の確率
            var argmaxP = 0; // 最大の確率のときの状態
            for (var stateIndex = 0; stateIndex < maxLogProbabilities.Length; stateIndex++)
            {
                var lp = maxLogProbabilities[stateIndex];
                if (double.IsNegativeInfinity(lp)) continue;

                logSum = Special.LogSum(logSum, lp);
                stateLogProbabilities.Add(new KeyValuePair<int, double>(stateIndex, lp));

                if (lp > maxP)
                {
                    maxP = lp;
                    argmaxP = stateIndex;
                }
            }

            if (stateLogProbabilities.Count == 0)
                throw new Exception("すべての状態の確率が 0 になりました。");

            // 確率が最も高い状態を現在のノートとして認識
            this.CurrentNote = states[argmaxP].Value.ReportingNote;

            // 次の計算のために確率を均しておく
            // 新しい確率 = 確率 / 確率の合計
            for (var i = 0; i < stateLogProbabilities.Count; i++)
            {
                var (stateIndex, lp) = stateLogProbabilities[i];
                stateLogProbabilities[i] = new KeyValuePair<int, double>(stateIndex, lp - logSum);
            }
        }
    }
}
