using PD2SPCompanion.Core.Collection;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// A vault renamed from the list on the Settings page: the open one is closed for it, moved and opened again under the
/// new name with the settings remembering it; one that is not open is only moved; a refused name, a name in use and the
/// moves of a save waiting each leave everything as it was, the box open with what was typed. Asked through the row's
/// commands, as the row's box asks.
/// <para>
/// Every test goes back to the name it found: the settings remember a vault by its name, and the fixture puts the file
/// back under the old one.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultRenameTests(Program program)
{
    private const string Renamed = "Renamed Vault", Second = "Second Vault";

    private VaultManagerViewModel Manager => program.Get<VP_ViewModel_Settings_R>().Vaults!;
    private VaultService Vaults => program.Get<VaultService>();
    private EditorSettings Settings => program.Get<EditorSettings>();
    private string Said => program.Main.Session.Status;

    private string PathOf(string name) => VaultLibrary.VaultPath(Vaults.Season!, false, name);

    private VaultListRow RowOf(string name)
    {
        Manager.Refresh();
        return Manager.Rows.First(row => row.Name == name);
    }

    /// <summary>The row's box opened, the name typed and let go of; the box closes.</summary>
    private void Rename(VaultListRow row, string to)
    {
        Typed(row, to);
        Assert.False(row.IsRenaming);
    }

    /// <summary>The row's box opened, the name typed and let go of; what the box does is the test's to read.</summary>
    private void Typed(VaultListRow row, string to)
    {
        Manager.StartRenameCommand.Execute(row);
        Assert.True(row.IsRenaming);
        Assert.Equal(row.Name, row.EditName);
        row.EditName = to;
        Manager.CommitRename(row);
    }

    /// <summary>A refused name: the box stays with what was typed, and goes when cancelled.</summary>
    private void StaysOpen(VaultListRow row, string typed)
    {
        Assert.True(row.IsRenaming);
        Assert.Equal(typed, row.EditName);
        Manager.CancelRenameCommand.Execute(row);
        Assert.False(row.IsRenaming);
    }

    /// <summary>The open vault under the name it was found with, whatever the test left.</summary>
    private void PutTheNameBack(string name)
    {
        program.On(() =>
        {
            if (Vaults.VaultName != name && File.Exists(PathOf(Vaults.VaultName)))
                Vaults.Rename(Vaults.VaultName, name);
            Assert.Equal(name, Vaults.VaultName);
        });
    }

    [CorpusFact]
    public void The_open_vault_is_moved_and_open_under_the_new_name_with_what_it_held()
    {
        if (!program.Ready(changes: true))
            return;

        string first = program.On(() => Vaults.VaultName);
        try
        {
            program.On(() =>
            {
                string was = PathOf(first);
                int held = Vaults.Store!.Count();
                Assert.True(held > 0, "the pad's vault holds nothing to keep");
                long gold = Vaults.Store.GoldBalance();

                Rename(RowOf(first), "  " + Renamed + " ");

                Assert.Equal($"Renamed to {Renamed}.", Said);
                Assert.False(File.Exists(was), "the old file is still there");
                Assert.True(File.Exists(PathOf(Renamed)));
                foreach (string beside in new[] { "-wal", "-shm" })
                    Assert.False(File.Exists(was + beside), "the old name's " + beside);

                // Open again, on the new file, with what it held.
                Assert.Equal(Renamed, Vaults.VaultName);
                Assert.NotNull(Vaults.Store);
                Assert.Equal(PathOf(Renamed), Vaults.Store!.FilePath);
                Assert.Equal(held, Vaults.Store.Count());
                Assert.Equal(gold, Vaults.Store.GoldBalance());

                // Remembered, and the list and the row's box say it.
                Assert.Equal(Renamed, Settings.VaultNameSoftcore);
                Assert.Equal(Renamed, Manager.OpenName);
                Assert.True(RowOf(Renamed).IsOpen);
                Assert.DoesNotContain(Manager.Rows, row => row.Name == first);
            });
        }
        finally
        {
            PutTheNameBack(first);
        }
    }

    [CorpusFact]
    public void A_vault_not_open_is_moved_and_the_open_one_stays_open()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            string open = Vaults.VaultName;
            var store = Vaults.Store;
            Vaults.Create(Second, null);

            Rename(RowOf(Second), Renamed);

            Assert.Equal($"Renamed to {Renamed}.", Said);
            Assert.False(File.Exists(PathOf(Second)));
            Assert.True(File.Exists(PathOf(Renamed)));
            Assert.Equal(open, Vaults.VaultName);
            Assert.Same(store, Vaults.Store);
            Assert.Equal(open, Settings.VaultNameSoftcore);
            Assert.False(RowOf(Renamed).IsOpen);
        });
    }

    [CorpusFact]
    public void A_refused_name_a_name_in_use_and_the_same_name_leave_everything_as_it_was()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            string open = Vaults.VaultName;
            var store = Vaults.Store;
            Vaults.Create(Second, null);

            void Refused(string typed, string said)
            {
                var row = RowOf(open);
                Typed(row, typed);
                Assert.Equal(said, Said);
                StaysOpen(row, typed);
                Assert.Equal(open, Vaults.VaultName);
                Assert.Same(store, Vaults.Store);
                Assert.True(File.Exists(PathOf(open)));
            }

            Refused(Second, "Name already used.");
            Refused(Second.ToUpperInvariant(), "Name already used.");
            Refused("   ", "Require name.");
            Refused("a/b", "Invalid file name.");
            Refused(new string('a', VaultLibrary.LongestName + 1), $"At most {VaultLibrary.LongestName} characters.");

            // Its own name: nothing done, nothing said.
            program.Main.Session.Status = "";
            Rename(RowOf(open), open);
            Assert.Equal("", Said);
            Assert.Same(store, Vaults.Store);

            // Cancelled: the box goes, the name stays.
            var cancelled = RowOf(open);
            Manager.StartRenameCommand.Execute(cancelled);
            cancelled.EditName = Renamed;
            Manager.CancelRenameCommand.Execute(cancelled);
            Assert.False(cancelled.IsRenaming);
            Assert.False(File.Exists(PathOf(Renamed)));
        });
    }

    [CorpusFact]
    public void With_a_saves_moves_waiting_the_open_vault_is_not_closed_under_them()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            string open = Vaults.VaultName;
            var store = Vaults.Store;
            var moves = program.Get<VaultMoves>();
            var purse = program.Main.Inventory!.Sections.First(section => section.Pages is not null).CarriedGold!;
            purse.Value = "20000";

            // A stored item, which a save moves into this very file.
            var tile = program.Main.Session.Equipped.Select(slot => slot.Item).OfType<ItemTileViewModel>().First();
            moves.Store(tile.Item, null);
            try
            {
                var row = RowOf(open);
                Typed(row, Renamed);

                Assert.Equal("Require save or undo.", Said);
                StaysOpen(row, Renamed);
                Assert.Equal(open, Vaults.VaultName);
                Assert.Same(store, Vaults.Store);
                Assert.False(File.Exists(PathOf(Renamed)), "renamed with a save's moves waiting");
            }
            finally
            {
                moves.Keep(tile.Item);
            }
        });
    }
}
