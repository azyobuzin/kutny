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

        // TODO: エッジもここに持つ？
    }
}
