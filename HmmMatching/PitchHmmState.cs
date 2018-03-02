using Kutny.Common;

namespace HmmMatching
{
    public class PitchHmmState
    {
        public UtauNote ReportingNote { get; }
        public bool IsSilentState { get; }
        public bool IsStartState => this.ReportingNote == null;

        protected PitchHmmState(UtauNote reportingNote, bool isSilentState)
        {
            this.ReportingNote = reportingNote;
            this.IsSilentState = isSilentState;
        }

        public override string ToString()
        {
            return this.IsStartState
                ? "スタート"
                : string.Format(
                    "{0} {1} {2}",
                    this.ReportingNote.Index,
                    this.IsSilentState ? "無" : CommonUtils.ToNoteName(this.ReportingNote.NoteNumber % 12),
                    this.ReportingNote.Lyric
                );
        }

        public static PitchHmmState CreateStartState()
        {
            return new PitchHmmState(null, true);
        }

        public static PitchHmmState CreateEmittingSoundState(UtauNote reportingNote)
        {
            return new PitchHmmState(reportingNote, false);
        }

        public static PitchHmmState CreateNoSoundState(UtauNote reportingNote)
        {
            return new PitchHmmState(reportingNote, true);
        }
    }
}
