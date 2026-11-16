using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// A menu row's glyph, named as Glyphs.axaml keys it with the spaces out: <c>OpenFolder</c> is "[Glyph] Open folder".
/// Names, not strings: a word in quotes here would be taken for one the player reads (tests/words.py).
/// </summary>
public enum MenuGlyph
{
    Bin, Pencil, Star, Lock, Unlock, Compare, OpenFolder, NewFolder, MoveFolder, MoveToFolder, ToVault, FromVault,
    BattleGear, PersonPlus, SwapAcross, SidePane, TopPane, Duplicate, Paste, SelectAll, CloseGaps, Open, Archive, History,
    Revive, Cut, Tick, Plus, Minus, Download, Upload, Sort, ChevronsDown, ChevronsUp, Exit, Magnifier, ArrowDown, ArrowUp,
    Deposit,

    /// <summary>No glyph: a line of a list a row opens, which a tick marks where it is in force.</summary>
    None,
}

/// <summary>
/// One thing a right-click menu does (<see cref="ActMenu"/>): its words, its glyph, the keys that do the same
/// without the menu, and the act. A page's view model makes them for what was clicked; nothing here knows what.
/// </summary>
public sealed class MenuAct(string label, MenuGlyph glyph, Action run) : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    /// <summary>What it does, in the page's own words - a button's tooltip, where a button does the same.</summary>
    public string Label { get; } = label;

    public MenuGlyph Glyph { get; } = glyph;

    public Action Run { get; } = run;

    /// <summary>The keys that do it without the menu, as a tooltip writes them: "Ctrl + X". None: "".</summary>
    public string Keys { get; init; } = "";

    /// <summary>Blocked just now where false: dimmed, and <see cref="Why"/> says what stands in the way.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Why it is blocked, a short line under its words in its tooltip.</summary>
    public string Why { get; init; } = "";

    /// <summary>What a plain click or Enter does to the thing: said first, in bold.</summary>
    public bool IsMain { get; init; }

    /// <summary>A state that is on - the folder locked, pinned: ticked.</summary>
    public bool IsOn { get; init; }

    /// <summary>The first of a group: a rule above it.</summary>
    public bool RuleAbove { get; init; }

    /// <summary>It deletes: its glyph turns red under the pointer, as a bin's does.</summary>
    public bool IsBin { get; init; }

    /// <summary>
    /// The word it asks with before it does, where it asks - a bin's "Confirm": the first press turns it to
    /// that word, in red, and the menu stays; pressed again, it runs. In the menu itself (in the first version the
    /// menu's Delete armed the tools' bin and the menu went, the second confirm being on the tool row). None: "", and one press runs it.
    /// </summary>
    public string Confirm { get; init; } = "";

    public bool Asks => Confirm.Length > 0;

    private bool _isAsking;

    /// <summary>Pressed once, where it asks: it shows its question, and the next press is the answer yes.</summary>
    public bool IsAsking
    {
        get => _isAsking;
        set
        {
            if (SetProperty(ref _isAsking, value))
                OnPropertyChanged(nameof(Shown));
        }
    }

    /// <summary>The word under the icon: what it does, or its question while it asks.</summary>
    public string Shown => IsAsking ? Confirm : Label;

    /// <summary>
    /// An icon of the menu's head with another before it: a faint line stands between the two. The menu's to say
    /// as it is shown (ActMenuService) - the first has none (cut away by a frame round the icons at first, which a UI scale did not cut cleanly).
    /// </summary>
    public bool FollowsAnother { get; internal set; }

    /// <summary>
    /// A list the row opens beside the menu, where it does nothing itself: "Sort by" and the orders. One level,
    /// and one such row to a menu, as the guidelines have it.
    /// </summary>
    public IReadOnlyList<MenuAct>? More { get; init; }

    public bool HasMore => More is { Count: > 0 };

    public bool HasKeys => Keys.Length > 0;

    /// <summary>The same act as one row among others, not the menu's first: not bold, and under a rule where asked.</summary>
    public MenuAct Lesser(bool ruleAbove = false) => new(Label, Glyph, Run)
    {
        Keys = Keys, IsEnabled = IsEnabled, Why = Why, IsOn = IsOn, IsBin = IsBin, Confirm = Confirm, More = More, RuleAbove = ruleAbove || RuleAbove,
    };

    /// <summary>The glyph is a filled drawing, not strokes (Glyph.axaml's <c>pin art</c>): drawn so.</summary>
    public bool IsArt => Glyph is MenuGlyph.BattleGear;

    public FontWeight Weight => IsMain ? FontWeight.Bold : FontWeight.Normal;

    /// <summary>The glyph itself. Null with no program running - a test of the page's menus - or a name mistyped.</summary>
    public Geometry? Shape =>
        Application.Current is { } app && app.TryFindResource(KeyOf(Glyph), out object? found) ? found as Geometry : null;

    /// <summary>The glyph's key: its name, a space before each capital after the first and that capital small.</summary>
    public static string KeyOf(MenuGlyph glyph) =>
        "[Glyph] " + System.Text.RegularExpressions.Regex.Replace(glyph.ToString(), "(?<!^)[A-Z]", capital => " " + capital.Value.ToLowerInvariant());

    /// <summary>
    /// The tooltip. On the row of icons it is all that says what the icon does - its words, its keys in brackets
    /// (<see cref="ShortcutText"/>); a worded row needs one only to say why it is blocked.
    /// </summary>
    public string IconTip => (HasKeys ? ShortcutText.With(Label, Keys) : Label) + (Why.Length > 0 ? Environment.NewLine + Why : "");

    public string? RowTip => Why.Length > 0 ? Why : null;
}

/// <summary>
/// A right-click menu's contents, in Windows 11's style with the app's art style: a row of
/// icons at its head for what every such menu has - cut, copy, paste, rename, delete, as each applies - and under
/// it the worded rows, the main one first. What the play mode forbids is not in it; what is blocked just now is
/// dimmed and says why.
/// </summary>
public sealed record ActMenu(IReadOnlyList<MenuAct> Icons, IReadOnlyList<MenuAct> Rows)
{
    /// <summary>
    /// "Sort by", opening the orders an order's own button lists (Explorer,
    /// Finder and Dolphin all have it in the menu of empty room): the one in force ticked, a rule before the
    /// directions.
    /// </summary>
    public static MenuAct SortBy(MenuChoice order, string why = "") => new(L("Sort by"), MenuGlyph.Sort, () => { })
    {
        // Dimmed, with why, where no order can be taken just now: a locked page.
        IsEnabled = why.Length == 0,
        Why = why,
        More = [.. order.Options.Select(option => new MenuAct(option.Shown, MenuGlyph.None, () => order.Pick(option))
        {
            IsOn = option.IsCurrent,
            RuleAbove = option.StartsGroup,
        })],
    };

    /// <summary>A row's words with how many it takes, where it takes several: "Store in vault (3)".</summary>
    public static string Counted(string does, int count) => count > 1 ? $"{does} ({count})" : does;

    public bool IsEmpty => Icons.Count == 0 && Rows.Count == 0;
}
