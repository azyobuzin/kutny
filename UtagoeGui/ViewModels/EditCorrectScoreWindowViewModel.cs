using System;
using System.Collections.Generic;
using Kutny.WpfInfra;
using Livet;
using Livet.Commands;
using UtagoeGui.Models;

namespace UtagoeGui.ViewModels
{
    public class EditCorrectScoreWindowViewModel : ViewModel2
    {
        public IAppStore Store { get; }
        public IAppActions Actions { get; }

        public EditCorrectScoreWindowViewModel(IAppStore store, IAppActions actions)
        {
            this.Store = store;
            this.Actions = actions;

            this.EnableAutoPropertyChangedEvent(store);
            this.EnableAutoCanExecuteChangedEvent(store);
            this.EnableValidation();

            this._startPosition = store.CorrectScoreStartPositionInAnalysisUnits.ToString();

            this.TempoSettings = ViewModelHelper.CreateReadOnlyDispatcherCollection(
                store.TempoSettings, x => new TempoSettingViewModel(x), DispatcherHelper.UIDispatcher);
        }

        #region プロパティ

        [ChangeValueWhenModelPropertyChange(nameof(IAppStore.CorrectScoreFileName))]
        public string FileName => this.Store.CorrectScoreFileName ?? "読み込まれていません";

        private string _startPosition;
        [ValidationMethod(nameof(ValidateStartPosition))]
        public string StartPosition
        {
            get => this._startPosition;
            set => this.Set(ref this._startPosition, value, x =>
            {
                if (this.ValidateStartPosition() == null)
                    this.Actions.MoveCorrectScoreStartPosition(double.Parse(x));
            });
        }

        private static readonly string[] s_startPositionValidationErrorMessage = { "実数を入力してください。" };
        private IEnumerable<string> ValidateStartPosition() => double.TryParse(this.StartPosition, out _) ? null : s_startPositionValidationErrorMessage;

        private string _measures = "0";
        [ValidationMethod(nameof(ValidateMeasures))]
        public string Measures
        {
            get => this._measures;
            set => this.Set(ref this._measures, value);
        }

        private static readonly string[] s_measuresValidationErrorMessage = { "0 以上の整数を入力してください。" };
        private IEnumerable<string> ValidateMeasures()
        {
            return int.TryParse(this.Measures, out var parsed)
                && parsed >= 0
                ? null : s_measuresValidationErrorMessage;
        }

        private string _ticks = "0";
        [ValidationMethod(nameof(ValidateTicks))]
        public string Ticks
        {
            get => this._ticks;
            set => this.Set(ref this._ticks, value);
        }

        private static readonly string[] s_ticksValidationErrorMessage = { "0 以上 1920 未満の範囲で入力してください。" };
        private IEnumerable<string> ValidateTicks()
        {
            return int.TryParse(this.Ticks, out var parsed)
                && parsed >= 0 && parsed < 1920
                ? null : s_ticksValidationErrorMessage;
        }

        private string _tempo = "120";
        [ValidationMethod(nameof(ValidateTempo))]
        public string Tempo
        {
            get => this._tempo;
            set => this.Set(ref this._tempo, value);
        }

        private static readonly string[] s_tempoValidationErrorMessage = { "0 より大きいの実数を入力してください。" };
        private IEnumerable<string> ValidateTempo()
        {
            return double.TryParse(this.Tempo, out var parsed)
                && parsed > 0
                ? null : s_tempoValidationErrorMessage;
        }

        public ReadOnlyDispatcherCollection<TempoSettingViewModel> TempoSettings { get; }

        private int _selectedTempoSettingIndex = -1;
        public int SelectedTempoSettingIndex
        {
            get => this._selectedTempoSettingIndex;
            set => this.Set(ref this._selectedTempoSettingIndex, value);
        }

        [ChangeValueWhenModelPropertyChange(nameof(IAppStore.PitchConcordanceRate))]
        public double PitchConcordanceRate => this.Store.PitchConcordanceRate;

        [ChangeValueWhenModelPropertyChange(nameof(IAppStore.VowelConcordanceRate))]
        public double VowelConcordanceRate => this.Store.VowelConcordanceRate;

        [ChangeValueWhenModelPropertyChange(nameof(IAppStore.VowelConcordanceRate2))]
        public double VowelConcordanceRate2 => this.Store.VowelConcordanceRate2;

        #endregion

        #region イベント

        public event EventHandler CorrectScoreFileSelectionRequested;

        #endregion

        #region コマンド

        private ViewModelCommand _openCorrectScoreFileCommand;
        [ChangeCanExecuteWhenModelPropertyChange(nameof(IAppStore.VoiceAnalysisResult))]
        public ViewModelCommand OpenCorrectScoreFileCommand => CreateCommand(
            ref this._openCorrectScoreFileCommand,
            () => RunOnUIThread(() => this.CorrectScoreFileSelectionRequested?.Invoke(this, EventArgs.Empty)),
            () => this.Store.VoiceAnalysisResult != null
        );

        private ViewModelCommand _closeCorrectScoreCommand;
        [ChangeCanExecuteWhenModelPropertyChange(nameof(IAppStore.CorrectScoreFileName))]
        public ViewModelCommand CloseCorrectScoreCommand => CreateCommand(
            ref this._closeCorrectScoreCommand,
            () => this.Actions.CloseCorrectScore(),
            () => this.Store.CorrectScoreFileName != null
        );

        private ViewModelCommand _addCommand;
        [ChangeCanExecuteWhenThisPropertyChange(nameof(Measures), nameof(Ticks), nameof(Tempo))]
        [ChangeCanExecuteWhenModelPropertyChange(nameof(IAppStore.VoiceAnalysisResult))]
        public ViewModelCommand AddCommand => CreateCommand(
            ref this._addCommand,
            () => this.Actions.AddTempoSetting(
                double.Parse(this.Tempo),
                int.Parse(this.Measures) * 1920 + int.Parse(this.Ticks)
            ),
            () => this.Store.VoiceAnalysisResult != null && this.ValidateMeasures() == null && this.ValidateTicks() == null && this.ValidateTempo() == null
        );

        private ViewModelCommand _removeCommand;
        [ChangeCanExecuteWhenThisPropertyChange(nameof(SelectedTempoSettingIndex))]
        [ChangeCanExecuteWhenModelPropertyChange(nameof(IAppStore.VoiceAnalysisResult))]
        public ViewModelCommand RemoveCommand => CreateCommand(
            ref this._removeCommand,
            () => this.Actions.RemoveTempoSetting(this.SelectedTempoSettingIndex),
            () => this.Store.VoiceAnalysisResult != null && this.SelectedTempoSettingIndex >= 0
        );

        #endregion

        #region View からの応答

        public void SelectedCorrectScoreFile(string fileName)
        {
            this.Actions.OpenCorrectScore(fileName);
        }

        public void Closed()
        {
            this.Actions.CloseCorrectScoreWindow();
        }

        #endregion
    }
}
