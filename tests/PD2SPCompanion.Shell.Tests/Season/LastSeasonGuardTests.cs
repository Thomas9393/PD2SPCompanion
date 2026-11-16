using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Season;

/// <summary>
/// A shared stash still in last season's encoding - as on a new season's first launch - opens, and takes
/// nothing in from elsewhere until the game has rewritten it:
/// a vault card let go of on one of its pages is refused, with the
/// reason, the card still in the vault and nothing written; the same card let go of on the character - this
/// season's - is taken.
/// <para>
/// The stash is the pad's own, written again in last season's encoding as the command line tool's
/// <c>lastseason</c> writes it, and read by the program as it reads a file the game saved. The drop is asked
/// of the pages as the view asks it once a carried card is let go of on a cell (BeginCarry, DropFromVault);
/// that a drag of the mouse carries a card there is the UI case's to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class LastSeasonGuardTests(Program program)
{
    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private SharedStashSession Stash => program.Get<SharedStashSession>();
    private VP_ViewModel_Inv_L Page => Main.Inventory!;
    private VP_ViewModel_Vault_L Vault => Main.RightVault!;

    private string StashFile => Path.Combine(program.Folder, "saves", SharedStashSession.SoftcoreFile);

    private ContainerSectionViewModel Section => Page.Sections.First(section => section.Pages is not null);

    private StashPageChoice Choice(string label) => Section.Pages!.First(page => page.Label == label);

    private int InVault => program.Get<VaultService>().Store!.Count();

    private IReadOnlyList<ItemTileViewModel> Cards()
    {
        foreach (var row in Vault.PageRows.ToList())
            _ = row.Pages;

        return Vault.Cards;
    }

    private ItemTileViewModel Tome => Cards().First(card => card.Name == "Tome of Town Portal");

    /// <summary>The tomes in the character's inventory: it carries one of its own already.</summary>
    private int Tomes() => Choice("Inventory").Grid!.Items.Count(tile => tile.Name == "Tome of Town Portal");

    /// <summary>The first cell of a grid with room for an item of that size.</summary>
    private static GridCell FreeCell(GridViewModel grid, int width, int height)
    {
        bool Taken(int column, int row) => grid.Items.Any(tile =>
            column < tile.CellX + tile.View.GridWidth && tile.CellX < column + width
            && row < tile.CellY + tile.View.GridHeight && tile.CellY < row + height);

        for (int row = 0; row + height <= grid.Rows; row++)
        {
            for (int column = 0; column + width <= grid.Columns; column++)
            {
                if (!Taken(column, row))
                    return new GridCell(grid.Page, column, row, grid.Shared);
            }
        }

        throw new InvalidOperationException($"{grid.Name} has no room.");
    }

    [CorpusFact]
    public void A_last_season_stash_opens_and_refuses_a_vault_card_which_this_seasons_character_takes()
    {
        if (!program.Ready(changes: true))
            return;

        // A new season's first launch, its stamp not yet in the table (SeasonSnapshot.KnownStamps): the file is told by
        // plausibility. With the stamps known, the reader goes by each item's own and this reads as a mix.
        var stamps = (SeasonReading.CurrentStamp, SeasonReading.PreviousStamp);
        SeasonReading.SetStamps(null, null);

        try
        {
            program.On(() =>
            {
                // The stash as last season's game wrote it, and noticed as a file the game saved.
                byte[] before = SharedStash.Read(StashFile).Write(lastSeason: true);
                Session.UseMyLayout();
                File.WriteAllBytes(StashFile, before);
                Main.Sync!.Check();
                Assert.Equal("Shared stash reloaded. Saved in game.", Session.Status);

                Assert.True(Stash.IsLoaded, "the last-season stash did not open");
                Assert.True(Stash.IsLastSeason);
                Assert.False(Session.IsLastSeason);

                // The vault on the right page, a shared page in the second pane.
                Main.ShowRightCommand.Execute(MainViewModel.VaultMark);
                Vault.Show();
                Section.SecondShown = true;
                Section.SecondPage = Choice("Shared Page 5");
                Assert.Empty(Choice("Shared Page 5").Grid!.Items);
            });
            Assert.True(program.Until(() => Cards().Any(card => card.Name == "Tome of Town Portal")), "the tome's card was not listed");

            program.On(() =>
            {
                int stored = InVault;
                byte[] before = File.ReadAllBytes(StashFile);

                // Vault -> the last-season stash: refused.
                Assert.True(Vault.BeginCarry(Tome));
                Assert.True(Page.IsVaultCarrying);
                Page.DropFromVault(FreeCell(Choice("Shared Page 5").Grid!, 1, 2));

                // Why, and nothing before it of what did not move (the character carries a tome of that id too).
                Assert.Equal("1 already held. Shared stash read as last season's. Require one character of this season opened.", Vault.Notice);
                Assert.False(Vault.IsCarrying);
                Assert.Equal(stored, InVault);
                Assert.Contains(Cards(), card => card.Name == "Tome of Town Portal");
                Assert.Empty(Choice("Shared Page 5").Grid!.Items);

                program.Get<AutoSave>().Flush();
                Assert.Equal(before, File.ReadAllBytes(StashFile));
            });

            // The control: the same card onto the character, this season's - taken.
            int kept = 0, carried = 0;
            program.On(() =>
            {
                kept = InVault;
                carried = Tomes();
                Assert.True(Vault.BeginCarry(Tome));
                Page.DropFromVault(FreeCell(Choice("Inventory").Grid!, 1, 2));
            });
            Assert.True(program.Until(() => InVault == kept - 1), "the tome was not taken by the character");
            program.On(() => Assert.Equal(carried + 1, Tomes()));
        }
        finally
        {
            SeasonReading.SetStamps(stamps.CurrentStamp, stamps.PreviousStamp);
            program.On(() =>
            {
                Vault.EndCarry();
                Section.SecondShown = false;
            });
        }
    }
}
