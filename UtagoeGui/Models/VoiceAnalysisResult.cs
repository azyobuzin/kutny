using System.Collections.Immutable;

namespace UtagoeGui.Models
{
    public class VoiceAnalysisResult
    {
        public ImmutableArray<NoteBlockModel> NoteBlocks { get; }
        public int UnitCount { get; }

        public VoiceAnalysisResult(ImmutableArray<NoteBlockModel> noteBlocks, int unitCount)
        {
            this.NoteBlocks = noteBlocks;
            this.UnitCount = unitCount;
        }
    }
}
