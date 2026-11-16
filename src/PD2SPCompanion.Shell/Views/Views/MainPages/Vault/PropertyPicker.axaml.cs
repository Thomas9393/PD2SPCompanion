using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Views;

/// <summary>The search form's property picker - see the markup.</summary>
public partial class PropertyPicker : UserControl
{
    private PropertyPickerViewModel? _model;

    public PropertyPicker()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_model is not null)
                _model.PropertyChanged -= ModelChanged;
            _model = DataContext as PropertyPickerViewModel;
            if (_model is not null)
                _model.PropertyChanged += ModelChanged;
        };
    }

    /// <summary>
    /// Opened: the search box takes the keys, so a name is typed at once (as NamePanel's box does) -
    /// and what is typed is the box's, not the page's behind it.
    /// </summary>
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PropertyPickerViewModel.IsOpen) || _model?.IsOpen != true)
            return;

        // After the veil is shown: a hidden box takes no focus.
        Dispatcher.UIThread.Post(() => SearchBox.Focus(), DispatcherPriority.Loaded);
    }
}
