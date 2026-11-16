using System;
using System.Collections.Generic;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// Select all, select none, invert: the menu that comes with multi-selection, opened from the same
/// menu button the sort uses. Actions rather than states, so no line carries a bullet.
/// <para>
/// The vault page's to begin with; the inventory page gives each of its containers one too, so
/// it lives here. The three actions are the owner's, since only it knows what is shown.
/// </para>
/// </summary>
public sealed class PickMenu : MenuChoice
{
    private readonly Action _all, _none, _invert;

    public PickMenu(Action all, Action none, Action invert)
    {
        _all = all;
        _none = none;
        _invert = invert;

        Options =
        [
            new SortOption(N("Select all"), isDirection: false, descending: false, startsGroup: false),
            new SortOption(N("Select none"), isDirection: false, descending: false, startsGroup: false),
            new SortOption(N("Invert selection"), isDirection: false, descending: false, startsGroup: false),
        ];
    }

    public override IReadOnlyList<SortOption> Options { get; }

    /// <summary>
    /// The menu's name, and under it the two drags nobody finds by themselves (on a page
    /// packed tight, every press is on an item and a plain drag carries it):
    /// with Shift held a drag draws the rectangle from anywhere, an item too, and with Ctrl it adds what it
    /// covers to the picks (DragPick). Drawn as key caps (KeyedTip).
    /// </summary>
    public override string Hint => L("Selection") + System.Environment.NewLine + Drags;

    /// <summary>The two drags that draw a rectangle from anywhere, a line each: the menu's tooltip, and the Edit page's notices.</summary>
    public static string Drags =>
        L("Shift + Drag: select area") + System.Environment.NewLine
        + L("Ctrl + Drag: add area");

    public override void Pick(SortOption option)
    {
        switch (option.Label)
        {
            case "Select all": _all(); break;
            case "Select none": _none(); break;
            case "Invert selection": _invert(); break;
        }
    }
}
