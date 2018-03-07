using System.ComponentModel;
using KeyEstimation;
using Kutny.WpfInfra;

namespace AutoHarmony.Models
{
    public interface IAppStore : INotifyPropertyChanged
    {
        Key? EstimatedKey { get; }
        float SignalPeak { get; }
        bool IsKeyEstimationRunning { get; }
        bool IsUpperHarmonyEnabled { get; }
        bool IsLowerHarmonyEnabled { get; }
    }

    public class AppStore : NotificationObject2, IAppStore
    {
        private Key? _estimatedKey;
        public Key? EstimatedKey
        {
            get => this._estimatedKey;
            set => this.Set(ref this._estimatedKey, value);
        }

        private float _signalPeak;
        public float SignalPeak
        {
            get => this._signalPeak;
            set => this.Set(ref this._signalPeak, value);
        }

        private bool _isKeyEstimationRunning;
        public bool IsKeyEstimationRunning
        {
            get => this._isKeyEstimationRunning;
            set => this.Set(ref this._isKeyEstimationRunning, value);
        }

        private bool _isUpperHarmonyEnabled;
        public bool IsUpperHarmonyEnabled
        {
            get => this._isUpperHarmonyEnabled;
            set => this.Set(ref this._isUpperHarmonyEnabled, value);
        }

        private bool _isLowerHarmonyEnabled;
        public bool IsLowerHarmonyEnabled
        {
            get => this._isLowerHarmonyEnabled;
            set => this.Set(ref this._isLowerHarmonyEnabled, value);
        }
    }
}
