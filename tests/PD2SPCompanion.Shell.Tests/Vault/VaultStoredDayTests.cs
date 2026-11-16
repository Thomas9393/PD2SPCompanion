using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.Tests.Inventory;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// The day a vault item was stored, as the program has it:
/// the date of the item's stay in the vault ("Stored" on the screen,
/// <c>added_at</c> in the file). Its tooltip says it, in a section of its own over the keys; a withdrawal undone
/// leaves it as it was - the item never left; withdrawn and stored again, it is a new record, stored now. A
/// character's item says nothing of it.
/// <para>
/// What the page and the save decide, asked of them as the view asks. That the tooltip draws the line under a
/// rule, over the key hints, is a window's to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultStoredDayTests(Program program)
{
    private const string Shown = "Shared Page 5", Tome = "Tome of Town Portal";

    private static readonly DateTimeOffset LongAgo = new(2001, 2, 3, 4, 5, 6, TimeSpan.Zero);

    private MainViewModel Main => program.Main;
    private VP_ViewModel_Vault_L Vault => Main.RightVault!;
    private PD2SPCompanion.Core.Collection.Vault Store => program.Get<VaultService>().Store!;

    /// <summary>The vault on the right page, its tome listed; an empty shared page on the left for it to come to.</summary>
    private ItemTileViewModel TheTome()
    {
        program.On(() =>
        {
            Assert.Empty(program.Grid(Shown).Items);
            program.Panes(Shown);
            Main.ShowRightCommand.Execute(MainViewModel.VaultMark);
            Vault.Show();
        });
        Assert.True(program.Until(() => InventoryPad.CardsOf(Vault).Any(card => card.Name == Tome)), "the vault's cards were not listed");
        return program.On(() => InventoryPad.CardsOf(Vault).First(card => card.Name == Tome));
    }

    /// <summary>Says a row has lain in the vault since long ago - a day no storing of this run's could give.</summary>
    private void SinceLongAgo(long key) => program.On(() =>
    {
        using var write = Store.Connection.CreateCommand();
        write.CommandText = "UPDATE item SET added_at = $at WHERE key = $key;";
        write.Parameters.AddWithValue("$at", LongAgo.ToUnixTimeSeconds());
        write.Parameters.AddWithValue("$key", key);
        write.ExecuteNonQuery();
    });

    private List<DateTimeOffset> TomesAdded() =>
        program.On(() => Store.RowsOf(Store.AllKeys()).Where(row => row.Name == Tome).Select(row => row.Added).ToList());

    [CorpusFact]
    public void A_vault_items_tooltip_says_the_day_it_was_stored_and_a_characters_item_says_nothing()
    {
        if (!program.Ready())
            return;

        var card = TheTome();
        program.On(() =>
        {
            Assert.Equal(card.Row.Added, card.Added);
            Assert.Equal($"Stored {card.Row.Added.ToLocalTime():yyyy-MM-dd}", card.StoredLine);

            var held = Main.Session.AllGrids.SelectMany(grid => grid.Items).First();
            Assert.Null(held.Added);
            Assert.Equal("", held.StoredLine);
        });
    }

    [CorpusFact]
    public void A_withdrawal_undone_leaves_the_item_stored_when_it_was()
    {
        if (!program.Ready(changes: true))
            return;

        var card = TheTome();
        SinceLongAgo(card.Key);
        int stored = program.On(() => program.InVault());

        program.On(() =>
        {
            Vault.SelectOnly(InventoryPad.CardsOf(Vault).First(each => each.Key == card.Key));
            Vault.WithdrawCommand.Execute(null);
        });
        Assert.True(program.Until(() => program.InVault() == stored - 1), "the tome's row did not leave the vault");
        Assert.DoesNotContain(LongAgo, TomesAdded());

        Assert.True(program.Until(() => Main.Home.UndoCommand.CanExecute(null)), "nothing to undo");
        program.On(() => Main.Home.UndoCommand.Execute(null));
        Assert.True(program.Until(() => program.InVault() == stored), "undo did not put the row back");

        // The withdrawal did not happen: the stay it would have ended goes on.
        Assert.Contains(LongAgo, TomesAdded());
    }

    [CorpusFact]
    public void Withdrawn_and_stored_again_the_item_is_stored_anew()
    {
        if (!program.Ready(changes: true))
            return;

        var card = TheTome();
        SinceLongAgo(card.Key);
        int stored = program.On(() => program.InVault());
        var started = DateTimeOffset.UtcNow.AddSeconds(-2);

        program.On(() =>
        {
            Vault.SelectOnly(InventoryPad.CardsOf(Vault).First(each => each.Key == card.Key));
            Vault.WithdrawCommand.Execute(null);
        });
        Assert.True(program.Until(() => program.InVault() == stored - 1), "the tome's row did not leave the vault");

        // Back into the vault, off the shared page it came to.
        program.On(() =>
        {
            var page = Main.Inventory!;
            page.ClickTileCommand.Execute(program.Tile(Tome, Shown));
            Assert.True(page.CanStore);
            page.StoreToVaultCommand.Execute(null);
        });
        Assert.True(program.Until(() => program.InVault() == stored), "the tome was not stored again");

        // A new record: its stay begins now, and nothing is left of the one that ended.
        var added = TomesAdded();
        Assert.DoesNotContain(LongAgo, added);
        Assert.Contains(added, at => at >= started);
    }
}
