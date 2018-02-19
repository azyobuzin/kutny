namespace HmmMatching
{
    public struct PitchHmmState
    {
        public string Label { get; }
        public UtauNote ReportingNote { get; }

        public PitchHmmState(string label, UtauNote reportingNote)
        {
            this.Label = label;
            this.ReportingNote = reportingNote;
        }
    }
}
