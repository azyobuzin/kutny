using System;
using System.Collections.Generic;

namespace HmmMatching
{
    public class HmmState<TValue, TObservation>
    {
        private int _index;
        private readonly Func<TObservation, double> _emissionLogProbability;
        private readonly Dictionary<int, double> _incomingEdges;

        public HiddenMarkovModel<TValue, TObservation> Model { get; private set; }
        public TValue Value { get; }

        public HmmState(TValue value, Func<TObservation, double> emissionLogProbability)
        {
            this.Value = value;
            this._emissionLogProbability = emissionLogProbability;
        }

        public int Index => this.Model != null ? this._index : throw new InvalidOperationException();

        public IReadOnlyDictionary<int, double> LogProbabilitiesByIncomingStateIndexes => this._incomingEdges;

        public double EmissionLogProbability(TObservation observation) => this._emissionLogProbability(observation);

        internal void SetModel(HiddenMarkovModel<TValue, TObservation> model, int index)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (this.Model != null) throw new InvalidOperationException();

            this.Model = model;
            this._index = index;
        }

        public void AddIncomingEdge(HmmState<TValue, TObservation> from, double logProbability)
        {
            if (this.Model == null) throw new InvalidOperationException();
            if (from == null) throw new ArgumentNullException(nameof(from));
            if (from.Model != this.Model) throw new ArgumentException(nameof(from));
            if (logProbability > 0.0) throw new ArgumentOutOfRangeException(nameof(logProbability));

            this._incomingEdges[from.Index] = logProbability;
        }

        public void AddIncomingEdge(int fromIndex, double logProbability)
        {
            if (this.Model == null) throw new InvalidOperationException();
            if (fromIndex < 0 || fromIndex >= this.Model.States.Count) throw new ArgumentOutOfRangeException(nameof(fromIndex));
            if (logProbability > 0.0) throw new ArgumentOutOfRangeException(nameof(logProbability));

            this._incomingEdges[fromIndex] = logProbability;
        }
    }
}
