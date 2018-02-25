using System;
using System.Collections.Generic;

namespace HmmMatching
{
    public class HmmState<TValue, TObservation>
    {
        private int _index;
        private readonly Func<TObservation, double> _emissionLogProbability;
        //private readonly Dictionary<int, double> _incomingEdges;
        private readonly Dictionary<int, double> _outgoingEdges;

        public HiddenMarkovModel<TValue, TObservation> Model { get; private set; }
        public TValue Value { get; }

        public HmmState(TValue value, Func<TObservation, double> emissionLogProbability)
        {
            this.Value = value;
            this._emissionLogProbability = emissionLogProbability;
        }

        public int Index => this.Model != null ? this._index : throw new InvalidOperationException();

        //public IReadOnlyDictionary<int, double> LogProbabilitiesByIncomingStateIndexes => this._incomingEdges;
        public IReadOnlyDictionary<int, double> LogProbabilitiesByOutgoingStateIndexes => this._outgoingEdges;

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
            from.AddOutgoingEdge(this.Index, logProbability);
        }

        public void AddIncomingEdge(int fromIndex, double logProbability)
        {
            this.Model.States[fromIndex].AddOutgoingEdge(this.Index, logProbability);
        }

        public void AddOutgoingEdge(HmmState<TValue, TObservation> to, double logProbability)
        {
            if (this.Model == null) throw new InvalidOperationException();
            if (to == null) throw new ArgumentNullException(nameof(to));
            if (to.Model != this.Model) throw new ArgumentException(nameof(to));
            if (logProbability <= 0.0) throw new ArgumentOutOfRangeException(nameof(logProbability));

            if (double.IsNegativeInfinity(logProbability))
                this._outgoingEdges.Remove(to.Index);
            else
                this._outgoingEdges[to.Index] = logProbability;
        }

        public void AddOutgoingEdge(int toIndex, double logProbability)
        {
            if (this.Model == null) throw new InvalidOperationException();
            if (toIndex < 0 || toIndex >= this.Model.States.Count) throw new ArgumentOutOfRangeException(nameof(toIndex));
            if (logProbability <= 0.0 && !double.IsNegativeInfinity(logProbability)) throw new ArgumentOutOfRangeException(nameof(logProbability));

            if (double.IsNegativeInfinity(logProbability))
                this._outgoingEdges.Remove(toIndex);
            else
                this._outgoingEdges[toIndex] = logProbability;
        }
    }
}
