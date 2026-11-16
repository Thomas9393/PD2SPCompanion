using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// What the inventory page's tests ask of the program again and again, built from what the fixture has: the
/// page's one section and what its panes show, a tile by its item's name, the cell of a container, a vault
/// page's cards - and the way back for a test that changed nothing saved (<see cref="PanesAsFound"/>).
/// Everything here is asked on the program's thread, by the caller.
/// </summary>
internal static class InventoryPad
{
    public static VP_ViewModel_Inv_L Page(this Program program) => program.Main.Inventory!;

    /// <summary>The one section of the Items page, whose panes pick any container.</summary>
    public static ContainerSectionViewModel Section(this Program program) =>
        program.Page().Sections.First(section => section.Pages is not null);

    /// <summary>A line of the panes' picker: "Inventory", "Cube", "Personal Stash", "Shared Page 5", "Materials".</summary>
    public static StashPageChoice Choice(this Program program, string label) =>
        program.Section().Pages!.First(page => page.Label == label);

    public static GridViewModel Grid(this Program program, string container) => program.Choice(container).Grid!;

    /// <summary>The first tile of that name in a container.</summary>
    public static ItemTileViewModel Tile(this Program program, string name, string container) =>
        program.Grid(container).Items.First(tile => tile.Name == name);

    public static bool Holds(this Program program, string container, string name) =>
        program.Grid(container).Items.Any(tile => tile.Name == name);

    /// <summary>How many tiles of that name a container holds.</summary>
    public static int Count(this Program program, string container, string name) =>
        program.Grid(container).Items.Count(tile => tile.Name == name);

    public static GridCell Cell(this Program program, string container, int column, int row) =>
        program.Grid(container).Cells.First(cell => cell.Column == column && cell.Row == row);

    /// <summary>The first pane on one container; the second on another, or put away.</summary>
    public static void Panes(this Program program, string first, string? second = null)
    {
        var section = program.Section();
        section.SelectedPage = program.Choice(first);
        section.SecondShown = second is not null;
        if (second is not null)
            section.SecondPage = program.Choice(second);
    }

    /// <summary>
    /// The page as a test finds it, for one that changed nothing saved and so is not followed by the fixture's
    /// own putting back: nothing carried, nothing selected, the inventory in the one pane shown.
    /// </summary>
    public static void PanesAsFound(this Program program) => program.On(() =>
    {
        var page = program.Page();
        if (page.Session.IsHolding)
            page.CancelHold();
        page.EndTabCarry();
        page.ClearSelection();
        page.DisarmDelete();

        if (page.Sections.FirstOrDefault(section => section.Pages is not null) is not { } section)
            return;

        section.SecondShown = false;
        section.SelectedPage = section.Pages!.First(each => each.Label == "Inventory");
        if (section.Pages!.FirstOrDefault(each => each.Label == "Personal Stash") is { } personal)
            section.SecondPage = personal;
    });

    /// <summary>Where every item of a container lies, by its id: "id@x,y", in order.</summary>
    public static string Cells(this Program program, string container) =>
        string.Join(" ", program.Grid(container).Items.Select(tile => $"{tile.Item.Id}@{tile.Item.X},{tile.Item.Y}").Order(StringComparer.Ordinal));

    /// <summary>The ids of a container's items, in order.</summary>
    public static string Ids(this Program program, string container) =>
        string.Join(",", program.Grid(container).Items.Select(tile => tile.Item.Id).Order());

    /// <summary>Whether any item of a container lies over another, or past its edges.</summary>
    public static bool Overlaps(this Program program, string container)
    {
        var grid = program.Grid(container);
        var taken = new HashSet<(int, int)>();
        foreach (var tile in grid.Items)
        {
            for (int x = tile.Item.X; x < tile.Item.X + tile.View.GridWidth; x++)
            {
                for (int y = tile.Item.Y; y < tile.Item.Y + tile.View.GridHeight; y++)
                {
                    if (x >= grid.Columns || y >= grid.Rows || !taken.Add((x, y)))
                        return true;
                }
            }
        }

        return false;
    }

    /// <summary>The tile lying on a cell of a container, or null for a free one.</summary>
    public static ItemTileViewModel? At(this Program program, string container, int column, int row) =>
        program.Grid(container).Items.FirstOrDefault(tile =>
            column >= tile.Item.X && column < tile.Item.X + tile.View.GridWidth
            && row >= tile.Item.Y && row < tile.Item.Y + tile.View.GridHeight);

    /// <summary>How many items the open vault holds.</summary>
    public static int InVault(this Program program) => program.Get<VaultService>().Store!.Count();

    /// <summary>A vault page's cards, its sheet read as the window reads it (VaultHands.CardsOf).</summary>
    public static IReadOnlyList<ItemTileViewModel> CardsOf(VP_ViewModel_Vault_L vault) => Vault.VaultHands.CardsOf(vault);

    /// <summary>A vault folder's id, by its name.</summary>
    public static long Folder(this Program program, string name) =>
        program.Get<VaultService>().Store!.Folders().First(folder => folder.Name == name).Id;

    /// <summary>The autosave made now, as it is made a moment after a change: what was done becomes a step of the history.</summary>
    public static bool Saved(this Program program) => program.Get<AutoSave>().Flush();
}
