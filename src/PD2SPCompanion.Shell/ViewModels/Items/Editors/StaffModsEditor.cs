using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// One skill of the staff mods picker: the skill, the item level it needs, whether the item carries it - and whether
/// the item carries its most already, so this one cannot be added: greyed, and said.
/// </summary>
public sealed record StaffModChoiceRow(SkillOption Skill, string Note, bool IsHeld, bool IsAboveLevel, bool IsFull = false) : IPickRow
{
    public string Name => Skill.Name;

    /// <summary>A click takes it off, or puts it on where there is room.</summary>
    public bool CanPick => IsHeld || !IsFull;

    /// <summary>What the row says at its right: the most reached, else the item level it needs.</summary>
    public string Said => !IsHeld && IsFull ? L($"max {StaffMods.MostPerItem}") : Note;
}

/// <summary>One staff mod the item carries, as the picker's top says it, with its own brush to take it off.</summary>
public sealed record StaffModHeldRow(int Skill, string Line);

/// <summary>
/// The item panel's staff mods (the Forge made none, as with superior): the class
/// skills a Wand, an Orb, a Katar and their kin roll on their own, up to three (StaffMods). The picker lists the
/// class's skills - a click puts one on at +3 or takes it off - and rolls a set as the game does, or empties it.
/// The levels are the page's boxes to change after, as every bonus's numbers are.
/// </summary>
public sealed partial class StaffModsEditor : ViewModelBase
{
    private readonly IItemPanel _panel;

    public StaffModsEditor(IItemPanel panel) => _panel = panel;


    public bool Shows => Stands(_panel);

    /// <summary>Whether the item's base rolls staff mods: what puts the line on the panel, and the Affixes section with it.</summary>
    public static bool Stands(IItemPanel panel) =>
        panel.Selected is { TabSlot: null } tile && !tile.Item.IsCompact && StaffMods.Rolls(tile.Item);

    private Item? Held => !Shows || _panel.Selected is not { } tile ? null : _panel.Pending() ?? tile.Item;

    private IReadOnlyList<StaffMods.Mod> Carried => Held is { } item ? StaffMods.On(item) : [];

    private static string Line(StaffMods.Mod mod) =>
        L($"+{mod.Levels} to {SkillNames.NameOf(mod.Skill) ?? mod.Skill.ToString()}");

    public string Now => Carried is { Count: > 0 } mods ? string.Join("\n", mods.Select(Line)) : L("None");

    public IReadOnlyList<string> NowLines => [.. Carried.Select(Line)];

    /// <summary>The staff mods the item carries, each to be taken off by its own brush.</summary>
    public IReadOnlyList<StaffModHeldRow> HeldRows => [.. Carried.Select(mod => new StaffModHeldRow(mod.Skill, Line(mod)))];

    /// <summary>
    /// The most an item carries, said beside the title - "max 3" - as the Sockets section says its own. A ceiling, not a
    /// count to fill: the game rolls none to three, not always three, and "1/3" would imply always three.
    /// </summary>
    public string Limits => L($"max {StaffMods.MostPerItem}");
    public bool HasNone => Shows && Carried.Count == 0;

    /// <summary>The class's skills, those the item carries marked, those its level does not reach marked too.</summary>
    public IReadOnlyList<StaffModChoiceRow> Choices
    {
        get
        {
            if (Held is not { } item)
                return [];

            var held = Carried.Select(mod => mod.Skill).ToHashSet();
            bool full = held.Count >= StaffMods.MostPerItem;
            return [.. StaffMods.Offered(item).Select(skill =>
            {
                int needs = StaffMods.LowestItemLevelFor(skill);
                return new StaffModChoiceRow(skill, needs > 1 ? L($"item level {needs}") : "", held.Contains(skill.Id), item.ItemLevel < needs, full);
            })];
        }
    }

    [ObservableProperty] private bool _isPickerOpen;

    [ObservableProperty] private bool _isPickerPresent;

    partial void OnIsPickerOpenChanged(bool value) =>
        Presence.Keep(value, () => IsPickerOpen, on => IsPickerPresent = on, VP_ViewModel_Inv_R.VeilFade);

    partial void OnIsPickerPresentChanged(bool value)
    {
        if (!value)
            return;
        OnPropertyChanged(nameof(Choices));
    }

    public void Notify()
    {
        OnPropertyChanged(nameof(Shows));
        OnPropertyChanged(nameof(Now));
        OnPropertyChanged(nameof(NowLines));
        OnPropertyChanged(nameof(HeldRows));
        OnPropertyChanged(nameof(HasNone));

        // The list only while the picker is there: shut, nobody reads it, and each notification worked it out for
        // nobody - and its rows asked again whether they could be picked.
        if (IsPickerPresent)
        {
            OnPropertyChanged(nameof(Choices));
        }
    }

    public void Reset() => IsPickerOpen = false;

    [RelayCommand]
    private void OpenPicker()
    {
        if (!Shows)
            return;

        _panel.Press();
        Notify();
        IsPickerOpen = true;
    }

    [RelayCommand]
    private void ClosePicker() => IsPickerOpen = false;

    /// <summary>A skill clicked: off where the item carries it, on at +3 where it does not - three at most.</summary>
    [RelayCommand]
    private void Toggle(StaffModChoiceRow? choice)
    {
        if (choice is null)
            return;

        var mods = Carried.ToList();
        if (mods.FirstOrDefault(mod => mod.Skill == choice.Skill.Id) is { } held)
            mods.Remove(held);
        else if (mods.Count >= StaffMods.MostPerItem)
            return;
        else
            mods.Add(new StaffMods.Mod(choice.Skill.Id, StaffMods.Levels.Max));

        Stage(mods);
    }

    /// <summary>One staff mod taken off by the brush beside it, the others kept.</summary>
    [RelayCommand]
    private void TakeOff(StaffModHeldRow? row)
    {
        if (row is not null)
            Stage([.. Carried.Where(mod => mod.Skill != row.Skill)]);
    }

    /// <summary>A set as the game rolls one (StaffMods.Roll): none to three, each +1 to +3, of skills the level reaches.</summary>
    [RelayCommand]
    private void Roll()
    {
        if (Held is { } item)
            Stage(StaffMods.Roll(item, Random.Shared));
    }

    [RelayCommand]
    private void Clear() => Stage([]);

    /// <summary>Whether the item carries a staff mod: levels for the Forge's plain roll.</summary>
    public bool HasMods => Carried.Count > 0;

    /// <summary>The Forge's Shift roll: a set as the game rolls one, which may be none (StaffMods.Roll). True where it differs.</summary>
    public bool Draw(Random random)
    {
        if (Held is not { } item)
            return false;

        var drawn = StaffMods.Roll(item, random);
        if (drawn.SequenceEqual(Carried))
            return false;

        Stage(drawn);
        return true;
    }

    /// <summary>
    /// The Forge's plain roll: each staff mod's levels again, the skills kept - the numbers, as the roll's other
    /// numbers are. True where one moved.
    /// </summary>
    public bool RollLevels(Random random)
    {
        if (Carried is not { Count: > 0 } mods)
            return false;

        var rolled = mods.Select(mod => mod with { Levels = random.Next(StaffMods.Levels.Min, StaffMods.Levels.Max + 1) }).ToList();
        if (rolled.SequenceEqual(mods))
            return false;

        Stage(rolled);
        return true;
    }

    /// <summary>The staff mods made these, staged through the rows; the picker stays open for the next.</summary>
    private void Stage(IReadOnlyList<StaffMods.Mod> mods)
    {
        if (_panel.Selected is not { } tile || _panel.Pending() is not { } shadow)
            return;

        _panel.Press();
        StaffMods.Put(shadow, mods);
        _panel.StageStatsFrom(shadow, tile);

        _panel.AffixesChanged(rebuild: false);
        _panel.Changed();
        Notify();
        _panel.Say(L($"{tile.Name}: staffmods {Now}."));
    }
}
