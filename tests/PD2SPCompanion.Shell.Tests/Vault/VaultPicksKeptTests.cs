using PD2SPCompanion.Shell.Tests.Inventory;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// The vault keeps what is picked through a reload of its folder, as the inventory keeps its picks:
/// a copy pasted, a sort, a search's change, an item stored from the character read the folder again,
/// and the picks went with the old cards. What is no longer in the folder is no longer picked.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultPicksKeptTests(Program program) : IDisposable
{
    private readonly VaultBook _book = new(program);

    private VP_ViewModel_Vault_L Page => _book.Left;

    private const string Fortune = "Small Charm of Fortune", Garnet = "Garnet Small Charm of Life";

    /// <summary>A ring of the pad's personal stash.</summary>
    private const string Ring = "Steel Ring of Skill";

    public void Dispose() => _book.PutAway();

    [CorpusFact]
    public void Two_picked_stay_picked_after_a_copy_pasted_and_a_sort()
    {
        if (!program.Ready(changes: true))
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            VaultBook.GoTo(Page, "Small");
            VaultHands.CtrlClick(Page, VaultBook.Card(Page, Fortune));
            VaultHands.CtrlClick(Page, VaultBook.Card(Page, Garnet));
            Assert.True(Page.IsPicking);
            Assert.Equal(2, Page.PickedCount);

            // A copy pasted reads the folder again: the two picked are picked still, their copies not.
            Page.Copy();
            Page.PasteCommand.Execute(null);
            Assert.True(Page.IsPicking);
            Assert.Equal(2, Page.PickedCount);
            Assert.Equal(2, VaultBook.CardsOf(Page).Count(card => card.IsPicked && card.Name is Fortune or Garnet));

            // A sort reads it again too.
            Page.Order.Pick(Page.Order.Options.First(option => option.Label == "Name"));
            Assert.True(Page.IsPicking);
            Assert.Equal(2, Page.PickedCount);
        });
    }

    /// <summary>
    /// The Search page beside the vault, and a condition of it changed as its own boxes change one (the keyword's
    /// box is the filter's Name): the vault's folder is read again under the search, and again as it changes. A
    /// search begun anew - the quick box's text sent over, the conditions emptied by their brush - is another
    /// matter: that lets the selection go, as it always did (VaultFilter.Cleared).
    /// </summary>
    [CorpusFact]
    public void Two_picked_stay_picked_as_a_search_changes_under_them()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            VaultBook.GoTo(Page, "Small");
            _book.Main.ShowSearch();
            VaultHands.CtrlClick(Page, VaultBook.Card(Page, Fortune));
            VaultHands.CtrlClick(Page, VaultBook.Card(Page, Garnet));
            Assert.Equal(2, Page.PickedCount);

            _book.Vaults.Filter.Name = "fortune";
        });
        _book.Until(() => Page.PagesText.Contains("1 found"), "the page did not read its folder again under the search");

        program.On(() =>
        {
            // Both picked still, the find and the miss alike.
            Assert.Equal("2 items · 1 found · 2 selected", Page.PagesText);
            Assert.True(VaultBook.Card(Page, Fortune).IsPicked);
            Assert.True(VaultBook.Card(Page, Garnet).IsPicked);

            _book.Vaults.Filter.Name = "charm";
        });
        _book.Until(() => Page.PagesText.Contains("2 found"), "the page did not read its folder again as the search changed");

        program.On(() =>
        {
            Assert.Equal("2 items · 2 found · 2 selected", Page.PagesText);
            Assert.True(VaultBook.Card(Page, Fortune).IsPicked);
            Assert.True(VaultBook.Card(Page, Garnet).IsPicked);
        });
    }

    [CorpusFact]
    public void Picks_stay_as_an_item_is_stored_from_the_character_and_one_that_leaves_the_folder_is_picked_no_more()
    {
        if (!program.Ready(changes: true))
            return;

        var right = program.On(() =>
        {
            program.Panes("Personal Stash");
            var vault = VaultHands.OnRight(program);
            VaultHands.GoTo(vault, "Small");
            return vault;
        });
        Assert.True(program.Until(() => VaultHands.CardOrNone(right, Fortune) is not null && VaultHands.CardOrNone(right, Garnet) is not null), "Small was not opened");

        int stored = 0;
        program.On(() =>
        {
            stored = program.InVault();
            VaultHands.CtrlClick(right, VaultHands.Card(right, Fortune));
            VaultHands.CtrlClick(right, VaultHands.Card(right, Garnet));
            Assert.Equal(2, right.PickedCount);

            // A ring stored from the stash, into the folder shown: the folder is read again.
            var page = program.Page();
            page.ClickTileCommand.Execute(program.Tile(Ring, "Personal Stash"));
            Assert.True(page.CanStore);
            page.StoreToVaultCommand.Execute(null);
        });
        Assert.True(program.Until(() => program.InVault() == stored + 1 && VaultHands.CardOrNone(right, Ring) is not null), "the ring was not stored into the folder shown");

        program.On(() =>
        {
            Assert.Equal(2, right.PickedCount);
            Assert.True(VaultHands.Card(right, Fortune).IsPicked);
            Assert.True(VaultHands.Card(right, Garnet).IsPicked);
            Assert.False(VaultHands.Card(right, Ring).IsPicked);

            // One of the two moved to another folder: no pick of this one - and one left picked is the selection.
            long grand = _book.Store.Folders().First(each => each.Name == "Grand").Id;
            _book.Store.MoveItems([VaultHands.Card(right, Fortune).Key], grand);
            _book.Vaults.SayChanged();
            Assert.Null(VaultHands.CardOrNone(right, Fortune));
            Assert.False(right.IsPicking);
            Assert.Same(VaultHands.Card(right, Garnet), right.Selected);
        });
    }
}
