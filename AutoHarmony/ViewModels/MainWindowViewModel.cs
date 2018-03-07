using AutoHarmony.Models;
using Kutny.WpfInfra;

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

        [ChangeCanExecuteWhenModelPropertyChange(nameof(IAppStore.IsKeyEstimationRunning))]
        public bool IsKeyEstimationRunning
        {
            get => this.Store.IsKeyEstimationRunning;
            set => this.Actions.EnableKeyEstimation(value);
        }

        [ChangeCanExecuteWhenModelPropertyChange(nameof(IAppStore.IsUpperHarmonyEnabled))]
        public bool IsUpperHarmonyEnabled
        {
            get => this.Store.IsUpperHarmonyEnabled;
            set => this.Actions.EnableUpperHarmony(value);
        }

        [ChangeCanExecuteWhenModelPropertyChange(nameof(IAppStore.IsLowerHarmonyEnabled))]
        public bool IsLowerHarmonyEnabled
        {
            get => this.Store.IsLowerHarmonyEnabled;
            set => this.Actions.EnableLowerHarmony(value);
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
