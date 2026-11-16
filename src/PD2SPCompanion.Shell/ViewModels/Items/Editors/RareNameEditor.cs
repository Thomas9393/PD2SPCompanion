using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// The item panel's rare name: the two halves a rare or crafted item is named from, picked over the
/// page from two lists and held until KEEP like every other edit.
/// </summary>
public sealed partial class RareNameEditor : ViewModelBase
{
    private readonly IItemPanel _panel;

    public RareNameEditor(IItemPanel panel) => _panel = panel;

    [ObservableProperty] private bool _isPickerPresent;

    partial void OnIsPickerOpenChanged(bool value) =>
        Presence.Keep(value, () => IsPickerOpen, on => IsPickerPresent = on, VP_ViewModel_Inv_R.VeilFade);

    /// <summary>The halves set to what the item holds, the searches emptied: a new item or a discard.</summary>
    public void ShowStored()
    {
#pragma warning disable MVVMTK0034
        _first = StoredFirst;
        _second = StoredSecond;
        OnPropertyChanged(nameof(First));
        OnPropertyChanged(nameof(Second));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(HasName));
        OnPropertyChanged(nameof(IsChanged));
        _firstSearch = "";
        _secondSearch = "";
        OnPropertyChanged(nameof(FirstSearch));
        OnPropertyChanged(nameof(SecondSearch));
        OnPropertyChanged(nameof(FirstNames));
        OnPropertyChanged(nameof(SecondNames));
        OnPropertyChanged(nameof(NoFirstNames));
        OnPropertyChanged(nameof(NoSecondNames));
#pragma warning restore MVVMTK0034
    }

    /// <summary>A new item's own halves shown pending, as a craft made in place brings them.</summary>
    public void ShowFrom(Item shadow)
    {
        First = shadow.RarePrefixId;
        Second = shadow.RareSuffixId;
        OnPropertyChanged(nameof(Name));
    }

    /// <summary>Writes the halves onto the item at KEEP.</summary>
    public void Commit(Item item)
    {
        if (!IsChanged)
            return;

        item.RarePrefixId = (ushort)First;
        item.RareSuffixId = (ushort)Second;
    }

    /// <summary>
    /// The two halves of a rare item's name, as they will read once kept. Staged like the
    /// rest of the page - the name is two numbers in the file, and picking one is an edit
    /// rather than an action.
    /// </summary>
    [ObservableProperty] private int _first;

    [ObservableProperty] private int _second;

    private int StoredFirst => _panel.Selected?.Item.RarePrefixId ?? 0;

    private int StoredSecond => _panel.Selected?.Item.RareSuffixId ?? 0;

    public bool HasName => _panel.Selected is { } tile
        && tile.Item.Quality is ItemQuality.Rare or ItemQuality.Craft;

    public bool IsChanged => HasName
        && (First != StoredFirst || Second != StoredSecond);

    /// <summary>The name as picked, which is what the button shows before it is kept.</summary>
    public string Name => AffixNames.NameFrom(First, Second);

    /// <summary>
    /// Only the halves this base could actually carry. The tables restrict each name by
    /// item type, and a list that offers Tread on a knife is a list that invites making an
    /// item the game would never have produced.
    /// </summary>
    public IReadOnlyList<RareHalfRow> FirstNames => _panel.Selected is { } tile
        ? [.. Matching(AffixNames.FirstNames(tile.Item), FirstSearch)
            .Select(half => new RareHalfRow(half, half.Id == DraftFirst))]
        : [];

    public IReadOnlyList<RareHalfRow> SecondNames => _panel.Selected is { } tile
        ? [.. Matching(AffixNames.SecondNames(tile.Item), SecondSearch)
            .Select(half => new RareHalfRow(half, half.Id == DraftSecond))]
        : [];

    /// <summary>
    /// What has been typed into each list's search. One a list rather than one for both:
    /// the halves are chosen independently, and a word that narrows the first rarely
    /// narrows the second.
    /// </summary>
    [ObservableProperty] private string _firstSearch = "";

    [ObservableProperty] private string _secondSearch = "";

    partial void OnFirstSearchChanged(string value)
    {
        OnPropertyChanged(nameof(FirstNames));
        OnPropertyChanged(nameof(NoFirstNames));
    }

    partial void OnSecondSearchChanged(string value)
    {
        OnPropertyChanged(nameof(SecondNames));
        OnPropertyChanged(nameof(NoSecondNames));
    }

    /// <summary>
    /// Whether a list is empty, told again wherever the list is: read once and never raised, it
    /// kept the empty list of the moment before an item was chosen, and "No match."
    /// stood over a full list.
    /// </summary>
    public bool NoFirstNames => FirstNames.Count == 0;

    public bool NoSecondNames => SecondNames.Count == 0;

    private static IReadOnlyList<RareHalf> Matching(IReadOnlyList<RareHalf> names, string typed) =>
        typed.Trim() is { Length: > 0 } wanted
            ? [.. names.Where(half =>
                half.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase))]
            : names;

    [ObservableProperty] private bool _isPickerOpen;

    /// <summary>
    /// The halves the lists show held. A half picked goes to the field at once, staged until the
    /// item is kept (a pick is the change - written at once, and taken back by Ctrl + Z); the picker stays open for the other half.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FirstNames))]
    [NotifyPropertyChangedFor(nameof(NoFirstNames))]
    private int _draftFirst;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SecondNames))]
    [NotifyPropertyChangedFor(nameof(NoSecondNames))]
    private int _draftSecond;

    /// <summary>The name as it is being picked, which is what the panel reads back.</summary>
    public string DraftName => AffixNames.NameFrom(DraftFirst, DraftSecond);

    [RelayCommand]
    private void OpenPicker()
    {
        _panel.Press();

        DraftFirst = First;
        DraftSecond = Second;
        FirstSearch = "";
        SecondSearch = "";

        OnPropertyChanged(nameof(DraftName));
        IsPickerOpen = true;

        _panel.Reveal(VP_ViewModel_Inv_R.RareFirstList, VP_ViewModel_Inv_R.RareSecondList);
    }

    [RelayCommand]
    private void ClosePicker() => IsPickerOpen = false;

    [RelayCommand]
    private void PickFirst(RareHalfRow? row)
    {
        if (row is null)
            return;

        DraftFirst = row.Half.Id;
        AfterPick();
    }

    [RelayCommand]
    private void PickSecond(RareHalfRow? row)
    {
        if (row is null)
            return;

        DraftSecond = row.Half.Id;
        AfterPick();
    }

    /// <summary>
    /// A name drawn at random from the two lists the item's kind has, as the game names a rare it makes: the
    /// Forge's roll as a new drop with Shift, with the affixes it draws. True where the
    /// name is another than it was.
    /// </summary>
    public bool RollName(Random random)
    {
        // From the whole of each list, not as a search has narrowed it.
        if (!HasName || _panel.Selected is not { } tile
            || AffixNames.FirstNames(tile.Item) is not { Count: > 0 } firsts
            || AffixNames.SecondNames(tile.Item) is not { Count: > 0 } seconds)
            return false;

        var first = firsts[random.Next(firsts.Count)].Id;
        var second = seconds[random.Next(seconds.Count)].Id;
        if (first == First && second == Second)
            return false;

        DraftFirst = first;
        DraftSecond = second;
        AfterPick();
        return true;
    }

    /// <summary>
    /// The Flawless Skull: a name at random, as the game names a rare - every picker has its roll.
    /// Another name than the one it has, a press always changing it.
    /// </summary>
    [RelayCommand]
    private void Roll()
    {
        var random = new Random();
        for (int tries = 0; tries < 16 && !RollName(random); tries++)
        {
        }
    }

    /// <summary>The half into the field, where the page holds it until KEEP.</summary>
    private void AfterPick()
    {
        _panel.Press();

        First = DraftFirst;
        Second = DraftSecond;

        OnPropertyChanged(nameof(DraftName));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(IsChanged));
        _panel.NameChanged();
        _panel.Changed();
    }

}
