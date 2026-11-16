using System;
using System.ComponentModel;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Views;

/// <summary>The item search's page: its conditions, and - once there are finds - its results (SearchWindow.axaml).</summary>
public partial class SearchWindow : UserControl
{
    public SearchWindow()
    {
        InitializeComponent();

        // Esc with the keyboard in the search is the search's (its class keeps the pages from taking it): a
        // picker of the conditions' own is closed by it.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Handled || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None)
                return;

            if (PD2SPCompanion.Shell.CustomControls.Base.ItemPanels.CloseTopVeil(this))
                e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Bubble);

        // A right click on a find, or the Menu key on one: its menu (FoundItem.Menu) - where the pointer is, or
        // beside the row that has the keyboard.
        AddHandler(ContextRequestedEvent, (_, e) =>
        {
            for (var at = e.Source as Visual; at is not null && !ReferenceEquals(at, this); at = at.GetVisualParent())
            {
                if (at is not Button { DataContext: FoundItem { Menu: { } menu, IsAsking: false } } row)
                    continue;

                PD2SPCompanion.Shell.Services.Generic.ActMenuService.Shared.Show(menu(), e.TryGetPosition(this, out Point _) ? null : row);
                e.Handled = true;
                return;
            }
        });

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not SearchWindowViewModel model || ReferenceEquals(model, _wired))
                return;

            _wired = model;
            model.KeywordWanted += () => Dispatcher.UIThread.Post(FocusKeyword, DispatcherPriority.Loaded);
            model.PropertyChanged += Changed;

            // A find gone to, or the finds selected or sent to a folder: what was asked for is on the left page
            // now, so the keyboard is let go of. The search keeps the keys while the keyboard is in it (its
            // class, ownkeys), and Esc or Delete after Select found would otherwise go nowhere.
            // To the main view, made focusable for it and focused as a pointer focuses (no outline): this
            // Avalonia's focus manager has no ClearFocus, and the window does not take the focus - focusing it
            // gave the keyboard back to the button it had come from (ItemPanels.TakeKeysBack met the same).
            void LetGo() => Dispatcher.UIThread.Post(() =>
            {
                if (this.FindAncestorOfType<MainView>() is { } pages)
                    PD2SPCompanion.Shell.CustomControls.Base.ItemPanels.LetKeysGo(this, pages);
            }, DispatcherPriority.Background);

            model.Finds.Picked += _ => LetGo();

            // The find F3 steps onto is kept in sight in the list, as it is on the left page.
            model.Finds.Marked += row =>
            {
                if (model.Finds.Rows.IndexOf(row) is >= 0 and var at)
                    Found.ScrollIntoView(at);
            };
            model.Results.SelectFoundRequested += LetGo;
            model.Results.MoveFoundRequested += LetGo;

            Lay();
        };
    }

    private SearchWindowViewModel? _wired;

    /// <summary>The keyword box's Down and Enter step into the finds, in the section under its own (ListKeys).</summary>
    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Dispatcher.UIThread.Post(() =>
        {
            if (Keyword() is { } keyword)
                PD2SPCompanion.Shell.CustomControls.Base.ListKeys.SetInto(keyword, Found);
        }, DispatcherPriority.Loaded);
    }

    private void Changed(object? sender, PropertyChangedEventArgs changed)
    {
        if (changed.PropertyName is not (nameof(SearchWindowViewModel.HasResults) or nameof(SearchWindowViewModel.ShowsConditions)
                                         or nameof(SearchWindowViewModel.ShowsResults)))
        {
            return;
        }

        Lay();

        // The form is shorter the moment the first find comes: the field being typed in stays in sight.
        Dispatcher.UIThread.Post(() =>
        {
            if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Control focused
                && focused.GetVisualAncestors().Contains(Form))
            {
                focused.BringIntoView();
            }
        }, DispatcherPriority.Loaded);
    }

    // ---- the page's height between the two: without a result, the conditions take up all the space;
    // with results, 40% conditions vs 60% results, and the player can decide which to fold. A grip between them
    // sized the two for a day
    // and was taken out as redundant; the share was taken out with it for an afternoon and put back. ----------

    /// <summary>The most of the page's height the conditions take beside results, their heading and the tools over them included.</summary>
    public const double MostShare = 0.40;

    /// <summary>The least the form has beside results, however short the page.</summary>
    private const double LeastForm = 96;

    /// <summary>The heading over the form and the section's padding: what the share holds besides the form.</summary>
    private const double AboutForm = 58;

    /// <summary>
    /// The rows as the search stands, one of the two having what the other leaves.
    /// No finds, or the results folded: the conditions' row has the page, the form scrolling in it - and
    /// folded themselves, they are their heading at the top. Finds unfolded: the conditions as tall as their
    /// fields, up to their share of the page - the form scrolling inside that - and the results the rest.
    /// </summary>
    private void Lay()
    {
        if (DataContext is not SearchWindowViewModel model)
            return;

        bool listed = model.HasResults && model.ShowsResults;

        // The keyword box's Down and Enter step into the finds (ListKeys): the box is built with the form, after the page.
        if (Keyword() is { } keyword)
            PD2SPCompanion.Shell.CustomControls.Base.ListKeys.SetInto(keyword, Found);
        var rows = Rows.RowDefinitions;
        rows[0].Height = listed ? GridLength.Auto : GridLength.Star;
        rows[2].Height = listed ? GridLength.Star : GridLength.Auto;

        Conditions.VerticalAlignment = listed || model.ShowsConditions ? VerticalAlignment.Stretch : VerticalAlignment.Top;
        Results.VerticalAlignment = listed ? VerticalAlignment.Stretch : VerticalAlignment.Top;
        Form.MaxHeight = listed ? Math.Max(LeastForm, Bounds.Height * MostShare - AboutForm) : double.PositiveInfinity;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Lay();
    }

    /// <summary>
    /// The caret in the conditions' keyword field, once it is laid out, and the field in sight. The first text
    /// box of the search's own form: the first of the whole panel is the presets' name box above it, where the
    /// caret would go while the presets are unfolded.
    /// </summary>
    private void FocusKeyword()
    {
        if (Keyword() is { } keyword)
        {
            keyword.Focus();
            keyword.SelectAll();
            keyword.BringIntoView();
        }
    }

    private TextBox? Keyword() =>
        Form.GetVisualDescendants().OfType<ItemSearchForm>().FirstOrDefault()?
            .GetVisualDescendants().OfType<TextBox>().FirstOrDefault(box => box.IsEffectivelyVisible && box.IsEffectivelyEnabled);
}
