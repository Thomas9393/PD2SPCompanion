using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// One skill in the picker, carrying its own command.
/// <para>
/// The alternative is for each entry to reach back up the tree for the row's command, and
/// the list lives in a flyout - a popup with a visual tree of its own - where reaching up
/// is not something to rely on. Holding the action is cheaper than debugging that.
/// </para>
/// </summary>
public sealed partial class SkillChoiceViewModel(
    SkillOption option, Action<SkillOption> pick, int? current = null)
{
    public SkillOption Option { get; } = option;

    /// <summary>Whether the row this list belongs to already points at this skill.</summary>
    public bool IsHeld { get; } = current is { } held && held == option.Id;

    /// <summary>
    /// The id leads, because it is what the file actually keeps and what anyone comparing
    /// against the tables will be looking for.
    /// </summary>
    public string Label => $"{Option.Id} - {Option.Display}";

    /// <summary>Whether this is worth showing for what has been typed.</summary>
    public bool Matches(string search) =>
        search.Length == 0
        || Option.Display.Contains(search, StringComparison.OrdinalIgnoreCase)
        || Option.Id.ToString().StartsWith(search, StringComparison.Ordinal);

    [RelayCommand]
    private void Pick() => pick(Option);
}

/// <summary>One section of the picker: a class, or everything with no class.</summary>
public sealed partial class SkillChoiceGroup(
    string name, IReadOnlyList<SkillChoiceViewModel> skills, bool isOpen = false)
    : ObservableObject
{
    public string Name { get; } = name;
    public IReadOnlyList<SkillChoiceViewModel> Skills { get; } = skills;

    /// <summary>
    /// Folded to begin with. Eight sections of a few dozen skills each is a long scroll
    /// before you have said anything about what you are looking for; folded, the whole
    /// list is eight lines and a class is one click away.
    /// </summary>
    [ObservableProperty] private bool _isOpen = isOpen;

    public string Header => $"{Name}  ({Skills.Count})";

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;

    /// <summary>Built from the game's own grouping, with the action threaded through.</summary>
    public static IReadOnlyList<SkillChoiceGroup> From(
        IReadOnlyList<SkillGroup> groups, Action<SkillOption> pick,
        bool isOpen = false, int? current = null) =>
        [.. groups.Select(group => new SkillChoiceGroup(
            group.Name,
            [.. group.Skills.Select(skill => new SkillChoiceViewModel(skill, pick, current))],
            isOpen))];
}
