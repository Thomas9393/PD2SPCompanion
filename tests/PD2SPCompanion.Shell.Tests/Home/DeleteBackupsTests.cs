using PD2SPCompanion.Core.Collection;
using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Home;

/// <summary>
/// Backups deleted on purpose: Delete all on the Settings page, which asks; one row's bin on the open character's list,
/// which is pressed twice. What goes is the saves' backups and nothing else - not the characters, not the file kept as
/// last saved, not the vault's copies. Asked through the commands and read back from the files in the run's folder.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class DeleteBackupsTests(Program program)
{
    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Default_L HomePage => Main.Home;
    private VP_ViewModel_Settings_L Settings => program.Get<VP_ViewModel_Settings_L>();

    private string Saves => Path.Combine(program.Folder, "saves");
    private string Root => SaveWriter.BackupRoot(Saves);

    private string[] Baks() => Directory.Exists(Root) ? Directory.GetFiles(Root, "*.bak", SearchOption.AllDirectories) : [];

    /// <summary>A backup of each character, a moment of the test's own apart, so none lands on another's name.</summary>
    private void TwoCharactersBackedUp()
    {
        SaveWriter.Backup(program.SaveOf(Program.Open), "20260101-000001", null);
        SaveWriter.Backup(program.SaveOf(Program.Open), "20260101-000002", null);
        SaveWriter.Backup(program.SaveOf(Program.Other), "20260101-000003", null);
    }

    /// <summary>Every file of the run that is not a save's backup, with its bytes' length: what no delete here may touch.</summary>
    private Dictionary<string, long> EverythingElse()
    {
        string vaults = Path.Combine(program.Folder, "vtsmart");
        return Directory.GetFiles(Saves, "*", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(vaults, "*.pd2lib", SearchOption.AllDirectories))
            .Where(file => !file.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))

            // The backups' captions, whose lines go with them.
            .Where(file => !Path.GetFileName(file).Equals("captions.txt", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(file => file, file => new FileInfo(file).Length);
    }

    private void Leave() => program.On(() =>
    {
        if (Main.Confirm.IsAsking)
            Main.Confirm.RejectCommand.Execute(null);
    });

    [CorpusFact]
    public void Delete_all_asks_no_leaves_every_backup_and_yes_deletes_only_backups()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            program.On(() =>
            {
                TwoCharactersBackedUp();

                // A vault's copy and the file kept as last saved, which are not this list's.
                Assert.StartsWith(program.Folder, PD2SPCompanion.Core.Collection.Vault.BackupFolder, StringComparison.OrdinalIgnoreCase);
                string copy = Path.Combine(PD2SPCompanion.Core.Collection.Vault.BackupFolder, "Keep me (deleted).pd2lib");
                Directory.CreateDirectory(PD2SPCompanion.Core.Collection.Vault.BackupFolder);
                File.WriteAllText(copy, "a vault's copy");
                string last = SaveWriter.AsWrittenPath(program.SaveOf(Program.Open));
                Directory.CreateDirectory(Path.GetDirectoryName(last)!);
                File.WriteAllText(last, "as last saved");

                var baks = Baks();
                Assert.True(baks.Length >= 3, $"{baks.Length} backups");
                var others = EverythingElse();

                Settings.DeleteAllCommand.Execute(null);

                Assert.True(Main.Confirm.IsAsking);
                Assert.Equal("Delete all backups", Main.Confirm.Title);
                Assert.StartsWith($"{baks.Length} backups will be deleted. Characters are not affected.", Main.Confirm.Message);
                Assert.Equal("DELETE ALL", Main.Confirm.ConfirmLabel);
                Assert.Equal(baks.Length, Baks().Length);

                // No: nothing goes.
                Main.Confirm.RejectCommand.Execute(null);
                Assert.Equal(baks.Length, Baks().Length);

                Settings.DeleteAllCommand.Execute(null);
                Main.Confirm.AcceptCommand.Execute(null);

                Assert.Equal($"Deleted {baks.Length} backups.", Session.Status);
                Assert.Empty(Baks());
                Assert.All(others, other => Assert.True(File.Exists(other.Key), $"{other.Key} went with the backups"));
                Assert.True(File.Exists(copy));
                Assert.True(File.Exists(program.SaveOf(Program.Open)));
                Assert.True(File.Exists(program.SaveOf(Program.Other)));
            });

            // The page's count follows, in its own time.
            Assert.True(program.Until(() => Settings.BackupCount == 0), "Settings still counts backups: " + program.On(() => Settings.BackupSummary));
            program.On(() =>
            {
                Assert.Equal("None", Settings.BackupSummary);
                Assert.False(Settings.HasBackups);

                // Nothing left to ask about.
                Settings.DeleteAllCommand.Execute(null);
                Assert.False(Main.Confirm.IsAsking);
                Assert.Equal("No backups.", Session.Status);
            });
        }
        finally
        {
            Leave();
        }
    }

    [CorpusFact]
    public void Clearing_the_trade_cache_asks_no_leaves_it_and_yes_deletes_only_the_listings()
    {
        if (!program.Ready())
            return;

        string folder = PD2SPCompanion.Core.Trade.TradeListings.CacheFolder;
        Assert.StartsWith(program.Folder, folder, StringComparison.OrdinalIgnoreCase);
        Directory.CreateDirectory(folder);

        // What another test left is put back after: this one wants the cache to itself.
        var found = Directory.GetFiles(folder, "*.json").ToDictionary(file => file, File.ReadAllBytes);
        foreach (string file in found.Keys)
            File.Delete(file);

        string[] listings = [Path.Combine(folder, "test-listing-1.json"), Path.Combine(folder, "test-listing-2.json")];
        string other = Path.Combine(folder, "not-a-listing.txt");
        foreach (string file in listings.Append(other))
            File.WriteAllText(file, "{}");

        var said = new List<string>();
        void Heard(object? sender, System.ComponentModel.PropertyChangedEventArgs changed)
        {
            if (changed.PropertyName == nameof(Session.Status))
                said.Add(Session.Status);
        }

        try
        {
            program.On(() =>
            {
                Settings.ClearCacheCommand.Execute(null);
                Assert.True(Main.Confirm.IsAsking);
                Assert.Equal("Clear trade cache", Main.Confirm.Title);
                Assert.StartsWith("2 cached listings will be deleted.", Main.Confirm.Message);

                Main.Confirm.RejectCommand.Execute(null);
                Assert.All(listings, file => Assert.True(File.Exists(file), "gone on a no"));

                Settings.ClearCacheCommand.Execute(null);
                // What it says is heard as it is said: a limit another test lowered can say its own right after.
                Session.PropertyChanged += Heard;
                Main.Confirm.AcceptCommand.Execute(null);
            });

            Assert.True(program.Until(() => said.Contains("Cleared 2 cached listings.")), string.Join(" | ", said));
            program.On(() =>
            {
                Session.PropertyChanged -= Heard;
                Assert.All(listings, file => Assert.False(File.Exists(file), "still there"));
                Assert.True(File.Exists(other), "what is not a listing went too");

                Settings.ClearCacheCommand.Execute(null);
                Assert.False(Main.Confirm.IsAsking);
                Assert.Equal("No cached listings.", Session.Status);
            });
        }
        finally
        {
            Leave();
            File.Delete(other);
            foreach (string file in listings)
                File.Delete(file);
            foreach (var (file, bytes) in found)
                File.WriteAllBytes(file, bytes);
        }
    }

    [CorpusFact]
    public void One_rows_bin_asks_on_the_first_press_and_deletes_that_backup_alone_on_the_second()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            program.On(() =>
            {
                TwoCharactersBackedUp();
                HomePage.LookAtBackups();

                var rows = HomePage.Backups.Where(row => row.Path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)).ToList();
                Assert.True(rows.Count >= 2, $"{rows.Count} backups listed");
                var going = rows[0];
                var staying = rows[1];
                int all = Baks().Length;

                HomePage.DeleteBackupCommand.Execute(going);
                Assert.True(going.IsArmed);
                Assert.True(File.Exists(going.Path), "deleted on the first press");

                // Another row asked: the first is taken back, and nothing goes.
                HomePage.DeleteBackupCommand.Execute(staying);
                Assert.False(going.IsArmed);
                Assert.True(staying.IsArmed);
                Assert.Equal(all, Baks().Length);

                HomePage.DeleteBackupCommand.Execute(going);
                HomePage.DeleteBackupCommand.Execute(going);

                Assert.False(File.Exists(going.Path), "not deleted on the second press");
                Assert.True(File.Exists(staying.Path), "the other backup went too");
                Assert.Equal(all - 1, Baks().Length);
                Assert.Equal($"Deleted backup from {going.When}.", Session.Status);
                Assert.DoesNotContain(HomePage.Backups, row => row.Path == going.Path);
                Assert.Contains(HomePage.Backups, row => row.Path == staying.Path);

                // Another character's are not this list's.
                Assert.Contains(Baks(), file => file.Contains(Path.DirectorySeparatorChar + Program.Other + Path.DirectorySeparatorChar, StringComparison.Ordinal));
            });
        }
        finally
        {
            Leave();
        }
    }
}
