using System.Collections.Generic;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using PD2SPCompanion.Shell.Services.Generic;

namespace PD2SPCompanion.Shell.Services.Settings;

/// <summary>One group of the sheet: what the keys under it are for, and the keys.</summary>
public sealed record KeyGroup(string Title, IReadOnlyList<TipHint> Keys);

/// <summary>
/// The sheet of keys and gestures (they lived only in tooltips - a
/// right click's five meanings, Ctrl and Shift on a stepper, the drags; the right click's five are rows of a
/// menu now). One page of them, over everything as
/// What's new is: opened with F1 from anywhere, and from Settings' About card.
/// <para>
/// Written here, not gathered from the tooltips: a tooltip says what its own control does, in its own words
/// and often by a count ("To vault (3)"); the sheet says the rule. <b>When a gesture changes, this changes
/// with it</b> - the tooltip that names it is the other place.
/// </para>
/// </summary>
public sealed partial class KeySheetService : ObservableObject
{
    /// <summary>The one sheet: the window shows it, a key and Settings' About card open it.</summary>
    public static KeySheetService Shared { get; } = new();

    [ObservableProperty] private bool _isOpen;

    /// <summary>
    /// In the window while it is open and for the time its fade out takes after (Presence.Keep), as every picker
    /// is: it faded in and was gone at once.
    /// </summary>
    [ObservableProperty] private bool _isPresent;

    partial void OnIsOpenChanged(bool value) =>
        PD2SPCompanion.Shell.ViewModels.Presence.Keep(value, () => IsOpen, on => IsPresent = on);

    public void Show() => IsOpen = true;

    [RelayCommand]
    private void Close() => IsOpen = false;

    /// <summary>F1: shown, or put away again.</summary>
    public void Toggle() => IsOpen = !IsOpen;

    private static TipHint Key(string keys, string does) => new(keys, does);

    /// <summary>The sheet's left column: getting about, and what is done to items.</summary>
    public IReadOnlyList<KeyGroup> Left { get; } =
    [
        new(L("Getting about"),
        [
            Key(L("Ctrl + 1 - 6"), L("Side bar pages")),
            Key(L("Ctrl + Tab"), L("Next bookmark of the right page; Shift for the one before")),
            Key(L("Ctrl + PageUp / PageDown"), L("Inventory or vault on the left page")),
            Key(L("Ctrl + LMB"), L("On a page in the inventory's list: show it in pane II")),
            Key(L("Enter / F2 / Del"), L("On the home page: open, rename, delete the selected character")),
            Key(L("Ctrl + F"), L("Search")),
            Key(L("Ctrl + Enter"), L("In the search box: to the Search page")),
            Key(L("F3"), L("On the Search page: next result; Shift for the one before")),
            Key(L("Ctrl + Z / Y"), L("Undo, redo")),
            Key(L("Esc"), L("Close the panel on top; let the selection go")),
            Key(L("F1"), L("This sheet")),
        ]),
        new(L("Selecting items"),
        [
            Key(L("LMB"), L("Select one")),
            Key(L("Ctrl + LMB"), L("Add to the selection, or take out of it")),
            Key(L("Shift + LMB"), L("Select the run from the last one")),
            Key(L("Shift + Drag"), L("Select an area; Ctrl + Drag adds one")),
            Key(L("Shift + Wheel"), L("In the vault: scroll sideways; Ctrl + Wheel a page at a time")),
            Key(L("Ctrl + A"), L("Select all - in a search, what it found")),
            Key(L("Arrows"), L("Move the selection to the next item that way")),
        ]),
        new(L("Moving items"),
        [
            Key(L("Drag"), L("Carry an item or the selection: to a cell, a pane, the vault, a folder, the Compendium page")),
            Key(L("Drag a Compendium card"), L("Take its item out of compendium, onto a cell")),
            Key(L("Drag onto an item"), L("Socket a gem, a rune or a jewel")),
            Key(L("Shift, letting go"), L("A carried group into any free cells, its shape not kept")),
            Key(L("Shift + Drag"), L("Out of the materials tab: a full stack")),
            Key(L("RMB"), L("Menu of the item, folder or page under the pointer; put back what is carried")),
            Key(L("Menu key / Shift + F10"), L("Menu of what is selected")),
            Key(L("Del"), L("Delete the selection")),
            Key(L("Ctrl + X"), L("Cut the selection: it moves when pasted; Esc lets it go")),
            Key(L("Ctrl + C"), L("Copy the selection, and as text to paste in the Forge")),
            Key(L("Ctrl + V"), L("Paste: on the page shown, or into the vault's folder")),
            Key(L("G"), L("Store the selection in compendium; on the Compendium page, the chosen card's item in or out")),
            Key(L("R"), L("Deposit materials: every material on the container pages into the materials tab")),
        ]),
    ];

    /// <summary>The right column: looking at an item, the vault's folders, numbers and steppers, lists.</summary>
    public IReadOnlyList<KeyGroup> Right { get; } =
    [
        new(L("Looking at an item"),
        [
            Key(L("Tab"), L("Compare with what is equipped, while over an item")),
            Key(L("Alt"), L("In the comparison: the other slot that takes it")),
            Key(L("Ctrl"), L("In the comparison: the mercenary's gear")),
            Key(L("[ / ]"), L("Scroll a tooltip cut to the screen")),
            Key(L("Shift"), L("Socket details, on or off")),
            Key(L("X"), L("Show sockets through the items")),
            Key(L("Ctrl + R"), L("Reroll the stats of the item being edited; on the Forge, roll it as a new drop")),
            Key(L("Ctrl + Shift + R"), L("On the Forge: what kind of item it is too - ethereal, affixes, automod, staffmods, superior, sockets")),
            Key(L("Alt + Enter"), L("In a property's box: the same number on its group (Enhanced Damage, all resistances)")),
        ]),
        new(L("Vault folders"),
        [
            Key(L("Backspace / Alt + Left"), L("Back")),
            Key(L("Alt + Right"), L("Forward")),
            Key(L("Alt + Up"), L("Up one folder")),
            Key(L("Enter / Double click"), L("Open the selected folder")),
            Key(L("F2"), L("Rename the selected folder")),
        ]),
        new(L("Numbers"),
        [
            Key(L("Up / Down"), L("One more or less, in a number box")),
            Key(L("PageUp / PageDown"), L("Ten more or less")),
            Key(L("Shift + LMB"), L("A stepper to its end: least, most, all, none")),
            Key(L("Ctrl + LMB"), L("A stepper by a larger step")),
        ]),
        new(L("Character points"),
        [
            Key(L("Ctrl + LMB"), L("5 points")),
            Key(L("Shift + LMB"), L("20 points")),
            Key(L("Ctrl + Shift + LMB"), L("Every point: a stat, a skill, the level, points to spend")),
        ]),
        new(L("Waypoints"),
        [
            Key(L("Ctrl + LMB"), L("Grant, Remove: every act of the difficulty")),
            Key(L("Shift + LMB"), L("Grant, Remove: every difficulty")),
        ]),
        new(L("Storing"),
        [
            Key(L("Ctrl + LMB"), L("Store sorted into folders")),
            Key(L("Shift + LMB"), L("Store crafting bases only")),
        ]),
        new(L("Lists and questions"),
        [
            Key(L("Up / Down"), L("Walk a list's rows")),
            Key(L("Enter"), L("Pick the row; answer yes")),
            Key(L("Esc"), L("Close; answer no")),
        ]),
    ];
}
