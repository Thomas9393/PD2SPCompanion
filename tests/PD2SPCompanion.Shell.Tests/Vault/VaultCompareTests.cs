using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// The vault on both pages of the book:
/// the left page's Compare puts the vault on the right page too, the right Vault
/// bookmark the same by hand; each page is its own - its folder, its ways back, its count, its selection, its
/// strip of messages; and what is carried from one is let go of on the other's sheet or tree.
/// <para>
/// What the two pages and the book decide, asked as the views ask. Which half of the window a page, its
/// Compare button and its strip are drawn on, that a drag crosses from one sheet to the other with the item
/// drawn under the pointer, and that the keys are the page's that was pressed in last, are the UI cases' to show
/// (units/vault-compare.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultCompareTests(Program program) : IDisposable
{
    private readonly VaultBook _book = new(program);

    private MainViewModel Main => program.Main;
    private VP_ViewModel_Vault_L Left => _book.Left;
    private VP_ViewModel_Vault_L Right => _book.Right;

    private const string Charm = "Small Charm of Fortune";

    public void Dispose() => _book.PutAway();

    [CorpusFact]
    public void Compare_puts_the_vault_on_the_right_page_too_and_again_gives_the_page_back_what_it_showed()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            // The grail beside the vault; the right Vault bookmark is there to press.
            Main.ShowRightCommand.Execute(MainViewModel.GrailMark);
            Assert.True(Main.CanRightVault);
            Assert.True(Main.IsRightGrail);
            Assert.False(Left.IsComparing);

            Left.ToggleCompareCommand.Execute(null);
            Assert.True(Main.IsComparing);
            Assert.True(Left.IsComparing);
            Assert.Same(Right, Main.CurrentPage_R);

            // The Compare button is the left page's alone: the first of the two.
            Assert.True(Left.IsPrimary);
            Assert.False(Right.IsPrimary);

            Right.Show();
            VaultBook.GoTo(Right, "Grand");

            Left.ToggleCompareCommand.Execute(null);
            Assert.False(Main.IsComparing);
            Assert.True(Main.IsRightGrail);

            // The bookmark by hand: the vault on both pages, as Compare puts it - the right page where it was left.
            Main.ShowRightCommand.Execute(MainViewModel.VaultMark);
            Assert.True(Main.IsComparing);
            Assert.True(Left.IsComparing);
            Assert.Equal(@"Vault\Charms\Grand", VaultBook.Shown(Right));
        });
    }

    [CorpusFact]
    public void A_vault_page_in_sight_for_the_first_time_opens_where_the_other_is_and_goes_its_own_way_from_there()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            VaultBook.GoTo(Left, "Small");

            // A right page never yet shown - the program's own may have been, by a test before this one: made
            // as the program makes it, and told what the book tells it before it comes into sight.
            var right = new VP_ViewModel_Vault_L(
                program.Get<CharacterSession>(), program.Get<EditorSettings>(), program.Get<ISFXManager>(),
                program.Get<ConfirmService>(), program.Get<VaultService>(), program.Get<SharedStashSession>(),
                program.Get<PlayRules>(), program.Get<VaultMoves>(), program.Get<ItemLanding>(), program.Get<GameSync>())
            {
                PageName = VP_ViewModel_Vault_L.RightPage,
                Twin = Left,
                TakesArrivals = () => false,
            };

            try
            {
                Assert.False(right.WasShown);
                right.OpenWhereTwinIs();
                right.Show();
                Assert.Equal(@"Vault\Charms\Small", VaultBook.Shown(right));

                // Shown once, it is its own: the left going elsewhere takes it nowhere.
                VaultBook.GoTo(Left, "Jewels");
                right.OpenWhereTwinIs();
                Assert.Equal(@"Vault\Charms\Small", VaultBook.Shown(right));

                VaultBook.GoTo(right, "Grand");
                Assert.Equal(@"Vault\Charms\Grand", VaultBook.Shown(right));
                Assert.Equal(@"Vault\Jewels", VaultBook.Shown(Left));
            }
            finally
            {
                // It listens to the vault for as long as the program runs: left at the top, where nothing reaches it.
                right.GoTo(null);
            }
        });
    }

    [CorpusFact]
    public void Each_page_shows_and_counts_its_own_folder_and_goes_back_its_own_way()
    {
        if (!program.Ready())
            return;

        _book.OpenBoth();
        program.On(() =>
        {
            VaultBook.GoTo(Left, "Small");
            VaultBook.GoTo(Right, "Small");
            VaultBook.GoTo(Right, "Grand");

            Assert.Equal(@"Vault\Charms\Grand", VaultBook.Shown(Right));
            Assert.Equal(@"Vault\Charms\Small", VaultBook.Shown(Left));
            Assert.Equal("2 items", Left.PagesText);
            Assert.Equal("1 item", Right.PagesText);

            Right.GoBackCommand.Execute(null);
            Assert.Equal(@"Vault\Charms\Small", VaultBook.Shown(Right));
            Assert.Equal(@"Vault\Charms\Small", VaultBook.Shown(Left));

            // The left page has been to one folder only: the right page's ways are not its.
            Assert.False(Left.CanGoForward);
            Assert.True(Right.CanGoForward);
        });
    }

    [CorpusFact]
    public void A_card_carried_from_one_page_and_let_go_of_on_the_others_sheet_comes_into_that_pages_folder()
    {
        if (!program.Ready(changes: true))
            return;

        _book.OpenBoth();
        program.On(() =>
        {
            VaultBook.GoTo(Left, "Small");
            VaultBook.GoTo(Right, "Grand");

            Assert.True(Left.BeginCarry(VaultBook.Card(Left, Charm)));
            Assert.True(Right.IsCarriedOver);

            // Over the other page's sheet it would come into that folder - the sheet is framed; over its own, not.
            Right.HoverSheet(true);
            Assert.True(Right.IsSheetTarget);
            Left.HoverSheet(true);
            Assert.False(Left.IsSheetTarget);

            Right.DropCarriedOnSheet();
            Assert.False(Right.IsSheetTarget);

            Assert.False(Left.IsCarrying);
            Assert.Equal("1 item", Left.PagesText);
            Assert.Equal("2 items", Right.PagesText);

            // In the right page's folder.
            var row = _book.Held(Charm);
            Assert.Equal(_book.Store.Folders().First(folder => folder.Name == "Grand").Id, row.FolderId);
            Assert.True(VaultBook.HasCard(Right, Charm));
            Assert.False(VaultBook.HasCard(Left, Charm));

            // Both trees count it so.
            Assert.Equal("2", VaultBook.Row(Left, "Grand").ItemsText);
            Assert.Equal("1", VaultBook.Row(Right, "Small").ItemsText);
        });
    }

    [CorpusFact]
    public void A_card_carried_from_the_right_page_and_let_go_of_on_a_row_of_the_left_pages_tree_moves_into_that_folder()
    {
        if (!program.Ready(changes: true))
            return;

        _book.OpenBoth();
        program.On(() =>
        {
            VaultBook.GoTo(Left, "Small");
            VaultBook.GoTo(Right, "Small");

            Assert.True(Right.BeginCarry(VaultBook.Card(Right, Charm)));

            // What the window draws under the pointer on the way: the right page's card.
            Assert.True(Main.IsVaultCarrying);
            Assert.Equal(Charm, Assert.Single(Main.VaultGhostParts).Tile.Name);

            Left.DropCarriedOnFolder(VaultBook.Row(Left, "Jewels"));

            Assert.False(Main.IsVaultCarrying);
            Assert.Equal("1 item", Right.PagesText);
            Assert.Equal("1 item", Left.PagesText);
            Assert.Equal("2", VaultBook.Row(Left, "Jewels").ItemsText);
            Assert.Equal("2", VaultBook.Row(Right, "Jewels").ItemsText);
        });
    }

    [CorpusFact]
    public void An_arrow_selects_on_the_page_it_is_given_to_and_a_copy_made_there_is_said_on_that_pages_strip()
    {
        if (!program.Ready(changes: true))
            return;

        _book.OpenBoth();
        program.On(() =>
        {
            // A copy is the Free mode's.
            _book.Settings.ChoosePolicy(PlayPolicy.Free);
            VaultBook.GoTo(Right, "Jewels");
            VaultBook.CardsOf(Right);

            // Nothing being selected, the sheet's first - the folder's one folder, a tile of it - and
            // beside that, its first item.
            Right.StepSelection(1, 0);
            Assert.Equal("Magic", Right.SelectedFolder?.Name);
            Right.StepSelection(1, 0);
            Assert.Equal("Sharp Large Charm of Venom", Right.Selected?.Name);
            Assert.True(Right.CanDeleteSelection);
            Assert.Null(Left.Selected);
            Assert.False(Left.CanDeleteSelection);

            string left = _book.Session.Messages.Text;
            Right.Copy();
            Right.PasteCommand.Execute(null);

            Assert.Equal("2 items", Right.PagesText);
            Assert.Equal(2, _book.Holds("Jewels"));
            Assert.Equal("Copied Sharp Large Charm of Venom.", _book.Session.RightMessages.Text);
            Assert.Equal(left, _book.Session.Messages.Text);
        });
    }
}
