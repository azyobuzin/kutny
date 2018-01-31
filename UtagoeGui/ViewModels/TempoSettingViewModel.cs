using UtagoeGui.Models;

namespace UtagoeGui.ViewModels
{
    public class TempoSettingViewModel
    {
        public TempoSettingViewModel(TempoSetting model)
        {
            this.Model = model;
        }

        public TempoSetting Model { get; }

        public int Measures => this.Model.Position / 1920;
        public int Ticks => this.Model.Position % 1920;
        public double Tempo => this.Model.Tempo;
    }
}
