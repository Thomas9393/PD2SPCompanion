using Avalonia.Media.Imaging;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Vault;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>One recipe of the chart: how many of it can be built, and whether it is the one the strip under the chart tells of.</summary>
public sealed partial class CraftCellViewModel(CraftPlan.Cell cell) : ObservableObject
{
    /// <summary>The recipe as last counted.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Count))]
    [NotifyPropertyChangedFor(nameof(Text))]
    [NotifyPropertyChangedFor(nameof(CanMake))]
    private CraftPlan.Cell _cell = cell;

    public int Count => Cell.Makeable;

    /// <summary>The number, or a quiet dot where none can be built: seventy numbers, most of them 0, hid the ones that are not.</summary>
    public string Text => Count > 0 ? Count.ToString(System.Globalization.CultureInfo.CurrentCulture) : "·";

    public bool CanMake => Count > 0;

    [ObservableProperty] private bool _isSelected;
}

/// <summary>A row of the chart: a type of item - its name under a base of it - and its recipe in each family.</summary>
public sealed record CraftRowViewModel(string Label, Bitmap? Picture, IReadOnlyList<CraftCellViewModel> Cells);

/// <summary>A column's head: a family, under the infusion that is the family's own.</summary>
public sealed record CraftFamilyHead(string Name, Bitmap? Orb, string OrbName);

/// <summary>
/// One thing that goes into the cube for the recipe told of, as the strip draws it: its picture, how many
/// there are under it - the materials tab's, or the vault's of a base - and what joins it to the part before.
/// </summary>
/// <param name="Before">"+", or "or" before the infusion that stands for the three before it; nothing before the first.</param>
/// <param name="IsShort">Fewer than the recipe takes.</param>
/// <param name="Opens">The first of the three the infusion stands for: a bracket opens before it.</param>
/// <param name="Closes">The last of the three: the bracket closes after it.</param>
public sealed record CraftPart(
    Bitmap? Sprite, string Name, string Count, bool IsShort, string Before, bool Opens = false, bool Closes = false)
{
    public bool HasBefore => Before.Length > 0;
}

/// <summary>
/// The crafting wizard on the Extra page, under the rune wizard: the wiki's chart of the craft recipes - a type of item a row, a family a
/// column - each cell the number of that recipe that can be built from what is on hand, and under the chart
/// what the recipe picked takes, what there is of it, and what it grants. It tells; it crafts nothing.
/// <para>
/// On hand: the materials of the shared stash's materials tab, as the rune wizard reads it, and the bases in
/// the vault's Crafting Bases folder - those and no others.
/// The counting is Core's (<see cref="CraftPlan"/>).
/// </para>
/// </summary>
public sealed partial class CraftWizardViewModel : ObservableObject
{
    private readonly RuneWizardViewModel _materials;
    private readonly VaultService _vaults;

    public CraftWizardViewModel(RuneWizardViewModel materials, VaultService vaults)
    {
        _materials = materials;
        _vaults = vaults;
    }

    /// <summary>Folded away, and kept so, as every card's fold is.</summary>
    [ObservableProperty] private bool _isOpen = FoldMemory.IsOpen(nameof(CraftWizardViewModel) + nameof(IsOpen));

    partial void OnIsOpenChanged(bool value) => FoldMemory.Keep(nameof(CraftWizardViewModel) + nameof(IsOpen), value);

    [RelayCommand]
    private void ToggleOpen() => IsOpen = !IsOpen;

    /// <summary>
    /// The mark behind the heading: where the bases are counted from, and the way they
    /// get there - Fast inventory storing with Shift held, which sorts crafting bases into that folder.
    /// </summary>
    public string BasesTip =>
        L($"Bases counted from vault folder {_folders}")
        + Environment.NewLine + L("Fast inventory storing with Shift puts bases there");

    /// <summary>The folders the bases are counted from, by the names they wear now: said by the mark.</summary>
    private string _folders = SmartSort.CraftingBasesFolder;

    /// <summary>The chart's columns: the families, in the wiki's order.</summary>
    public ObservableCollection<CraftFamilyHead> Families { get; } = [];

    public ObservableCollection<CraftRowViewModel> Rows { get; } = [];

    /// <summary>Beside the card's heading: "12 of 70 can be built".</summary>
    [ObservableProperty] private string _summary = "";

    /// <summary>The recipe the strip under the chart tells of, or none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelected))]
    private CraftCellViewModel? _selected;

    public bool HasSelected => Selected is not null;

    [RelayCommand]
    private void Pick(CraftCellViewModel? cell)
    {
        if (cell is not null)
            Selected = cell;
    }

    /// <summary>A click anywhere else on the page: the recipe is let go.</summary>
    public void Release() => Selected = null;

    partial void OnSelectedChanged(CraftCellViewModel? oldValue, CraftCellViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.IsSelected = false;
        if (newValue is not null)
            newValue.IsSelected = true;

        Tell();
    }

    // ---- the strip under the chart: the recipe picked ----------------------------------------------------

    [ObservableProperty] private string _title = "";

    /// <summary>"Can build 3", or why not: "No base", "Materials missing".</summary>
    [ObservableProperty] private string _verdict = "";

    [ObservableProperty] private bool _verdictIsGood;

    /// <summary>
    /// What goes into the cube, left to right: the base, and with it the jewel, the rune and the gem in a
    /// bracket, or the infusion. The base first and the three bracketed: with the base last the line
    /// read as the three alone, or the infusion with a base.
    /// </summary>
    public ObservableCollection<CraftPart> Parts { get; } = [];

    /// <summary>What the family puts on the item, a line each: drawn as an item's properties are.</summary>
    [ObservableProperty] private string _grants = "";

    private void Tell()
    {
        Parts.Clear();
        if (Selected?.Cell is not { } cell)
        {
            (Title, Verdict, VerdictIsGood, Grants) = ("", "", false, "");
            return;
        }

        Title = L($"{cell.Card.Family} {GrailCatalog.TypeLabel(cell.Card.Type)}");
        VerdictIsGood = cell.Makeable > 0;
        Verdict = cell.Makeable > 0 ? L($"Can build {cell.Makeable}")
            : cell.ByMaterials == 0 && cell.Bases == 0 ? L("No materials, no base")
            : cell.Bases == 0 ? L("No base")
            : L("Materials missing");

        Parts.Add(new CraftPart(
            BaseSprite(cell.Card), L("Crafting bases in vault"), Said(cell.Bases), cell.Bases == 0, ""));

        // The three are bracketed only where there is an infusion to stand for them.
        bool bracketed = cell.Infusion is not null && cell.Needs.Count > 1;
        for (int i = 0; i < cell.Needs.Count; i++)
        {
            Parts.Add(Part(cell.Needs[i], "+") with
            {
                Opens = bracketed && i == 0,
                Closes = bracketed && i == cell.Needs.Count - 1,
            });
        }

        if (cell.Infusion is { } infusion)
            Parts.Add(Part(infusion, L("or")));

        Grants = string.Join(Environment.NewLine, Crafts.Ranges(cell.Card.Recipe));
    }

    private CraftPart Part(CraftPlan.Need need, string before) =>
        new(SpriteOf(need.Code), ItemPresenter.BaseName(need.Code), Said(need.Have), !need.IsMet, before);

    private static string Said(int count) => count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>The pictures drawn so far, by item code: the game's own, read once each.</summary>
    private readonly Dictionary<string, Bitmap?> _sprites = new(StringComparer.OrdinalIgnoreCase);

    private Bitmap? SpriteOf(string code)
    {
        // A jewel wears one of several pictures, and a different one each time it is shown here (a small eye candy).
        // Drawn with none it was the base's bare file, which is no jewel's.
        int looks = ItemTypes.VariantCount(code);
        int look = looks > 1 ? Random.Shared.Next(looks) : 0;
        string key = looks > 1 ? $"{code}#{look}" : code;

        if (!_sprites.TryGetValue(key, out var sprite))
        {
            var item = ItemForge.Material(code, 1, "");
            if (looks > 1)
                (item.HasMultipleGraphics, item.GraphicId) = (true, (byte)look);

            _sprites[key] = sprite = new ItemTileViewModel(item, 0).Sprite;
        }

        return sprite;
    }

    /// <summary>
    /// A base of the recipe's type, as the grail's Crafting tab drew its cards (GrailCatalog.CraftEntries):
    /// the classic look where one was chosen - a Monarch, a Long Sword, a Light Plate, a Helm - and
    /// elsewhere the base the recipe's own description names.
    /// </summary>
    private Bitmap? BaseSprite(AffixRanges.CraftCard card)
    {
        string key = "base:" + card.Type;
        if (_sprites.TryGetValue(key, out var sprite))
            return sprite;

        var entry = GrailCatalog.CraftEntries().FirstOrDefault(each =>
            each.Card.Type.Equals(card.Type, StringComparison.OrdinalIgnoreCase)
            && each.Card.Family.Equals(card.Family, StringComparison.OrdinalIgnoreCase));

        return _sprites[key] = entry is null ? null : new ItemTileViewModel(entry.Item, 0).Sprite;
    }

    // ---- counting ------------------------------------------------------------------------------------------

    /// <summary>The chart has been drawn once, with a recipe picked for it: after that the player picks, or none is.</summary>
    private bool _begun;

    /// <summary>
    /// Counts everything again: called with the rune wizard's count, whenever the character, the stash or the
    /// vault may have changed and each time the page comes to the front.
    /// </summary>
    public void Refresh()
    {
        var tab = _materials.MaterialsTab()?.Count;
        IReadOnlyList<CraftPlan.Cell> cells;
        try
        {
            cells = CraftPlan.Plan(tab ?? (_ => 0), Bases());
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            return;
        }

        if (cells.Count == 0)
            return;

        var byPlace = cells.ToDictionary(
            cell => (cell.Card.Type.ToLowerInvariant(), cell.Card.Family.ToLowerInvariant()));

        // The chart is made once and its cells told their counts after: a recipe picked stays picked, and
        // nothing is drawn again that has not changed.
        if (Rows.Count == 0)
        {
            foreach (string family in GrailCatalog.FamilyOrder)
            {
                if (cells.FirstOrDefault(cell => cell.Card.Family.Equals(family, StringComparison.OrdinalIgnoreCase)) is not { } named)
                    continue;

                string? orb = named.Card.Infusion;
                Families.Add(new CraftFamilyHead(
                    named.Card.Family, orb is null ? null : SpriteOf(orb), orb is null ? "" : ItemPresenter.BaseName(orb)));
            }

            foreach (string type in GrailCatalog.TypeOrder)
            {
                var row = GrailCatalog.FamilyOrder
                    .Select(family => byPlace.GetValueOrDefault((type, family)))
                    .OfType<CraftPlan.Cell>()
                    .Select(cell => new CraftCellViewModel(cell))
                    .ToList();
                if (row.Count > 0)
                    Rows.Add(new CraftRowViewModel(GrailCatalog.TypeLabel(type), BaseSprite(row[0].Cell.Card), row));
            }
        }
        else
        {
            foreach (var shown in Rows.SelectMany(row => row.Cells))
            {
                if (byPlace.TryGetValue((shown.Cell.Card.Type.ToLowerInvariant(), shown.Cell.Card.Family.ToLowerInvariant()), out var counted))
                    shown.Cell = counted;
            }
        }

        int makeable = cells.Count(cell => cell.Makeable > 0);
        Summary = L($"{makeable} of {cells.Count} can be built");

        // To begin with, the first there is to build - or the chart's first recipe. Once only: a recipe let
        // go stays let go.
        if (!_begun)
        {
            _begun = true;
            Selected = Rows.SelectMany(row => row.Cells).FirstOrDefault(cell => cell.CanMake) ?? Rows[0].Cells[0];
        }
        else
            Tell();
    }

    /// <summary>
    /// What lies in the vault's Crafting Bases folder and the folders under it - the folder Store all's
    /// crafting-base sorting fills (SmartSort) - read as items. None where the vault has no such folder.
    /// </summary>
    private IReadOnlyList<Item> Bases()
    {
        // Where the sorter delivers crafting bases, by its own rules and its own links: a folder renamed or
        // moved is still the one: the wizards and the sorter looked at different things.
        var roots = SmartSort.CraftingBasesRoots(SmartSort.Current());
        string folders = string.Join(", ", roots.Select(root => _vaults.Store?.DeliveryName(root) ?? root).Distinct());
        if (folders != _folders)
        {
            _folders = folders;
            OnPropertyChanged(nameof(BasesTip));
        }

        if (_vaults.Store is not { } store)
            return [];

        return
        [
            .. roots.SelectMany(store.RowsUnder)
                .DistinctBy(row => row.Key)
                .Select(row => ItemBlob.Restore(row.Blob, VaultService.StatIdBits)),
        ];
    }
}
