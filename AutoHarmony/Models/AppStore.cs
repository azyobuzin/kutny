using System.Collections.Immutable;
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
        ImmutableArray<RecoderProvider> RecoderProviders { get; }
        int SelectedRecoderProviderIndex { get; }
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

        private ImmutableArray<RecoderProvider> _recoderProviders = ImmutableArray<RecoderProvider>.Empty;
        public ImmutableArray<RecoderProvider> RecoderProviders
        {
            get => this._recoderProviders;
            set => this.Set(ref this._recoderProviders, value);
        }

        private int _selectedRecoderProviderIndex;
        public int SelectedRecoderProviderIndex
        {
            get => this._selectedRecoderProviderIndex;
            set => this.Set(ref this._selectedRecoderProviderIndex, value);
        }
    }
}
