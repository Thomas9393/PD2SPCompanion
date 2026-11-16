using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D2SLib.Model.Save;
using PD2SPCompanion.Core.Graphics;
using PD2SPCompanion.Shell.Imaging;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// One line of the map picker: something the cube does to a map with one item - the item's name, what it
/// writes, and whether the map shows its mark.
/// </summary>
public sealed record MapRecipeRow(MapRecipes.Recipe Recipe, string Name, IReadOnlyList<string> Lines, bool IsHeld) : IPickRow;

/// <summary>One recipe the map shows the mark of, as the picker's top says it, with its own brush to take it off.</summary>
public sealed record MapRecipeHeldRow(MapRecipes.Recipe Recipe, string Name);

/// <summary>
/// The item panel's map recipes: what the cube does to
/// a map besides corrupting it - the Standard of Heroes, the Orb of Fortification, the Catalyst Shard and
/// the Corrupted Ears. Each is fixed numbers written onto the map and one stat that says it was done, so a
/// press puts one on and another takes it off; an ear put on takes the place of the ear that was there.
/// <para>
/// Staged through the rows as a superior's bonus is. The picker stays open - a map takes several of these
/// together - and a host that keeps at once keeps them when it closes.
/// </para>
/// </summary>
public sealed partial class MapRecipeEditor : ViewModelBase
{
    private readonly IItemPanel _panel;

    public MapRecipeEditor(IItemPanel panel) => _panel = panel;

    /// <summary>
    /// The item as the page has it: what is staged counts. Only a map's: the pending item was made for every item
    /// selected and every digit typed, five times a notification, to find it was not a map. A base changed on the page stays of its kind.
    /// </summary>
    private Item? Held => _panel.Selected is { TabSlot: null } tile && !tile.Item.IsCompact
                          && MapRecipes.AnyFor(_panel.Draft.Code ?? tile.Item.Code)
        ? _panel.Pending() ?? tile.Item
        : null;

    /// <summary>Whether the cube has anything of the kind for this item: a magic, rare or unique map.</summary>
    public bool Shows => Held is { } item && MapRecipes.Available(item).Count > 0;

    /// <summary>The Standard of Heroes, as the cubes and the shard stand for theirs: the game's own picture, read from the install.</summary>
    public Avalonia.Media.Imaging.Bitmap? Icon => SpriteBitmap.From(SpriteLoader.Current?.Load("invflag"));

    /// <summary>
    /// What a recipe writes, a line a property. Its mark is said as the game says it and without its
    /// number - "Fortified", not "Fortified 50" - and a guarded one's not at all: "Treacherous 7" is the
    /// Assassin's ear by its number, which the row's own name says better.
    /// </summary>
    private static IReadOnlyList<string> Words(MapRecipes.Recipe recipe)
    {
        bool Marks(CorruptionGrant grant) =>
            AffixRanges.StatsWritten(grant.Code).Contains(recipe.Marker, StringComparer.OrdinalIgnoreCase);

        var lines = new List<string>();
        if (!recipe.Once && StatText.Describe(recipe.Marker, recipe.MarkerValue) is { Length: > 0 } mark)
            lines.Add(mark);

        lines.AddRange(Crafts.Ranges(new AffixRanges.CraftRecipe(
            "", [.. recipe.Grants.Where(grant => !Marks(grant))
                .Select(grant => new AffixRanges.CraftGrant(grant.Code, grant.Min, grant.Max, grant.Param))])));
        return lines;
    }

    /// <summary>What has been done to the map, by the names of what did it.</summary>
    public IReadOnlyList<string> NowLines => Held is { } item ? [.. MapRecipes.On(item).Select(recipe => recipe.Name)] : [];

    public bool HasNone => NowLines.Count == 0;

    /// <summary>
    /// The same, each to be taken off by its own brush, as a staff mod is. A press on the row
    /// in the list still takes it off too.
    /// </summary>
    public IReadOnlyList<MapRecipeHeldRow> HeldRows =>
        Held is { } item ? [.. MapRecipes.On(item).Select(recipe => new MapRecipeHeldRow(recipe, recipe.Name))] : [];

    /// <summary>Everything the cube would do to this map, in the table's order, what is on it marked.</summary>
    public IReadOnlyList<MapRecipeRow> Choices
    {
        get
        {
            if (Held is not { } item)
                return [];

            var on = MapRecipes.On(item);
            return [.. MapRecipes.Available(item).Select(recipe => new MapRecipeRow(recipe, recipe.Name, Words(recipe), on.Contains(recipe)))];
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

    /// <summary>Everything the panel shows of them, told again.</summary>
    public void Notify()
    {
        OnPropertyChanged(nameof(Shows));
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

    /// <summary>One recipe taken off by the brush beside it, the others kept.</summary>
    [RelayCommand]
    private void TakeOff(MapRecipeHeldRow? row)
    {
        if (row is not null && Choices.FirstOrDefault(choice => choice.Recipe == row.Recipe && choice.IsHeld) is { } held)
            Pick(held);
    }

    /// <summary>
    /// Puts one on, or takes it off where the map shows its mark. One that shares its mark with another
    /// on the map - an ear with an ear - takes that one's place. Worked out on the item as the panel holds
    /// it, so nothing already pending is lost.
    /// </summary>
    [RelayCommand]
    private void Pick(MapRecipeRow? row)
    {
        if (row is null || _panel.Selected is not { } tile || _panel.Pending() is not { } shadow)
            return;

        _panel.Press();

        bool off = MapRecipes.On(shadow).Contains(row.Recipe);
        if (off)
        {
            MapRecipes.TakeOff(shadow, row.Recipe);
        }
        else
        {
            foreach (var rival in MapRecipes.On(shadow)
                         .Where(done => done.Marker.Equals(row.Recipe.Marker, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                MapRecipes.TakeOff(shadow, rival);
            }

            MapRecipes.Apply(shadow, row.Recipe);
        }

        _panel.StageStatsFrom(shadow, tile);
        _panel.AffixesChanged(rebuild: false);
        _panel.Changed();
        Notify();

        string name = tile.Name, recipe = row.Name;
        _panel.Say(off ? L($"{name}: {recipe} removed.") : L($"{name}: {recipe} added."));
    }
}
