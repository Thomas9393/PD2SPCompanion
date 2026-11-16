using D2SLib.Model.Save;

using PD2SPCompanion.Core.Characters;
using PD2SPCompanion.Core.Collection;
using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Edit;

/// <summary>
/// What the tests of the Edit page ask of the program again and again, built from the fixture's own members:
/// the page itself, an item of the pad found by its name and selected as a click selects it, the wait for a
/// change to be the item's, a play mode worn for one test, an item made as the command line tool's
/// <c>give</c> makes it, and the vault's Edit page with an item on it put there as <c>vault --add</c> puts it. Everything but the waits
/// (<see cref="Settled"/>, <see cref="Turned"/>), <see cref="InMode"/> and <see cref="OnTheVaultsEditPage"/> is asked on the
/// program's thread.
/// </summary>
internal static class EditPad
{
    /// <summary>The Edit page beside the inventory: the character's item panel.</summary>
    public static VP_ViewModel_Inv_R EditPage(this Program program) => program.Get<VP_ViewModel_Inv_R>();

    /// <summary>The Edit page beside the vault: the same panel over the vault's one selected card.</summary>
    public static VP_ViewModel_Inv_R VaultEditPage(this Program program) => program.Get<VP_ViewModel_Vault_R>().Editor;

    /// <summary>The one section of the Items page, whose panes pick any container.</summary>
    public static ContainerSectionViewModel Section(this Program program) =>
        program.Main.Inventory!.Sections.First(section => section.Pages is not null);

    public static StashPageChoice Choice(this Program program, string label) =>
        program.Section().Pages!.First(page => page.Label == label);

    /// <summary>The first pane on one container and, where one is named, the second beside it on another.</summary>
    public static void Panes(this Program program, string first, string? second = null)
    {
        var section = program.Section();
        section.SelectedPage = program.Choice(first);
        section.SecondShown = second is not null;
        if (second is not null)
            section.SecondPage = program.Choice(second);
    }

    /// <summary>Every tile of an item the character or the shared stash holds: worn first, then the containers in the picker's order.</summary>
    public static IEnumerable<ItemTileViewModel> Tiles(this Program program) =>
        program.Main.Session.Equipped.Where(slot => !slot.IsSwap).Select(slot => slot.Item).OfType<ItemTileViewModel>()
            .Concat(program.Section().Pages!.Where(page => page.Grid is not null).SelectMany(page => page.Grid!.Items));

    /// <summary>An item's tile by the item's name - in one container where that is named, else the first there is, a worn one before any other.</summary>
    public static ItemTileViewModel Tile(this Program program, string name, string? container = null) =>
        container is null
            ? program.Tiles().First(tile => tile.Name == name)
            : program.Choice(container).Grid!.Items.First(tile => tile.Name == name);

    /// <summary>The tile an item has now: a move or a gem turned out builds the tiles again, over the same items.</summary>
    public static ItemTileViewModel TileOf(this Program program, Item item) =>
        program.Tiles().First(tile => ReferenceEquals(tile.Item, item));

    /// <summary>
    /// The tile on a container's cell. A rune or gem out of a socket is a new item there: Season 14's game writes a
    /// socketed one compact and a loose one with a quantity (ItemForge.Loose), so the socketed item is not what lands.
    /// </summary>
    public static ItemTileViewModel TileOn(this Program program, string container, GridCell cell) =>
        program.Choice(container).Grid!.Items.First(tile => tile.CellX == cell.Column && tile.CellY == cell.Row);

    public static bool Holds(this Program program, string name, string container) =>
        program.Choice(container).Grid!.Items.Any(tile => tile.Name == name);

    public static int CountIn(this Program program, string container) => program.Choice(container).Grid!.Items.Count;

    /// <summary>An empty cell of a container's grid, as a pointer over it finds one.</summary>
    public static GridCell FreeCell(this Program program, string container)
    {
        var grid = program.Choice(container).Grid!;
        return grid.Cells.First(cell => !grid.Items.Any(tile =>
        {
            var (wide, tall) = ItemMover.Footprint(tile.Item);
            return cell.Column >= tile.Item.X && cell.Column < tile.Item.X + wide
                && cell.Row >= tile.Item.Y && cell.Row < tile.Item.Y + tall;
        }));
    }

    public static EquipSlotViewModel Worn(this Program program, ItemLocation at) =>
        program.Main.Session.Equipped.First(slot => slot.Slot.Location == at && !slot.IsSwap);

    /// <summary>An item selected, as a click on its tile selects it: the Edit page then shows it. The item itself is handed back.</summary>
    public static Item Select(this Program program, string name, string? container = null)
    {
        var tile = program.Tile(name, container);
        program.Main.Session.SelectedItem = tile;
        return tile.Item;
    }

    /// <summary>Nothing selected, as a click on an empty cell leaves it: how a test that only looked leaves the page.</summary>
    public static void Deselect(this Program program) => program.Main.Session.SelectedItem = null;

    /// <summary>
    /// Waited for: nothing of the page is waiting to be written. A number typed tells the page a moment after,
    /// and what a picker staged is kept a moment after it closes - both posted to the program's own thread.
    /// </summary>
    public static void Settled(this Program program, VP_ViewModel_Inv_R? page = null)
    {
        Assert.True(program.Until(() => !(page ?? program.EditPage()).HasChanges), "the change was not kept");
    }

    /// <summary>
    /// Waited for: the program has had its turn - everything it had posted to itself has run. For proving that
    /// something did not happen: a number left unfinished is not written, a gem with nowhere to go stays.
    /// </summary>
    public static void Turned(this Program program)
    {
        bool turned = false;
        program.On(() => Avalonia.Threading.Dispatcher.UIThread.Post(() => turned = true, Avalonia.Threading.DispatcherPriority.Background));
        Assert.True(program.Until(() => turned), "the program did not take its turn");
    }

    /// <summary>The play mode for one test, the one it found put back when the test lets go of it.</summary>
    public static IDisposable InMode(this Program program, string mode)
    {
        string was = program.On(() =>
        {
            var settings = program.Get<EditorSettings>();
            string before = settings.PlayMode;
            settings.PlayMode = mode;
            return before;
        });

        return new PutBack(() => program.On(() => program.Get<EditorSettings>().PlayMode = was));
    }

    private sealed class PutBack(Action back) : IDisposable
    {
        public void Dispose() => back();
    }

    /// <summary>
    /// A plain base with so many sockets, as <c>pd2spc give --base code:sockets</c> makes it (ItemForge.PlainBase),
    /// handed to the character where an item the program made lands: the page a pane shows, then the first room.
    /// </summary>
    public static Item GivePlain(this Program program, string code, int sockets)
    {
        var item = ItemForge.PlainBase(code, sockets, program.Main.Session.ItemEra!).Item;
        item.Id = (uint)Random.Shared.NextInt64(1, uint.MaxValue);
        Assert.Null(program.Get<ItemLanding>().Receive(item, "no room", out _));
        return item;
    }

    /// <summary>What the page's legitimacy check says of the item it shows: nothing, for an item the game could have made.</summary>
    public static List<string> Findings(this VP_ViewModel_Inv_R page) => [.. page.Findings.Select(row => row.Said)];

    /// <summary>How many of the item's sockets hold something.</summary>
    public static int Filled(this Item item) => item.SocketedItems.Count;

    /// <summary>A stat of the item's own list, by the name the tables give it; null where the item has none.</summary>
    public static int? StatOf(this Item item, string name) =>
        item.StatLists.FirstOrDefault()?.Stats.FirstOrDefault(stat => stat.Stat == name)?.Value;

    /// <summary>Every value an item holds of a stat, in whichever of its lists.</summary>
    public static List<int> Every(this Item item, string name) =>
        [.. item.StatLists.SelectMany(list => list.Stats).Where(stat => stat.Stat == name).Select(stat => stat.Value)];

    /// <summary>The item's summary over the page, line by line, as its holder draws it - the words the game's tooltip has.</summary>
    public static List<string> Lines(this VP_ViewModel_Inv_R page) => [.. page.Summary!.Tooltip.Select(line => line.Text)];

    /// <summary>The page's row of a stat, by the name the tables give it - the first, where a set or a runeword lists it again.</summary>
    public static StatRowViewModel Row(this VP_ViewModel_Inv_R page, string name) => page.Stats.First(row => row.Name == name);

    // ---- the vault's Edit page ---------------------------------------------------------------------------

    /// <summary>A vault page's cards, its sheet read as the window reads it (VaultHands.CardsOf).</summary>
    public static IReadOnlyList<ItemTileViewModel> Cards(this VP_ViewModel_Vault_L vault) => Vault.VaultHands.CardsOf(vault);

    /// <summary>
    /// A plain base put in the vault as <c>pd2spc vault --add</c> puts one (Vault.Add, the vault's own deposit),
    /// and the vault then shown on the left page with that card selected: the vault's Edit page shows it. What
    /// the old scripts did with a carrier character before the program started.
    /// <para>
    /// Not stored from the character with the page's own Store: the deposit is all these tests need. An edit
    /// after a storing - once no step of the history, Undo taking back the storing instead - is
    /// VaultEditUndoTests' own subject.
    /// </para>
    /// </summary>
    public static void OnTheVaultsEditPage(this Program program, string code, int sockets, string name)
    {
        var main = program.Main;
        program.On(() =>
        {
            var vaults = program.Get<VaultService>();
            var item = ItemForge.PlainBase(code, sockets, main.Session.ItemEra!).Item;
            item.Id = (uint)Random.Shared.NextInt64(1, uint.MaxValue);
            Assert.NotNull(vaults.Store!.Add(item, vaults.CurrentFolder, out _));
            vaults.SayChanged();
        });

        program.ShowOnTheVaultsEditPage(name);
    }

    /// <summary>The vault shown on the left page with the card of that name selected: the vault's Edit page, on the right, shows it.</summary>
    public static void ShowOnTheVaultsEditPage(this Program program, string name)
    {
        var main = program.Main;
        program.On(() =>
        {
            main.ShowLeftCommand.Execute(MainViewModel.VaultMark);
            main.ShowRightCommand.Execute(MainViewModel.EditMark);
            main.Vault!.Show();
        });
        Assert.True(program.Until(() => main.Vault!.Cards().Any(card => card.Name == name)), $"{name}'s card was not listed");

        program.On(() => main.Vault!.SelectOnly(main.Vault.Cards().First(card => card.Name == name)));
        Assert.True(program.Until(() => program.VaultEditPage().Session.SelectedItem?.Name == name), $"{name} is not on the vault's Edit page");
    }

    /// <summary>
    /// The item on the vault's Edit page as the vault's file has it: its row, and the item read from the row -
    /// not the one the page holds.
    /// </summary>
    public static (VaultRow Row, Item Item) Stored(this Program program)
    {
        var vaults = program.Get<VaultService>();
        var row = vaults.Store!.RowsOf([vaults.Chosen!.Key])[0];
        return (row, ItemBlob.Restore(row.Blob, vaults.Store.StatIdBits));
    }
}
