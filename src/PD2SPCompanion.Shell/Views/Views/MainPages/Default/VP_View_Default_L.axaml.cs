using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using PD2SPCompanion.Shell.CustomControls.Base;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Views;

public partial class VP_View_Default_L : UserControl, PD2SPCompanion.Shell.CustomControls.Base.IShownPage
{
    public VP_View_Default_L()
    {
        InitializeComponent();
    }

    /// <summary>The window the page listens on for Delete and F2, while it is in one.</summary>
    private TopLevel? _keys;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (_keys is null && TopLevel.GetTopLevel(this) is { } window)
        {
            _keys = window;
            _keys.AddHandler(KeyDownEvent, OnWindowKey, RoutingStrategies.Tunnel);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        OnHidden();
        _keys?.RemoveHandler(KeyDownEvent, OnWindowKey);
        _keys = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// Delete and F2 for the selected character wherever the focus was left on the page (they
    /// worked only sometimes). Heard on the list alone, they were lost whenever the list had not the keys: after a delete's
    /// question closed, after the name panel, once the card holding the focus was gone, after a click beside the
    /// cards. On the window instead, as the Items page hears its keys - while the list shows, not from a box of
    /// words, not under a question or a picker.
    /// </summary>
    private void OnWindowKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Delete or Key.F2)
            || DataContext is not VP_ViewModel_Default_L model || model.IsAsking
            || !Characters.IsEffectivelyVisible)
            return;

        if (ItemPanels.IsBoxOrPicker(_keys?.FocusManager?.GetFocusedElement() as Visual) || ItemPanels.IsVeiled(_keys))
            return;

        switch (e.Key)
        {
            case Key.F2 when model.CanRename && !model.GameBlocked:
                model.ToggleRenameCommand.Execute(null);
                break;
            case Key.Delete when model.CanDelete && !model.GameBlocked:
                model.DeleteCharacterCommand.Execute(null);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>In front (PageDeck), or shown the first time: the lists catch up with the save folder.</summary>
    public void OnShown() => (DataContext as VP_ViewModel_Default_L)?.Shown();

    public void OnHidden() => (DataContext as VP_ViewModel_Default_L)?.Hidden();

    /// <summary>
    /// Tells the page how wide the list is, so it can choose one column or two. A
    /// UniformGrid cannot decide that for itself.
    /// </summary>
    private void Characters_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is VP_ViewModel_Default_L model)
            model.SetViewportWidth(e.NewSize.Width);
    }

    /// <summary>
    /// A card's menu: by a right click on it - the list selects what the right button is
    /// pressed on - where the pointer is; by the Menu key or Shift + F10, of the card selected, beside it.
    /// </summary>
    private void Characters_Context(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not VP_ViewModel_Default_L model || model.IsAsking)
            return;

        var menus = PD2SPCompanion.Shell.Services.Generic.ActMenuService.Shared;
        if (e.TryGetPosition(Characters, out _))
        {
            if (e.Source is not Visual source || source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not { } card)
                return;

            Characters.SelectedItem = card.DataContext;
            menus.Show(model.MenuForSelected());
        }
        else
        {
            menus.Show(model.MenuForSelected(), Characters.ContainerFromIndex(model.SelectedIndex));
        }

        e.Handled = true;
    }

    /// <summary>
    /// Double-clicking a character opens it, which is the gesture the list invites.
    /// <para>
    /// Only when the click landed on a row: double-clicking the empty space below the last
    /// character should do nothing, not reopen whatever happens to be selected.
    /// </para>
    /// </summary>
    private void Characters_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<ListBoxItem>() is not null)
            LoadSelected();
    }

    /// <summary>Enter opens the highlighted character; the arrow keys already move between
    /// them, so this completes keyboard use of the list.</summary>
    /// <summary>
    /// The list's keys, Explorer's: Enter opens, F2 renames, Delete deletes -
    /// through the commands, so they obey the same guards as the card's buttons. F2 and Delete are heard on the
    /// window first (OnWindowKey); here they are what is left where it let one pass.
    /// </summary>
    private void Characters_KeyDown(object? sender, KeyEventArgs e)
    {
        // Not while a question is up: focus stays in the list behind it, and Enter after Delete opened the very
        // character the question was about.
        if (DataContext is not VP_ViewModel_Default_L model || model.IsAsking)
            return;

        switch (e.Key)
        {
            case Key.Enter or Key.Return:
                LoadSelected();
                break;
            case Key.F2 when model.CanRename && !model.GameBlocked:
                model.ToggleRenameCommand.Execute(null);
                break;
            case Key.Delete when model.CanDelete && !model.GameBlocked:
                model.DeleteCharacterCommand.Execute(null);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>
    /// The opened card's figure and the town under it, as large as the card's height lets them be: up to
    /// one and a half times the list cards' most, and no smaller than that most - a short window
    /// keeps what it had. The figure's row sizes itself to its content, so the room is worked out here: the
    /// card's height less the words under the figure.
    /// </summary>
    private void OpenedCard_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // A little air kept above the head and under the words: the figure's row would fill the card to its edge.
        const double Air = 24;
        double room = OpenedCard.Bounds.Height - Words.Bounds.Height - Stage.Margin.Bottom - Air;
        double most = Math.Clamp(room / PD2SPCompanion.Shell.CustomControls.Base.FigureImage.BodyRoom,
                                 PD2SPCompanion.Shell.CustomControls.Base.FigureImage.MostScale,
                                 PD2SPCompanion.Shell.CustomControls.Base.FigureImage.OpenedScale);
        foreach (var standing in Stage.Children)
        {
            if (standing is PD2SPCompanion.Shell.CustomControls.Base.FigureImage figure)
                figure.Most = most;
            else if (standing is PD2SPCompanion.Shell.CustomControls.Base.TownBackdrop town)
                town.Most = most;
        }
    }

    /// <summary>Goes through the command, so it obeys the same guard as the button.</summary>
    private void LoadSelected()
    {
        if (DataContext is VP_ViewModel_Default_L model && model.LoadCommand.CanExecute(null))
            model.LoadCommand.Execute(null);
    }
}
