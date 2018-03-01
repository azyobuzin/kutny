namespace HmmMatching
{
    public struct PitchHmmGenerationResult
    {
        public HiddenMarkovModel<PitchHmmState, PitchHmmEmission> Model { get; }
        public HmmState<PitchHmmState, PitchHmmEmission> StartState { get; }

        public PitchHmmGenerationResult(HiddenMarkovModel<PitchHmmState, PitchHmmEmission> model, HmmState<PitchHmmState, PitchHmmEmission> startState)
        {
            this.Model = model;
            this.StartState = startState;
        }
    }
}
