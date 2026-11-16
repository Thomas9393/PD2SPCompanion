using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// One line of the automagic picker: a row of <c>automagic.txt</c> - or none - what it grants as ranges, the item
/// levels it spawns at, and whether it is the one the item carries.
/// </summary>
public sealed record AutomagicChoiceRow(AffixRanges.AutomagicRow? Row, IReadOnlyList<string> Lines, string Note, bool IsHeld,
    bool IsAboveLevel = false) : IPickRow;

/// <summary>
/// The item panel's automagic (the Forge made none; like superior, it expands on the Forge or Edit page):
/// which row of its base's <c>auto prefix</c> group the item carries - a class
/// javelin's skills, a staff's casting tier, a Paladin shield's resistances - and choosing another, or none.
/// <para>
/// The item keeps the row's id beside its properties; both are staged, the id on the draft until KEEP, the
/// properties through the rows (AffixRanges.PutAutomagic: the old row's share out, the new row's in at the top of
/// its ranges, the page's boxes to change after).
/// </para>
/// </summary>
public sealed partial class AutomagicEditor : ViewModelBase
{
    private readonly IItemPanel _panel;

    public AutomagicEditor(IItemPanel panel) => _panel = panel;


    /// <summary>Whether the item's base offers an automagic at all: what puts the line on the panel.</summary>
    public bool Shows => Stands(_panel);

    /// <summary>
    /// <see cref="Shows"/> asked of a panel: the Affixes section stands for the line too. Only where there is a
    /// choice - a group of several rows, the class items': 21 of 27 groups have one row, a
    /// melee weapon's splash, a staff's cast rate, and the line on every such item was noise. The Forge puts that
    /// one row on by itself (ItemForge.DressBase).
    /// </summary>
    public static bool Stands(IItemPanel panel) =>
        panel.Selected is { TabSlot: null } tile && !tile.Item.IsCompact && AffixRanges.AutomagicRowsFor(tile.Item).Count > 1;

    /// <summary>The item as the page has it: what is staged counts.</summary>
    private Item? Held => !Shows || _panel.Selected is not { } tile ? null : _panel.Pending() ?? tile.Item;

    private IReadOnlyList<AffixRanges.AutomagicRow> Rows => Held is { } item ? AffixRanges.AutomagicRowsFor(item) : [];

    /// <summary>The row carried, staged or stored; null with none.</summary>
    private AffixRanges.AutomagicRow? Carried =>
        Held is { IsAutoAffix: true, AutoAffixId: > 0 } item ? Rows.FirstOrDefault(row => row.Id == item.AutoAffixId) : null;

    private static IReadOnlyList<string> Words(AffixRanges.AutomagicRow row) =>
        Crafts.Ranges(new AffixRanges.CraftRecipe("", row.Grants));

    /// <summary>The item levels a row spawns at, said only where it is not every level.</summary>
    private static string Levels(AffixRanges.AutomagicRow row) =>
        row.Most > 0 && row.Most < 99 ? L($"item level {row.Least}-{row.Most}")
        : row.Least > 1 ? L($"item level {row.Least}+")
        : "";

    /// <summary>What the line says: one bonus a row, as the Affix line above it is - cut off at the field's width, a
    /// head's poison read "+1012 to Minimum Poison Damage, +1270 t...".</summary>
    public string Now => Carried is { } row ? string.Join("\n", Words(row)) : L("None");

    public IReadOnlyList<string> NowLines => Carried is { } row ? Words(row) : [];

    public bool HasNone => Shows && Carried is null;

    /// <summary>
    /// Whether there is a automod for the brush to take off. The brush at the top, as the corruption's and the
    /// runeword's: taking off was a "None" row at the head of the list.
    /// </summary>
    public bool CanTakeOff => Carried is not null;

    /// <summary>Every row of the base's group, the one carried marked, those its level does not reach marked too.</summary>
    public IReadOnlyList<AutomagicChoiceRow> Choices
    {
        get
        {
            if (Held is not { } item)
                return [];

            int level = item.ItemLevel;
            int? carried = Carried?.Id;

            // What every row says alike - the javelins' splash - is said by none of them: the rows read by what
            // tells them apart. The line on the item still says all of it.
            var rows = Rows;
            var shared = rows.Select(row => (IEnumerable<string>)Words(row)).Aggregate((all, next) => all.Intersect(next)).ToHashSet();
            IReadOnlyList<string> Telling(AffixRanges.AutomagicRow row) =>
                Words(row).Where(line => !shared.Contains(line)).ToList() is { Count: > 0 } own ? own : Words(row);

            return
            [
                .. rows.Select(row => new AutomagicChoiceRow(row, Telling(row), Levels(row), row.Id == carried,
                    level < row.Least || (row.Most > 0 && level > row.Most))),
            ];
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
        OnPropertyChanged(nameof(HasNone));
        OnPropertyChanged(nameof(CanTakeOff));

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

    /// <summary>
    /// The Flawless Skull: a row as the game draws one - among those the item level reaches, by frequency
    /// (AffixRanges.RollAutomagic) - never the one carried where there is another, so a press always changes it.
    /// </summary>
    [RelayCommand]
    private void Roll()
    {
        if (Held is not { } item)
            return;

        int? carried = Carried?.Id;
        var rows = Choices.Where(choice => choice.Row is { } row && row.Reaches(item.ItemLevel)).ToList();
        bool another = rows.Any(choice => choice.Row!.Id != carried);

        for (int tries = 0; tries < 64; tries++)
        {
            if (AffixRanges.RollAutomagic(item, System.Random.Shared) is not { } drawn || (another && drawn.Id == carried))
                continue;

            Pick(rows.FirstOrDefault(choice => choice.Row!.Id == drawn.Id));
            return;
        }
    }

    /// <summary>
    /// The Forge's Shift roll: a row as the game draws one, at its odds - the one
    /// carried may come again, as on a new drop. True where the row changed.
    /// </summary>
    public bool Draw(System.Random random)
    {
        if (Held is not { } item || AffixRanges.RollAutomagic(item, random) is not { } drawn || drawn.Id == Carried?.Id
            || Choices.FirstOrDefault(choice => choice.Row?.Id == drawn.Id) is not { } choice)
        {
            return false;
        }

        Pick(choice);
        return true;
    }

    /// <summary>The brush: the automod taken off.</summary>
    [RelayCommand]
    private void TakeOff()
    {
        if (CanTakeOff)
            Pick(new AutomagicChoiceRow(null, [], "", false));
    }

    /// <summary>One taken from the list and staged - the picker stays open, as a superior bonus's does.</summary>
    [RelayCommand]
    private void Pick(AutomagicChoiceRow? choice)
    {
        if (choice is null || _panel.Selected is not { } tile || _panel.Pending() is not { } shadow)
            return;

        _panel.Press();

        int id = choice.Row?.Id ?? 0;
        AffixRanges.PutAutomagic(shadow, id);

        int stored = tile.Item.IsAutoAffix ? tile.Item.AutoAffixId : 0;
        _panel.Draft.AutoAffix = id == stored ? null : id;
        _panel.StageStatsFrom(shadow, tile);

        // Open still, as the corruption's picker: the pick is marked, another takes its place; written when it closes.
        _panel.AffixesChanged(rebuild: false);
        _panel.Changed();
        Notify();

        _panel.Say(choice.Row is null
            ? L($"{tile.Name}: automod removed.")
            : L($"{tile.Name}: now {string.Join(", ", choice.Lines)}."));
    }
}
