using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// The item panel's crafting: which recipe a crafted item was made by, changing it, and making a
/// craft of a magic or rare item in place. What it settles is staged into the panel's
/// <see cref="ItemDraft"/>; the properties themselves go through the rows, as any edit's do.
/// </summary>
public sealed partial class CraftingEditor : ViewModelBase
{
    private readonly IItemPanel _panel;

    public CraftingEditor(IItemPanel panel) => _panel = panel;

    private ItemDraft Draft => _panel.Draft;

    /// <summary>
    /// Whether this item was crafted, which is what puts the crafting line on the panel
    /// and turns its name orange.
    /// </summary>
    public bool IsCraft =>
        _panel.Selected is { } tile
        && !tile.Item.IsCompact
        && tile.Item.Quality is ItemQuality.Craft;

    /// <summary>
    /// The craft family the item belongs to - Vampiric, Blood, Caster - or "Unknown" where
    /// its properties no longer name one.
    /// <para>
    /// No recipe id is stored, so the family is read back off what the item carries: each
    /// recipe grants a fixed set of properties and each set is unique within an item type.
    /// An item whose properties have been edited since it was crafted therefore stops
    /// naming a family, and saying "Unknown" is the honest answer rather than guessing at
    /// the nearest one.
    /// </para>
    /// </summary>
    public string Family =>
        IsCraft
            ? Draft.CraftFamily
              ?? (_panel.Selected is { } tile
                  ? AffixRanges.CraftFamilyOf(tile.Item) ?? L("Unknown")
                  : "")
            : "";

    /// <summary>The recipe the item will be made by, staged or stored.</summary>
    private AffixRanges.CraftRecipe? Now =>
        _panel.Selected is not { } tile
            ? null
            : Draft.CraftFamily is { Length: > 0 } staged
                ? AffixRanges.CraftFamilies(tile.Item).FirstOrDefault(recipe =>
                    string.Equals(recipe.Family, staged, StringComparison.OrdinalIgnoreCase))
                : AffixRanges.CraftOn(tile.Item);

    [ObservableProperty] private bool _isPickerOpen;

    /// <summary>Kept apart from openness so the veil can fade out - see <see cref="Presence"/>.</summary>
    [ObservableProperty] private bool _isPickerPresent;

    partial void OnIsPickerOpenChanged(bool value) =>
        Presence.Keep(value, () => IsPickerOpen, on => IsPickerPresent = on, VP_ViewModel_Inv_R.VeilFade);

    /// <summary>What the item is made by now, and what that grants, as ranges.</summary>
    public IReadOnlyList<string> NowLines =>
        Now is { } made ? Crafts.Ranges(made) : [];

    /// <summary>
    /// True where the item names no family. Its properties no longer match any recipe, so
    /// there is nothing to show and nothing certain to take off.
    /// </summary>
    public bool IsUnknown => IsCraft && Now is null;

    /// <summary>
    /// Every family this base can be made into, with the one chosen marked. Read off the
    /// item as the panel holds it, so a family chosen and not yet confirmed still shows the
    /// item's own as the current one.
    /// </summary>
    public IReadOnlyList<CraftChoiceRow> Choices =>
        _panel.Selected is { } tile
            ? [.. AffixRanges.CraftFamilies(tile.Item)
                .Select(recipe => new CraftChoiceRow(
                    recipe,
                    string.Equals(recipe.Family, Chosen?.Family,
                                  StringComparison.OrdinalIgnoreCase)))]
            : [];

    /// <summary>
    /// The family picked and its numbers rolled, on its way to the item: Confirm stages it at
    /// once, and the page holds it until KEEP, as a corruption is held.
    /// </summary>
    [ObservableProperty]
    private Crafts.CraftDraft? _chosen;

    /// <summary>
    /// Whether this item could be put through a craft recipe: a magic or rare one whose
    /// base some recipe takes.
    /// <para>
    /// A corrupted item is <b>not</b> ruled out. The craft recipes carry only five
    /// conditions between them - <c>mag</c>, <c>rar</c>, <c>eth</c>, <c>noe</c> and
    /// <c>qty</c> - and no recipe anywhere in <c>cubemain</c> tests for corruption. The
    /// wiki says the same: a corrupted item cannot be corrupted twice, and is otherwise
    /// eligible for the recipes. What comes out is a new item that does not carry the
    /// corruption, which is what crafting does to everything the base had.
    /// </para>
    /// </summary>
    public bool CanCraftFrom =>
        _panel.Transforms
        && Draft.CraftFrom is null
        && _panel.Selected is { } tile
        && !tile.Item.IsCompact
        && tile.Item.Quality is ItemQuality.Magic or ItemQuality.Rare
        && AffixRanges.CraftFamilies(tile.Item).Count > 0;

    /// <summary>
    /// True while the picker is being used to turn a plain item into a crafted one, rather
    /// than to change which recipe a crafted one was made by. The two differ in what
    /// confirming does, and in nothing else.
    /// </summary>
    [ObservableProperty] private bool _isCraftingFrom;

    partial void OnIsPickerPresentChanged(bool value)
    {
        if (!value)
            return;
        OnPropertyChanged(nameof(Choices));
    }

    /// <summary>Everything the panel shows of the craft, told again.</summary>
    public void Notify()
    {
        OnPropertyChanged(nameof(IsCraft));
        OnPropertyChanged(nameof(Family));
        OnPropertyChanged(nameof(CanCraftFrom));
        OnPropertyChanged(nameof(NowLines));
        OnPropertyChanged(nameof(IsUnknown));

        // The list only while the picker is there: shut, nobody reads it, and each notification worked it out for
        // nobody - and its rows asked again whether they could be picked.
        if (IsPickerPresent)
        {
            OnPropertyChanged(nameof(Choices));
        }
    }

    /// <summary>The picker belongs to the item that was showing when it opened; a rebuild closes it.</summary>
    public void Reset()
    {
        IsPickerOpen = false;
        Chosen = null;
        IsCraftingFrom = false;
    }

    /// <summary>
    /// Writes what a craft made in place changed besides the properties, which the rows write:
    /// the quality, the name and the affix ids. Answers whether there was one. The family is
    /// only dropped - the properties carried it - so it is read off the item from here on.
    /// </summary>
    public bool Commit(Item item)
    {
        bool crafted = Draft.CraftFrom is not null;
        if (Draft.CraftFrom is { } craft)
        {
            item.Quality = craft.Quality;
            item.RarePrefixId = craft.RarePrefixId;
            item.RareSuffixId = craft.RareSuffixId;
            item.MagicPrefixIds = craft.MagicPrefixIds is { } prefixes ? [.. prefixes] : null!;
            item.MagicSuffixIds = craft.MagicSuffixIds is { } suffixes ? [.. suffixes] : null!;
            Draft.CraftFrom = null;
        }

        Draft.CraftFamily = null;
        return crafted;
    }

    /// <summary>
    /// What <see cref="Commit"/> writes for a craft made in place, on a copy and with nothing let go of:
    /// for the item's summary.
    /// </summary>
    public void Wear(Item shadow)
    {
        if (Draft.CraftFrom is not { } craft)
            return;

        shadow.Quality = craft.Quality;
        shadow.RarePrefixId = craft.RarePrefixId;
        shadow.RareSuffixId = craft.RareSuffixId;
        shadow.MagicPrefixIds = craft.MagicPrefixIds is { } prefixes ? [.. prefixes] : null!;
        shadow.MagicSuffixIds = craft.MagicSuffixIds is { } suffixes ? [.. suffixes] : null!;
    }

    /// <summary>Opens the picker on an item that is not crafted yet.</summary>
    [RelayCommand]
    private void CraftFromItem()
    {
        if (!CanCraftFrom)
            return;

        // After the open, not before: OpenPicker clears the intent so the picker reached from
        // a crafted item's own crafting line never inherits it.
        OpenPicker();
        IsCraftingFrom = true;
    }

    [RelayCommand]
    private void OpenPicker()
    {
        _panel.Press();

        Chosen = null;
        IsCraftingFrom = false;
        OnPropertyChanged(nameof(NowLines));
        OnPropertyChanged(nameof(IsUnknown));
        OnPropertyChanged(nameof(Choices));
        IsPickerOpen = true;

        _panel.Reveal(VP_ViewModel_Inv_R.CraftList);
    }

    [RelayCommand]
    private void ClosePicker()
    {
        Chosen = null;
        IsCraftingFrom = false;
        IsPickerOpen = false;

        // Nothing was settled, so nothing stays marked.
        OnPropertyChanged(nameof(Choices));
    }

    /// <summary>
    /// Takes one from the list, its numbers rolled inside their ranges, and stages it - the picker
    /// closes (a pick is the change - written at once, and taken back by Ctrl + Z).
    /// </summary>
    [RelayCommand]
    private void Pick(CraftChoiceRow? row)
    {
        if (row is null)
            return;

        _panel.Press();

        Chosen = Crafts.Draft(row.Recipe, new Random());
        Confirm();
    }

    /// <summary>
    /// Takes the old family's properties off and puts the chosen one's on, staged rather
    /// than written - the same route an affix swap takes, worked out on the item as the
    /// panel holds it so nothing already pending is lost.
    /// </summary>
    private void Confirm()
    {
        if (_panel.Selected is not { } tile || Chosen is not { } chosen)
            return;

        if (_panel.Pending() is not { } shadow)
            return;

        // The name it has before the craft: the change is kept at once, and the tile then has the crafted name.
        string name = tile.Name;

        var random = new Random();
        int gone;

        if (IsCraftingFrom)
        {
            // In place, staged, as the runeword builder is, and open in the vault again. It used to go onto a duplicate; Duplicate
            // is the explicit way to keep the original. The recipe as the game makes it - quality,
            // the cube's item level from the character's, the name, the affixes, the automagic
            // kept - then the family's lines at the values this panel rolled.
            Crafts.CraftFrom(shadow, chosen.Recipe, random, _panel.CharacterLevel);
            Crafts.Reforge(shadow, chosen, random);
            Draft.CraftFrom = shadow;

            // A new item, which does not carry the corruption or the desecration (CanCraftFrom): the craft has
            // taken their lines off, and the pair that marks each has no row, so it goes by being staged. Left
            // on, the crafted item read as corrupted with its corruption's number gone. Its empty sockets
            // go too (Crafts.CraftFrom), staged as a clean's are.
            _panel.StageSocketsFrom(shadow);
            if (Corruptions.IdOn(tile.Item) is > 0 || Corruptions.IsSpent(tile.Item) || Draft.Corruption is > 0)
                Draft.Corruption = 0;

            if (Desecrations.IsDesecrated(tile.Item) || Draft.Desecration is > 0)
            {
                Draft.Desecration = 0;
                Draft.DesecrationMod = null;
            }

            // What the rows and the name show pending; the rest is written from the shadow on KEEP.
            _panel.ShowBasicsOf(shadow);

            IsCraftingFrom = false;
            gone = 0;
        }
        else
        {
            gone = Crafts.Reforge(shadow, chosen, random).Count(each => each.Removed);
        }

        // Another family's lines now: a recipe a reroll pinned no longer answers, on the item or on its next copy.
        AffixRanges.ForgetPinnedRecipe(shadow);
        AffixRanges.ForgetPinnedRecipe(tile.Item);

        _panel.StageStatsFrom(shadow, tile);

        Draft.CraftFamily = chosen.Family;
        Chosen = null;
        // Open still, as the corruption's picker: the pick is marked, another takes its place; written when it closes.

        // Not a rebuild: that reads the rows from the item on disk, which is exactly the staged
        // change just worked out. StageStatsFrom has already put the new properties into the
        // rows, the way the corruption panel leaves them.
        _panel.AffixesChanged(rebuild: true);
        _panel.Changed();
        Notify();

        string family = L(chosen.Family).ToLowerInvariant();
        _panel.Say(gone > 0
            ? Plural(gone, $"{name}: now {family}, {gone} property taken off.",
                     $"{name}: now {family}, {gone} properties taken off.")
            : L($"{name}: now {family}."));
    }
}
