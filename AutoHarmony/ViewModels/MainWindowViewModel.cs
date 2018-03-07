using System.Collections.Immutable;
using AutoHarmony.Models;
using Kutny.WpfInfra;
using Livet.EventListeners.WeakEvents;

namespace AutoHarmony.ViewModels
{
    public class MainWindowViewModel : ViewModel2
    {
        public IAppStore Store { get; }
        public IAppActions Actions { get; }

        public MainWindowViewModel()
        {
            var model = new AppModel();
            this.Store = model.Store;
            this.Actions = model;

            this.EnableAutoPropertyChangedEvent(this.Store);

            this.CompositeDisposable.Add(new PropertyChangedWeakEventListener(
                this.Store,
                (_, e) =>
                {
                    switch (e.PropertyName)
                    {
                        case nameof(IAppStore.RecoderProviders):
                            this.RecoderProviders = ImmutableArray.CreateRange(this.Store.RecoderProviders, x => x.DisplayName);
                            break;
                    }
                }
            ));
        }

        #region プロパティ

        [ChangeValueWhenModelPropertyChange(nameof(IAppStore.EstimatedKey))]
        public string EstimatedKey
        {
            get
            {
                var key = this.Store.EstimatedKey;
                return key.HasValue ? key.Value.ToString() : "--";
            }
        }

        [ChangeValueWhenModelPropertyChange(nameof(IAppStore.SignalPeak))]
        public float SignalPeak => this.Store.SignalPeak;

        [ChangeValueWhenModelPropertyChange(nameof(IAppStore.IsKeyEstimationRunning))]
        public bool IsKeyEstimationRunning
        {
            get => this.Store.IsKeyEstimationRunning;
            set => this.Actions.EnableKeyEstimation(value);
        }

        [ChangeValueWhenModelPropertyChange(nameof(IAppStore.IsUpperHarmonyEnabled))]
        public bool IsUpperHarmonyEnabled
        {
            get => this.Store.IsUpperHarmonyEnabled;
            set => this.Actions.EnableUpperHarmony(value);
        }

        [ChangeValueWhenModelPropertyChange(nameof(IAppStore.IsLowerHarmonyEnabled))]
        public bool IsLowerHarmonyEnabled
        {
            get => this.Store.IsLowerHarmonyEnabled;
            set => this.Actions.EnableLowerHarmony(value);
        }

        private ImmutableArray<string> _recoderProviders = ImmutableArray<string>.Empty;
        public ImmutableArray<string> RecoderProviders
        {
            get => this._recoderProviders;
            private set => this.Set(ref this._recoderProviders, value);
        }

        [ChangeValueWhenModelPropertyChange(nameof(IAppStore.SelectedRecoderProviderIndex))]
        public int SelectedRecoderProviderIndex
        {
            get => this.Store.SelectedRecoderProviderIndex;
            set => this.Actions.SelectRecoderProvider(value);
        }

        #endregion

        #region View からの応答

        public void Initialize() => this.Actions.Initialize();

        public void Exit() => this.Actions.Exit();

        public void ToggleKeyEstimation()
        {
            this.Actions.EnableKeyEstimation(!this.Store.IsKeyEstimationRunning);
        }

        public void ToggleUpperHarmony()
        {
            this.Actions.EnableUpperHarmony(!this.Store.IsUpperHarmonyEnabled);
        }

        public void ToggleLowerHarmony()
        {
            this.Actions.EnableLowerHarmony(!this.Store.IsLowerHarmonyEnabled);
        }

        #endregion
    }
}
