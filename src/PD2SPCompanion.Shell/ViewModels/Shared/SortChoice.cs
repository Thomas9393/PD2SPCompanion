using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PD2SPCompanion.Shell.Services.Settings;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// The order a list is shown in: which mode, which direction, remembered between runs.
/// <para>
/// A panel owns one of these, gives it the modes it understands and a key of its own, and
/// listens to <see cref="Changed"/> to rebuild its list. What each mode <i>means</i> stays with
/// the panel - only the panel knows whether "Added" is a timestamp or a row number - so this
/// carries the choice, the persistence and the direction rule, and nothing else.
/// </para>
/// <para>
/// Pair it with the <c>SortMenu</c> control - an icon button opening a menu like Explorer's Sort,
/// already wired to one of these. It replaced <c>SortBar</c>, a drop box beside a direction
/// button.
/// </para>
/// </summary>
public sealed partial class SortChoice : MenuChoice
{
    private readonly EditorSettings _settings;
    private readonly string _key;

    /// <param name="key">
    /// Names the list, not the page - two lists on one page keep their orders apart. It is
    /// written into the settings file, so changing it forgets the old preference.
    /// </param>
    /// <param name="modes">What the list can be ordered by, in the order they are offered.</param>
    public SortChoice(EditorSettings settings, string key, params string[] modes)
    {
        _settings = settings;
        _key = key;
        Modes = modes;

        var stored = settings.SortFor(key);

        // The backing fields, not the properties: assigning a property here would save the
        // settings back out during construction, so one stored order would be written again on
        // every start. It would also raise Changed before the owner had subscribed.
        _by = Modes.Contains(stored.By) ? stored.By : Modes.FirstOrDefault() ?? "";
        _descending = stored.Descending;

        Options =
        [
            .. Modes.Select(mode => new SortOption(mode, isDirection: false, descending: false, startsGroup: false)),
            new SortOption(N("Ascending"), isDirection: true, descending: false, startsGroup: true),
            new SortOption(N("Descending"), isDirection: true, descending: true, startsGroup: false),
        ];

        MarkCurrent();
    }

    /// <summary>
    /// What <c>SortMenu</c> lists, as Explorer's Sort menu does: the modes, then the two
    /// directions, a bullet beside the one of each that is in force.
    /// </summary>
    public override IReadOnlyList<SortOption> Options { get; }

    /// <summary>The sort button's tooltip: what it is, then the order in force.</summary>
    public override string Hint => L("Sort") + Environment.NewLine + (Blocked?.Invoke() ?? Face);

    /// <summary>
    /// Why no order can be taken just now, or null - a locked page. The owner dims the button for it and the
    /// tooltip says it in place of the order in force; <see cref="SayBlocked"/> where it changes.
    /// </summary>
    public Func<string?>? Blocked { get; set; }

    /// <summary>What <see cref="Blocked"/> answers may be another: the tooltip is read again.</summary>
    public void SayBlocked() => OnPropertyChanged(nameof(Hint));

    /// <summary>The order in force, in a few characters: <c>Added ↑</c>.</summary>
    public string Face => $"{L(By)} {(Descending ? "↓" : "↑")}";

    /// <summary>
    /// One pick from the menu. The mode already in force, picked again, turns the order the
    /// other way - Explorer's rule, and what makes a separate direction button unnecessary.
    /// </summary>
    public override void Pick(SortOption option)
    {
        if (Allowed?.Invoke() == false)
            return;

        if (option.IsDirection)
            Descending = option.Descending;
        else if (option.Label == By)
            Descending = !Descending;
        else
            By = option.Label;

        Picked?.Invoke();
    }

    /// <summary>
    /// Asked before an order or a direction is taken, where sorting is an action that can be refused - a
    /// locked folder, a locked page. False, which the owner says why: the order kept and the menu's bullet
    /// stay where they were, on the order the list was last laid out in.
    /// </summary>
    public Func<bool>? Allowed { get; set; }

    /// <summary>
    /// An order or a direction was chosen from the menu, whether or not it differs from the one there was:
    /// where sorting is an action - the vault lays a folder out again - the same order asked for again is
    /// asked for again.
    /// </summary>
    public event Action? Picked;

    private void MarkCurrent()
    {
        foreach (var option in Options)
            option.IsCurrent = option.IsDirection ? option.Descending == Descending : option.Label == By;

        OnPropertyChanged(nameof(Face));
        OnPropertyChanged(nameof(Hint));
    }

    public IReadOnlyList<string> Modes { get; }

    /// <summary>Raised after either half of the choice moves, so the owner can rebuild.</summary>
    public event Action? Changed;

    [ObservableProperty] private string _by;

    [ObservableProperty] private bool _descending;

    partial void OnByChanged(string value) => Remember();

    partial void OnDescendingChanged(bool value) => Remember();

    private void Remember()
    {
        MarkCurrent();
        _settings.RememberSort(_key, By, Descending);
        Changed?.Invoke();
    }

    /// <summary>
    /// The sequence the way the list should read.
    /// <para>
    /// Descending <b>reverses</b> what the caller ordered rather than ordering the other way,
    /// and the difference matters: a second sort would place equal items differently, so
    /// flipping twice would not come back to where it started. Reversed, the list is exactly
    /// itself read backwards.
    /// </para>
    /// </summary>
    public IEnumerable<T> Arrange<T>(IOrderedEnumerable<T> ordered) =>
        Descending ? ordered.Reverse() : ordered;
}

/// <summary>One line of <c>SortMenu</c>: a mode to order by, or a direction.</summary>
public sealed partial class SortOption : ObservableObject
{
    public SortOption(string label, bool isDirection, bool descending, bool startsGroup)
    {
        Label = label;
        IsDirection = isDirection;
        Descending = descending;
        StartsGroup = startsGroup;
    }

    public string Label { get; }

    /// <summary>The label as shown: in the language in use (Label stays English - it is stored and compared).</summary>
    public string Shown => L(Label);

    public bool IsDirection { get; }

    /// <summary>For a direction, which one it is.</summary>
    public bool Descending { get; }

    /// <summary>The first of the directions, which a rule sets apart from the modes above it.</summary>
    public bool StartsGroup { get; }

    public Avalonia.Thickness Rule => StartsGroup ? new(0, 1, 0, 0) : default;

    public Avalonia.Thickness Room => StartsGroup ? new(0, 6, 0, 0) : default;

    [ObservableProperty] private bool _isCurrent;
}
