using PD2SPCompanion.Core.Collection;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.ViewModels;

using Store = PD2SPCompanion.Core.Collection.Vault;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// The vault's two pages as a test of them opens and leaves them: built from the program's own members, for
/// the tests of this folder. A test opens the vault where it wants it (<see cref="OpenLeft"/>,
/// <see cref="OpenBoth"/>) - both pages then at the vault's top, having been nowhere - and puts everything
/// back when it is over (<see cref="PutAway"/>, from the test class's Dispose): the book on the inventory and
/// Edit, the pages' looks, the sort order, the play mode, no search and nothing selected.
/// <para>
/// Everything here but the two openers, <see cref="Until"/> and <see cref="PutAway"/> is asked on the
/// program's thread, inside <c>program.On</c>.
/// </para>
/// </summary>
internal sealed class VaultBook
{
    private readonly Program _program;
    private readonly PlayPolicy? _policy;
    private readonly string _customName = "", _sortBy = "", _vaultName = "";
    private readonly bool _sortDescending;
    private readonly List<string> _customs = [];

    public VaultBook(Program program)
    {
        _program = program;
        if (!program.Available)
            return;

        // As the test finds them: put back by PutAway.
        (_policy, _customName, _sortBy, _sortDescending, _vaultName, _customs) = program.On(() =>
            (Settings.Policy, Settings.CustomName, Left.Order.By, Left.Order.Descending, Vaults.VaultName,
             Settings.CustomPresets.Select(kept => kept.Name).ToList()));
    }

    public MainViewModel Main => _program.Main;

    /// <summary>The left vault page: the first of the two.</summary>
    public VP_ViewModel_Vault_L Left => Main.Vault!;

    /// <summary>The right vault page: beside the inventory, or beside the left one.</summary>
    public VP_ViewModel_Vault_L Right => Main.RightVault!;

    public CharacterSession Session => Main.Session;

    public VaultService Vaults => _program.Get<VaultService>();

    public EditorSettings Settings => _program.Get<EditorSettings>();

    /// <summary>The open vault itself: what a test reads back from.</summary>
    public Store Store => Vaults.Store!;

    /// <summary>A vault page's cards, its sheet read as the window reads it (<see cref="VaultHands.CardsOf"/>).</summary>
    public static IReadOnlyList<ItemTileViewModel> CardsOf(VP_ViewModel_Vault_L page) => VaultHands.CardsOf(page);

    public static ItemTileViewModel Card(VP_ViewModel_Vault_L page, string name) => CardsOf(page).First(card => card.Name == name);

    public static bool HasCard(VP_ViewModel_Vault_L page, string name) => CardsOf(page).Any(card => card.Name == name);

    /// <summary>A folder's row in a page's tree, by its name.</summary>
    public static FolderPlace Row(VP_ViewModel_Vault_L page, string name) => page.Places.First(place => place.Name == name);

    /// <summary>A folder's lock put on or taken off without opening it: by its menu's row - Lock or Unlock, whichever it has.</summary>
    public static void ToggleLock(VP_ViewModel_Vault_L page, string name)
    {
        var row = Row(page, name);
        page.MenuFor(row.Id).Pick(row.IsLocked ? "Unlock" : "Lock");
    }

    /// <summary>Where a page is: the whole way to its folder, as the path over it says.</summary>
    public static string Shown(VP_ViewModel_Vault_L page) => page.Place?.Path ?? "";

    /// <summary>The first stretch of a page's sheet: its grid, as wide as the sheet, and the items read onto it.</summary>
    public static VaultPageViewModel Sheet(VP_ViewModel_Vault_L page) => page.PageRows[0].Pages[0];

    /// <summary>An item's row as the vault holds it, by its name.</summary>
    public VaultRow Held(string name) => Store.Page(VaultQuery.Everything, "Name", false, 0, 1000).First(row => row.Name == name);

    /// <summary>How many items lie in a folder, by the folder's name.</summary>
    public int Holds(string folder) => Store.Count(Store.Folders().First(each => each.Name == folder).Id);

    /// <summary>A page gone to a folder by its row in the tree, as a click on the row goes.</summary>
    public static void GoTo(VP_ViewModel_Vault_L page, string folder) => page.GoToPlace(Row(page, folder));

    /// <summary>The vault on the left page, Edit beside it: both vault pages at the top, having been nowhere.</summary>
    public void OpenLeft()
    {
        _program.On(() =>
        {
            Calm();
            Main.ShowLeftCommand.Execute(MainViewModel.VaultMark);

            // Show: what the view calls as the page comes into sight.
            Left.Show();
            Assert.True(CardsOf(Left).Count > 0, "the left vault page's cards were not listed");
        });
    }

    /// <summary>The vault on both pages, as the left page's Compare button puts it.</summary>
    public void OpenBoth()
    {
        OpenLeft();
        _program.On(() =>
        {
            Left.ToggleCompareCommand.Execute(null);
            Right.Show();
            Assert.True(Main.IsComparing, "the vault is not on both pages");
            Assert.True(CardsOf(Right).Count > 0, "the right vault page's cards were not listed");
        });
    }

    /// <summary>
    /// The two pages as a test of them begins, and as it leaves them for the next test, anyone's: nothing
    /// carried, picked, selected or open on either, their looks as a new install has them - and both at the
    /// vault's top, their ways and their folds forgotten, as another vault opened leaves them, which is the one
    /// way they forget (nothing is asked again where they are so already).
    /// </summary>
    private void Calm()
    {
        foreach (var page in new[] { Left, Right })
        {
            page.EndCarry();
            page.IsMenuOpen = false;
            page.IsPickerOpen = false;
            page.DisarmDelete();
            foreach (var naming in page.SheetFolders.Where(tile => tile.IsRenaming).ToList())
                page.CancelRename(naming.Rename!);

            page.ClearSelection();
            page.Notice = "";
            page.RoomHidesMap = page.RoomHidesTree = false;
            page.TreeDraggedShut = false;
            page.ShowsTree = page.IsMinimap = true;
            page.TreeWidth = VaultLook.DefaultTreeWidth;
        }

        // Unfolded is what a test may have left: folded is how a folder is made.
        if (new[] { Left, Right }.Any(page => page.Here is not null || page.CanGoBack || page.CanGoForward
                                              || page.Places.Any(place => place.HoldsFolders && place.IsOpen)))
            Vaults.Reopened();
    }

    /// <summary>Waited for on the program's thread, the test failing where it never comes.</summary>
    public void Until(Func<bool> so, string what, int milliseconds = 5000) =>
        Assert.True(_program.Until(so, milliseconds), what);

    /// <summary>
    /// Everything a test of the vault's pages may have left, put back: the search ended, nothing carried,
    /// picked or open, the pages' looks as a new install has them, the sort order and the play mode as they
    /// were, the first vault open, both pages at the vault's top, the book on the inventory and Edit.
    /// </summary>
    public void PutAway()
    {
        if (!_program.Available)
            return;

        _program.On(() =>
        {
            Main.SearchWindow!.End();

            foreach (var page in new[] { Left, Right })
                (page.Order.By, page.Order.Descending) = (_sortBy, _sortDescending);

            if (Settings.Policy != _policy)
                Settings.ChoosePolicy(_policy!, _customName);
            if (Settings.CustomName != _customName)
                Settings.CustomName = _customName;

            // A custom mode made by the test goes, now that none of them is the mode.
            foreach (var made in Settings.CustomPresets.Where(kept => !_customs.Contains(kept.Name)).ToList())
                Settings.ForgetCustom(made);

            if (Vaults.VaultName != _vaultName && Vaults.Vaults().Any(each => each.Name == _vaultName))
                Vaults.SwitchTo(_vaultName);

            Calm();
            Main.ShowPageByKey("Inventory_L");
            Main.ShowPageByKey("Inventory_R");
        });
    }
}
