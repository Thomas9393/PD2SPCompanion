using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// One line of the superior picker: a row of the superior table - or none - what it grants as ranges, and
/// whether it is the one the item carries. The same shape the craft, corruption and affix lists use.
/// </summary>
public sealed record SuperiorChoiceRow(QualityItems.Bonus? Bonus, IReadOnlyList<string> Lines, bool IsHeld) : IPickRow;

/// <summary>
/// The item panel's superior bonus: which
/// row of <c>qualityitems.txt</c> a superior item carries, and choosing another - or none. The game rolls
/// one row for an item, one bonus or two; here every row the base could have rolled is offered, and the
/// numbers it brings are the page's own boxes to change after.
/// <para>
/// Nothing of it is stored but the stats themselves, so the row is read off them and a pick is staged
/// through the rows, as a craft's change of family is - with the stored defence, which an Enhanced
/// Defense takes to the base's top plus one.
/// </para>
/// </summary>
public sealed partial class SuperiorEditor : ViewModelBase
{
    private readonly IItemPanel _panel;

    public SuperiorEditor(IItemPanel panel) => _panel = panel;


    /// <summary>
    /// Whether the item is superior - or a normal one made superior and not yet kept, or a runeword the
    /// Forge is making - on a base the table has a row for: what puts the line on the panel.
    /// </summary>
    public bool Shows => Stands(_panel);

    /// <summary><see cref="Shows"/> asked of a panel: the Affixes section stands for the line too.</summary>
    public static bool Stands(IItemPanel panel) =>
        panel.Selected is { TabSlot: null } tile
        && !tile.Item.IsCompact
        && (tile.Item.Quality is ItemQuality.Superior || panel.Draft.Quality is ItemQuality.Superior
            || ChoosesBase(panel, tile.Item))
        && QualityItems.RowsFor(tile.Item).Count > 0;

    /// <summary>
    /// A runeword the Forge is making (there was no way to make it on a superior base): its
    /// base is normal or superior as this line says - none, or a bonus - rather than by a box of the
    /// Forge's own. Superior is a trait of the base, set among the sections as Ethereal is; the Rarity box
    /// keeps saying what the item is. The Forge's page alone: Edit makes a base superior with its button.
    /// </summary>
    private static bool ChoosesBase(IItemPanel panel, Item item) =>
        !panel.Transforms && item.IsRuneword && item.Quality is ItemQuality.Normal or ItemQuality.Superior;

    /// <summary>
    /// Whether the item can be made superior: a normal one - a runeword's base too - whose base the table
    /// has a row for. Not where the page chose what the item is (the Forge: its Rarity box).
    /// </summary>
    public bool CanMake =>
        _panel.Transforms
        && _panel.Selected is { TabSlot: null } tile
        && !tile.Item.IsCompact
        && tile.Item.Quality is ItemQuality.Normal
        && _panel.Draft.Quality is null
        && QualityItems.RowsFor(tile.Item).Count > 0;

    /// <summary>
    /// Makes a normal item superior, staged as any change: the quality alone, and the picker opened for the
    /// bonus that goes with it - left at none, it is a superior item with no bonus, which the table never
    /// rolls but the file holds.
    /// </summary>
    [RelayCommand]
    private void Make()
    {
        if (!CanMake || _panel.Selected is not { } tile)
            return;

        _panel.Press();
        _panel.Draft.Quality = ItemQuality.Superior;

        // Built again, not only told: the section this item had no use for shows from here.
        _panel.AffixesChanged(rebuild: true);
        _panel.Changed();
        Notify();
        _panel.Say(L($"{tile.Name}: superior."));

        IsPickerOpen = true;
    }

    /// <summary>The item as the page has it: what is staged counts, as it does everywhere on the panel.</summary>
    private Item? Held => !Shows || _panel.Selected is not { } tile ? null : _panel.Pending() ?? tile.Item;

    /// <summary>The row carried, staged or stored; null with no bonus, or with a set no row grants.</summary>
    private QualityItems.Bonus? Carried => Held is { } item ? QualityItems.RowOn(item) : null;

    private static IReadOnlyList<string> Words(QualityItems.Bonus bonus) =>
        Crafts.Ranges(new AffixRanges.CraftRecipe(
            "", [.. bonus.Mods.Select(mod => new AffixRanges.CraftGrant(mod.Code, mod.Min, mod.Max))]));

    /// <summary>What the line says: the row's bonuses, or that there is none.</summary>
    public string Now => Carried is { } bonus ? string.Join("\n", Words(bonus)) : L("None");

    /// <summary>The same in the picker's head, a line a bonus.</summary>
    public IReadOnlyList<string> NowLines => Carried is { } bonus ? Words(bonus) : [];

    public bool HasNone => Shows && Carried is null;

    /// <summary>
    /// Whether there is a bonus for the brush to take off. The brush at the top, as the corruption's and the
    /// runeword's: taking off was a "None" row at the head of the list.
    /// </summary>
    public bool CanTakeOff => Carried is not null;

    /// <summary>Every row the base could have rolled, the one carried marked.</summary>
    public IReadOnlyList<SuperiorChoiceRow> Choices
    {
        get
        {
            if (Held is not { } item)
                return [];

            int? carried = QualityItems.RowOn(item)?.Row;
            return [.. QualityItems.RowsFor(item).Select(bonus => new SuperiorChoiceRow(bonus, Words(bonus), bonus.Row == carried))];
        }
    }

    [ObservableProperty] private bool _isPickerOpen;

    /// <summary>Kept apart from openness so the veil can fade out - see <see cref="Presence"/>.</summary>
    [ObservableProperty] private bool _isPickerPresent;

    partial void OnIsPickerOpenChanged(bool value) =>
        Presence.Keep(value, () => IsPickerOpen, on => IsPickerPresent = on, VP_ViewModel_Inv_R.VeilFade);

    partial void OnIsPickerPresentChanged(bool value)
    {
        if (!value)
            return;
        OnPropertyChanged(nameof(Choices));
    }

    /// <summary>Everything the panel shows of the bonus, told again.</summary>
    public void Notify()
    {
        OnPropertyChanged(nameof(Shows));
        OnPropertyChanged(nameof(CanMake));
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

    /// <summary>The picker belongs to the item that was showing when it opened; a rebuild closes it.</summary>
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
    /// The Flawless Skull: a bonus at random among the rows the base can have, each alike - never the one carried
    /// where there is another, so a press always changes it.
    /// </summary>
    [RelayCommand]
    private void Roll()
    {
        var rows = Choices.ToList();
        if (rows.Count == 0)
            return;

        var others = rows.Count > 1 ? rows.Where(row => !row.IsHeld).ToList() : rows;
        Pick(others[Random.Shared.Next(others.Count)]);
    }

    /// <summary>A superior item, whose bonus the Forge's Shift roll draws again.</summary>
    public bool CanDraw => Held is { } item && (_panel.Draft.Quality ?? item.Quality) is ItemQuality.Superior;

    /// <summary>
    /// The Forge's Shift roll: on a superior item, a row as the game draws one -
    /// each alike, no table giving weights; the one carried may come again. A normal item stays normal. True where
    /// the row changed.
    /// </summary>
    public bool Draw(Random random)
    {
        if (!CanDraw || Choices is not { Count: > 0 } rows)
            return false;

        var row = rows[random.Next(rows.Count)];
        if (row.IsHeld)
            return false;

        Pick(row);
        return true;
    }

    /// <summary>The brush: the bonus taken off - on a Forge runeword, a normal base again.</summary>
    [RelayCommand]
    private void TakeOff()
    {
        if (CanTakeOff)
            Pick(new SuperiorChoiceRow(null, [], false));
    }

    /// <summary>
    /// Takes one from the list and stages it - the picker stays open, as every picker's does: the row's
    /// stats in place of the old row's, each at the top of its range, and the defence an Enhanced Defense
    /// brings. Worked out on the item as the panel holds it, so nothing already pending is lost.
    /// </summary>
    [RelayCommand]
    private void Pick(SuperiorChoiceRow? row)
    {
        if (row is null || _panel.Selected is not { } tile || _panel.Pending() is not { } shadow)
            return;

        _panel.Press();

        int defense = shadow.Armor;

        // A full item stays full: 340 of 396 real superior items with a Durability bonus hold their current
        // durability at the maximum it raises (the trade and armory pages), the rest worn.
        bool wasFull = shadow.MaxDurability > 0
                       && shadow.Durability >= Math.Min(ItemStats.MaxDurabilityOf(shadow, shadow.MaxDurability), 255);

        // On a Forge runeword the pick says what the base is: a bonus makes it superior, none normal again.
        // A normal base holds no more than its top defence, which an Enhanced Defense had taken past.
        bool choosesBase = ChoosesBase(_panel, tile.Item);
        if (choosesBase)
        {
            var wanted = row.Bonus is null ? ItemQuality.Normal : ItemQuality.Superior;
            _panel.Draft.Quality = wanted == tile.Item.Quality ? null : wanted;
            shadow.Quality = wanted;
        }

        QualityItems.Put(shadow, row.Bonus);

        if (choosesBase && row.Bonus is null && ItemBases.Of(shadow.Code) is { MaximumDefense: { } top })
        {
            int most = shadow.IsEthereal ? ItemBases.Ethereal(top) : top;
            if (shadow.Armor > most)
                shadow.Armor = (ushort)most;
        }

        _panel.StageStatsFrom(shadow, tile);
        if (shadow.Armor != defense)
            _panel.StageDefense(shadow.Armor);

        if (wasFull)
            _panel.StageDurability(Math.Min(ItemStats.MaxDurabilityOf(shadow, shadow.MaxDurability), 255));

        // Open still, as the corruption's picker: the pick is marked, another takes its place; written when it closes.

        _panel.AffixesChanged(rebuild: false);
        _panel.Changed();
        Notify();

        string name = tile.Name;
        if (row.Bonus is null)
        {
            _panel.Say(choosesBase ? L($"{name}: normal base.") : L($"{name}: superior bonus removed."));
            return;
        }

        string bonus = string.Join(", ", row.Lines);
        _panel.Say(L($"{name}: now {bonus}."));
    }
}
