using System.Windows;
using Livet.EventListeners.WeakEvents;
using UtagoeGui.Infrastructures;
using UtagoeGui.Models;

namespace UtagoeGui.ViewModels
{
    public class CorrectNoteBlockViewModel : ViewModel2
    {
        private readonly CorrectNoteBlockModel _model;
        private readonly IAppStore _appStore;

        public CorrectNoteBlockViewModel(CorrectNoteBlockModel model, IAppStore appStore)
        {
            this._model = model;
            this._appStore = appStore;

            this.CompositeDisposable.Add(new PropertyChangedWeakEventListener(
                model,
                (_, e) =>
                {
                    switch (e.PropertyName)
                    {
                        case nameof(CorrectNoteBlockModel.StartPositionInAnalysisUnits):
                            this.RaisePropertyChanged(nameof(this.Margin));
                            break;
                        case nameof(CorrectNoteBlockModel.LengthInAnalysisUnits):
                            this.RaisePropertyChanged(nameof(this.Width));
                            break;
                    }
                }
            ));

            this.CompositeDisposable.Add(new PropertyChangedWeakEventListener(
                appStore,
                (_, e) =>
                {
                    switch (e.PropertyName)
                    {
                        case nameof(IAppStore.ScoreScale):
                            this.RaisePropertyChanged(nameof(this.Margin));
                            this.RaisePropertyChanged(nameof(this.Width));
                            break;
                    }
                }
            ));
        }

        public string Text => this._model.Lyric;

        public int RowIndex => Logics.MaximumNoteNumber - this._model.NoteNumber;

        public Thickness Margin => new Thickness(this._model.StartPositionInAnalysisUnits * MainWindowViewModel.UnitWidth * this._appStore.ScoreScale, 0, 0, 0);

        public double Width => this._model.LengthInAnalysisUnits * MainWindowViewModel.UnitWidth * this._appStore.ScoreScale;
    }
}
