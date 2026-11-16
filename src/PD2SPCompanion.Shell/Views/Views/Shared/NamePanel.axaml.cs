using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Views;

public partial class NamePanel : UserControl
{
    private VP_ViewModel_Default_L? _model;

    public NamePanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_model is not null)
                _model.PropertyChanged -= ModelChanged;
            _model = DataContext as VP_ViewModel_Default_L;
            if (_model is not null)
                _model.PropertyChanged += ModelChanged;
        };
    }

    /// <summary>Opened: the box takes the keys, its name selected, so typing replaces it.</summary>
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(VP_ViewModel_Default_L.IsRenaming) || _model?.IsRenaming != true)
            return;

        // After the veil is shown: a hidden box takes no focus.
        Dispatcher.UIThread.Post(() =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        }, DispatcherPriority.Loaded);
    }
}
