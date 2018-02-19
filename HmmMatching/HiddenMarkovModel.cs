using System;
using System.Collections.Generic;

namespace HmmMatching
{
    public class HiddenMarkovModel<TState, TObservation>
    {
        private readonly List<HmmState<TState, TObservation>> _states;
        private int _edgesCapacity;
        private double[,] _edges;

        public HiddenMarkovModel(int stateCapacity)
        {
            this._states = new List<HmmState<TState, TObservation>>(stateCapacity);
            this._edgesCapacity = this._states.Capacity;
            if (this._edgesCapacity > 0)
                this._edges = new double[stateCapacity, stateCapacity];
        }

        public HiddenMarkovModel() : this(0) { }

        public IReadOnlyList<HmmState<TState, TObservation>> States => this._states;

        public HmmState<TState, TObservation> AddState(TState value, Func<TObservation, double> emissionProbability)
        {
            if (emissionProbability == null) throw new ArgumentNullException(nameof(emissionProbability));

            var state = new HmmState<TState, TObservation>(this, this._states.Count, value, emissionProbability);
            this._states.Add(state);
            return state;
        }

        public void SetTransitionProbability(int from, int to, double probability)
        {
            if (!this.CheckIndex(from)) throw new ArgumentOutOfRangeException(nameof(from));
            if (!this.CheckIndex(to)) throw new ArgumentOutOfRangeException(nameof(to));
            if (probability < 0.0 || probability > 1.0) throw new ArgumentOutOfRangeException(nameof(probability));

            this.EnsureEdgesCapacity(Math.Max(from, to));

            this._edges[from, to] = probability;
        }

        public double GetTransitionProbability(int from, int to)
        {
            if (!this.CheckIndex(from)) throw new ArgumentOutOfRangeException(nameof(from));
            if (!this.CheckIndex(to)) throw new ArgumentOutOfRangeException(nameof(to));

            if (this._edgesCapacity <= Math.Max(from, to)) return 0.0;

            return this._edges[from, to];
        }

        private bool CheckIndex(int index) => index >= 0 && index < this._states.Count;

        private void EnsureEdgesCapacity(int index)
        {
            if (index >= this._edgesCapacity) this.ExpandEdges();
        }

        private void ExpandEdges()
        {
            var newCapacity = this._states.Capacity;
            var newEdges = new double[newCapacity, newCapacity];

            for (var i = 0; i < this._edgesCapacity; i++)
            {
                for (var j = 0; j < this._edgesCapacity; j++)
                    newEdges[i, j] = this._edges[i, j];
            }

            this._edgesCapacity = newCapacity;
            this._edges = newEdges;
        }

        public HmmState<TState, TObservation>[] ViterbiPath(IReadOnlyList<double> initialProbabilities, IReadOnlyList<TObservation> observations)
        {
            if (initialProbabilities == null) throw new ArgumentNullException(nameof(initialProbabilities));
            if (initialProbabilities.Count != this._states.Count) throw new ArgumentException(nameof(initialProbabilities) + " の要素数が状態数と一致しません。");
            if (observations == null) throw new ArgumentNullException(nameof(observations));

            // TODO: アクセスしなかったノードに関する情報は捨てたい
            // TODO: 確率計算を log にしたい（log 0 は -∞ になることに注意）

            var observationCount = observations.Count;
            var probabilities = new double[this._states.Count];
            var mlStates = new int[observationCount, this._states.Count];

            var firstObservation = observations[0];
            foreach (var state in this._states)
            {
                var index = state.Index;
                var initialProbability = initialProbabilities[index];
                if (initialProbability > 0.0)
                    probabilities[index] = initialProbability * this._states[index].EmissionProbability(firstObservation);
            }

            var newProbabilities = new double[this._states.Count];
            for (var i = 1; i < observationCount; i++)
            {
                var observation = observations[i];

                // ここら辺についての考察
                // このループは並列化可能
                // エッジを全パターン持っているとメモリがやばそうなので、 incomming だけ持っておけばいいかも（ついでに List いらなくなるし）
                foreach (var toState in this._states)
                {
                    var toStateIndex = toState.Index;

                    var maxP = 0.0;
                    var argmaxP = 0;

                    foreach (var fromState in this._states)
                    {
                        var fromStateIndex = fromState.Index;

                        var p = probabilities[fromStateIndex] * this.GetTransitionProbability(fromStateIndex, toStateIndex);
                        if (p <= 0.0) continue;
                        p *= toState.EmissionProbability(observation);

                        if (p > maxP)
                        {
                            maxP = p;
                            argmaxP = fromStateIndex;
                        }
                    }

                    newProbabilities[toStateIndex] = maxP;
                    mlStates[i, toStateIndex] = argmaxP;
                }

                // 配列を入れ替えて次に備える
                var tmp = probabilities;
                probabilities = newProbabilities;
                newProbabilities = probabilities;
            }

            var finalMaxP = 0.0;
            var finalArgmaxP = 0;
            foreach (var state in this._states)
            {
                var i = state.Index;
                if (probabilities[i] > finalMaxP)
                {
                    finalMaxP = probabilities[i];
                    finalArgmaxP = i;
                }
            }

            // 日本語 Wikipedia では最初からリスト作ってたけど、どっちがいいのか（Single Linked List がいい気がするが）
            var result = new HmmState<TState, TObservation>[observationCount];
            result[observationCount - 1] = this._states[finalArgmaxP];
            for (var i = observationCount - 2; i >= 0; i--)
                result[i] = this._states[mlStates[i + 1, result[i + 1].Index]];

            return result;
        }

        /// <summary>
        /// 遷移確率が合計で 1 になっているかを確認
        /// </summary>
        public void VerifyTransitionProbabilities()
        {
            for (var i = 0; i < this._states.Count; i++)
            {
                var total = 0.0;
                for (var j = 0; j < this._states.Count; j++)
                    total += this.GetTransitionProbability(i, j);

                if (total < 0.0 || (total > 0.0 && Math.Abs(1.0 - total) > 1e-5))
                    throw new Exception();
            }
        }
    }
}
