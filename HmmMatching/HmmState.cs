using System;

namespace HmmMatching
{
    public sealed class HmmState<TState, TObservation>
    {
        public HiddenMarkovModel<TState, TObservation> Model { get; }
        public int Index { get; }
        public TState Value { get; }
        private readonly Func<TObservation, double> _emissionProbability;

        internal HmmState(HiddenMarkovModel<TState, TObservation> model, int index, TState value, Func<TObservation, double> emissionProbability)
        {
            this.Model = model;
            this.Index = index;
            this.Value = value;
            this._emissionProbability = emissionProbability;
        }

        public double EmissionProbability(TObservation emission) => this._emissionProbability(emission);

        public void AddOutgoingEdge(HmmState<TState, TObservation> to, double probability)
        {
            if (to == null) throw new ArgumentNullException(nameof(to));
            if (to.Model != this.Model) throw new ArgumentException();

            this.Model.SetTransitionProbability(this.Index, to.Index, probability);
        }

        public void AddIncommingEdge(HmmState<TState, TObservation> from, double probability)
        {
            if (from == null) throw new ArgumentNullException(nameof(from));
            if (from.Model != this.Model) throw new ArgumentException();

            this.Model.SetTransitionProbability(from.Index, this.Index, probability);
        }
    }
}
