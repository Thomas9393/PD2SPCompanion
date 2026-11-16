using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Views;

/// <summary>
/// The window's search box, in the top row (QuickSearchBar.axaml). The keys while the caret is in the box: Down
/// and Up mark a row of its list, Enter goes to the row marked - the first, until another is - Ctrl + Enter
/// shows the Search page with what is typed and the caret in its keyword field, Esc puts the list away and
/// empties the box.
/// </summary>
public partial class QuickSearchBar : UserControl
{
    public QuickSearchBar()
    {
        InitializeComponent();

        Box.AddHandler(KeyDownEvent, OnBoxKey, RoutingStrategies.Tunnel);

        DataContextChanged += (_, _) =>
        {
            if (DataContext is QuickSearchViewModel search)
            {
                search.FocusRequested += TakeFocus;
                search.Ended += LetGo;
            }
        };
    }

    /// <summary>
    /// A find gone to, Esc, a click elsewhere: the keyboard let go of, so the page's keys - Delete, the arrows, Esc,
    /// Ctrl + 1-6 - reach it at once (else they did nothing until a click, the caret left in the emptied
    /// box). As the Search page lets go (SearchWindow): to the main view, focused as a pointer focuses. After what
    /// takes the keyboard next - the Search page's keyword field, on Ctrl + Enter - had its turn, and only if the
    /// box still has it then (ItemPanels.LetKeysGo).
    /// </summary>
    private void LetGo() =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (TopLevel.GetTopLevel(this)?.GetVisualDescendants().OfType<MainView>().FirstOrDefault() is { } pages)
                PD2SPCompanion.Shell.CustomControls.Base.ItemPanels.LetKeysGo(Box, pages);
        }, Avalonia.Threading.DispatcherPriority.Background);

    /// <summary>Ctrl + F: the caret in the box, what stands in it selected.</summary>
    private void TakeFocus() =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Box.Focus();
            Box.SelectAll();
        }, Avalonia.Threading.DispatcherPriority.Loaded);

    private void OnBoxKey(object? sender, KeyEventArgs e)
    {
        if (DataContext is not QuickSearchViewModel search)
            return;

        switch (e.Key)
        {
            case Key.Enter when e.KeyModifiers == KeyModifiers.Control:
                search.ToListCommand.Execute(null);
                break;
            case Key.Enter when e.KeyModifiers == KeyModifiers.None:
                search.GoMarked();
                break;
            case Key.Down when e.KeyModifiers == KeyModifiers.None:
                search.MoveMark(+1);
                break;
            case Key.Up when e.KeyModifiers == KeyModifiers.None:
                search.MoveMark(-1);
                break;
            case Key.Escape:
                search.CloseCommand.Execute(null);
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
