namespace UtagoeGui.Models
{
    public class TempoSetting
    {
        public double Tempo { get; }
        public int Position { get; }

        public TempoSetting(double tempo, int position)
        {
            this.Tempo = tempo;
            this.Position = position;
        }
    }
}
