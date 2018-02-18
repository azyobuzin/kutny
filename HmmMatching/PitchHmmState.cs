namespace HmmMatching
{
    public struct PitchHmmState
    {
        public UtauNote ReportingNote { get; }

        public PitchHmmState(UtauNote reportingNote)
        {
            this.ReportingNote = reportingNote;
        }
    }
}
