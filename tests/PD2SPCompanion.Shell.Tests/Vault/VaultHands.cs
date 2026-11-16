using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// What the tests of the vault page, of trading beside it and of the grail do with their hands: the vault put
/// on a page of the book and read as the window reads it, a folder gone to by its name, an item's tile found
/// in the container it lies in, a Ctrl + click as the page's own picking makes it, and both pages brought back
/// to where a test found them. Built from the fixture's public members; everything here is asked on the
/// program's thread.
/// </summary>
internal static class VaultHands
{
    /// <summary>
    /// A vault page's cards, its sheet read as the window reads it: a stretch of the sheet reads its items
    /// when it is drawn, and every reload makes the stretches anew.
    /// </summary>
    public static IReadOnlyList<ItemTileViewModel> CardsOf(VP_ViewModel_Vault_L vault)
    {
        foreach (var row in vault.PageRows.ToList())
            _ = row.Pages;

        return vault.Cards;
    }

    public static ItemTileViewModel? CardOrNone(VP_ViewModel_Vault_L vault, string name) =>
        CardsOf(vault).FirstOrDefault(card => card.Name == name);

    public static ItemTileViewModel Card(VP_ViewModel_Vault_L vault, string name) =>
        CardsOf(vault).First(card => card.Name == name);

    /// <summary>The vault on the left page of the book, listed: Show is what the view calls as the page comes into sight.</summary>
    public static VP_ViewModel_Vault_L OnLeft(Program program)
    {
        program.Main.ShowLeftCommand.Execute(MainViewModel.VaultMark);
        program.Main.Vault!.Show();
        return program.Main.Vault!;
    }

    /// <summary>The vault on the right page, beside whatever the left page shows.</summary>
    public static VP_ViewModel_Vault_L OnRight(Program program)
    {
        program.Main.ShowRightCommand.Execute(MainViewModel.VaultMark);
        program.Main.RightVault!.Show();
        return program.Main.RightVault!;
    }

    /// <summary>A folder's row of the tree, by its name.</summary>
    public static FolderPlace Place(VP_ViewModel_Vault_L vault, string name) =>
        vault.Places.First(place => place.Name == name && place.Id is not null);

    /// <summary>A folder gone to by its row of the tree, as a click on the row goes.</summary>
    public static void GoTo(VP_ViewModel_Vault_L vault, string name) => vault.GoToPlace(Place(vault, name));

    /// <summary>
    /// Every folder of a page's tree folded, however a test left it: how a folder is made, and how the tree opens
    /// - a fold is kept while the program runs, so one test's is the next one's.
    /// </summary>
    public static void FoldAll(VP_ViewModel_Vault_L vault)
    {
        foreach (var place in vault.Places.Where(place => place.HoldsFolders && place.IsOpen).ToList())
            vault.FoldPlaceCommand.Execute(place);
    }

    /// <summary>How many items the vault's file holds.</summary>
    public static int Held(Program program) => program.Get<VaultService>().Store!.Count();

    /// <summary>The Items page's one section of containers, whose panes pick any of them.</summary>
    public static ContainerSectionViewModel Section(Program program) =>
        program.Main.Inventory!.Sections.First(section => section.Pages is not null);

    public static StashPageChoice Choice(Program program, string label) =>
        Section(program).Pages!.First(page => page.Label == label);

    /// <summary>An item's tile in the grid of the container it lies in.</summary>
    public static ItemTileViewModel? TileOrNone(Program program, string name, string container) =>
        Choice(program, container).Grid!.Items.FirstOrDefault(tile => tile.Name == name);

    public static ItemTileViewModel Tile(Program program, string name, string container) =>
        Choice(program, container).Grid!.Items.First(tile => tile.Name == name);

    /// <summary>
    /// Ctrl + click on an item, as the pages' picking makes it of a press and its release (DragPick): picking
    /// comes on keeping what was selected, the item's tick is turned, and what is left is the selection.
    /// </summary>
    public static void CtrlClick(IDragPicker page, object item)
    {
        Assert.True(page.BeginDragPick(keepSelected: true));
        page.SetPicked(item, page.IsPicked(item) != true);
        page.EndPick();
    }

    /// <summary>Whether an item of that id lies in the shared stash, on any page.</summary>
    public static bool InSharedStash(Program program, uint id) =>
        Section(program).Pages!.Where(page => page.Grid is { Shared: true })
            .Any(page => page.Grid!.Items.Any(tile => tile.Item.Id == id));

    /// <summary>
    /// Both vault pages as a test found them: at the vault's top, nothing selected, carried or picked, no
    /// picker open. For a test that only walks the vault - one that changes what is saved has
    /// the vault opened again before the next (Program.Ready).
    /// </summary>
    public static void Rest(Program program)
    {
        foreach (var vault in new[] { program.Main.Vault!, program.Main.RightVault! })
        {
            if (vault.IsCarrying)
                vault.EndCarry();
            vault.IsPickerOpen = false;
            vault.ClearSelection();
            vault.DisarmDelete();
            // Home from anywhere: a folder gone to from the search opens the vault with no Show, and WasShown alone
            // left it there - the next test found Charms\Grand where it looked for the vault's top.
            if (vault.WasShown || vault.Place is not null)
                vault.GoTo(null);
        }
    }

    /// <summary>The first cell of a container nothing lies on, with room for an item so wide and so tall: where a drag lets go.</summary>
    public static GridCell FreeCell(Program program, string container, int wide = 1, int tall = 1)
    {
        var grid = Choice(program, container).Grid!;
        var taken = new HashSet<(int, int)>();
        foreach (var tile in grid.Items)
        {
            for (int x = 0; x < tile.View.GridWidth; x++)
            {
                for (int y = 0; y < tile.View.GridHeight; y++)
                    taken.Add((tile.CellX + x, tile.CellY + y));
            }
        }

        bool Free(GridCell cell)
        {
            for (int x = 0; x < wide; x++)
            {
                for (int y = 0; y < tall; y++)
                {
                    if (cell.Column + x >= grid.Columns || cell.Row + y >= grid.Rows || taken.Contains((cell.Column + x, cell.Row + y)))
                        return false;
                }
            }

            return true;
        }

        return grid.Cells.First(Free);
    }
}
