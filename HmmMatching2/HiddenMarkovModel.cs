using System;
using System.Collections.Generic;
using Accord.Math;

namespace HmmMatching
{
    public class HiddenMarkovModel<TStateValue, TObservation>
    {
        private readonly List<HmmState<TStateValue, TObservation>> _states = new List<HmmState<TStateValue, TObservation>>();

        public IReadOnlyList<HmmState<TStateValue, TObservation>> States => this._states;

        public void AddState(HmmState<TStateValue, TObservation> state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            state.SetModel(this, this._states.Count);
            this._states.Add(state);
        }

        /// <summary>
        /// 各状態の遷移確率の和が 1 であることを確認します。
        /// </summary>
        public void VerifyEdges()
        {
            foreach (var fromState in this._states)
            {
                var logSum = double.NegativeInfinity;

                foreach (var toState in this._states)
                {
                    if (toState.LogProbabilitiesByIncomingStateIndexes.TryGetValue(fromState.Index, out var lp))
                        logSum = Special.LogSum(logSum, lp);
                }

                const double errorMargin = 0.01;
                if (double.IsNaN(logSum) || logSum < -errorMargin || logSum > errorMargin)
                    throw new Exception();
            }
        }
    }
}
