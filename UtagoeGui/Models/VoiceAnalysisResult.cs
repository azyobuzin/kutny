using System.Collections.Immutable;

namespace UtagoeGui.Models
{
    public class VoiceAnalysisResult
    {
        public ImmutableArray<NoteBlockInfo> NoteBlocks { get; }
        public int UnitCount { get; }

        public VoiceAnalysisResult(ImmutableArray<NoteBlockInfo> noteBlocks, int unitCount)
        {
            this.NoteBlocks = noteBlocks;
            this.UnitCount = unitCount;
        }
    }
}
