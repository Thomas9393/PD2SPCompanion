using System;
using System.Collections.Generic;

using CommunityToolkit.Mvvm.ComponentModel;

using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Services.Generic;

/// <summary>
/// The one right-click menu: whatever was clicked - a folder, an item, a character's card -
/// its page says what can be done to it (<see cref="ActMenu"/>) and this shows it, where the pointer is or, asked
/// for by the Menu key, beside what has the keyboard. One menu for the window, drawn once (ActMenuHost), so every
/// page's looks and behaves the same.
/// </summary>
public sealed partial class ActMenuService : ObservableObject
{
    public static ActMenuService Shared { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcons))]
    private IReadOnlyList<MenuAct> _icons = [];

    [ObservableProperty] private IReadOnlyList<MenuAct> _rows = [];

    public bool HasIcons => Icons.Count > 0;

    /// <summary>The menu is up. The host's to say.</summary>
    public bool IsOpen { get; set; }

    /// <summary>
    /// Asked to open: beside the control given - the row the Menu key was pressed on - or, with none, where the
    /// pointer is; and at a place within the control, where what the menu is for is no control of its own - an
    /// item of a grid, which one layer draws. The host's.
    /// </summary>
    public event Action<object?, Avalonia.Rect?>? Asked;

    /// <summary>Asked to close. The host's.</summary>
    public event Action? Shut;

    /// <summary>Shows a menu. An empty one - nothing can be done to what was clicked - shows nothing, and closes the one up.</summary>
    public void Show(ActMenu menu, object? beside = null, Avalonia.Rect? at = null)
    {
        Shut?.Invoke();
        if (menu.IsEmpty)
            return;

        for (int index = 0; index < menu.Icons.Count; index++)
            menu.Icons[index].FollowsAnother = index > 0;

        Icons = menu.Icons;
        Rows = menu.Rows;
        Asked?.Invoke(beside, at);
    }

    public void Close() => Shut?.Invoke();

    /// <summary>
    /// A row or an icon picked: the menu closes, and then the act runs - once it has closed, as closing gives the
    /// keyboard back to where it was, and a name box an act opened before that lost it at once (the tools' list
    /// learned it). The host says when (<paramref name="later"/>); a test runs it at once.
    /// </summary>
    public void Pick(MenuAct act, Action<Action>? later = null)
    {
        Shut?.Invoke();
        if (!act.IsEnabled)
            return;

        if (later is null)
            act.Run();
        else
            later(act.Run);
    }
}
