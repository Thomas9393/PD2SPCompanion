using D2SLib.Model.Save;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// A season's vault migrated into the next, rehearsed S12 ->
/// S13: an S12 vault made under the S12 pack's own tables - items in folders, a note, gold,
/// withdraw and made history, a waiting note - migrated into the installed season. In a vault folder of its
/// own (PD2SPC_VAULTS), never the player's.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class SeasonMigrationTests(Game game, ITestOutputHelper output) : IDisposable
{
    private readonly string _vaults = Path.Combine(Path.GetTempPath(), "pd2spc-migrate-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string? _vaultsBefore = Environment.GetEnvironmentVariable("PD2SPC_VAULTS");

    // Put back when done: KnowTheStamp left them set, and every test after this one read with no season before - an S13
    // corpus item judged as this season's (two legitimacy tests passed only after this class).
    private readonly (string? Current, string? Previous) _stampsBefore = (SeasonReading.CurrentStamp, SeasonReading.PreviousStamp);

    private static string S12Tables =>
        Path.Combine(Game.Corpus, "packs", "tdlofcc-s12", "PD2-PlugY-main", "ProjectD2", "data", "global", "excel", "modpacks", "official");

    private static string S12Stash => Path.Combine(Game.Corpus, "packs", "tdlofcc-s12", "PD2-PlugY-main", "Save", "Library", "Bases.d2x");

    /// <summary>The pack's shared stash: magic, rare, set and unique items, which have ranges to keep a place in.</summary>
    private static string S12Shared => Path.Combine(Game.Corpus, "packs", "tdlofcc-s12", "PD2-PlugY-main", "Save", "Library", "_LOD_SharedStashSave.sss");

    /// <summary>Season 7's tables and shared stash, from the other pack: a season two or more back.</summary>
    private static string S7Root => Path.Combine(Game.Corpus, "packs", "betweenwalls-s7", "PD2-Singleplayer-main", "Diablo II");

    private static string S7Tables => Path.Combine(S7Root, "ProjectD2", "data", "global", "excel", "modpacks", "official");

    private static string S7Shared => Path.Combine(S7Root, "Save", "Library", "_LOD_SharedStashSave.sss");

    /// <summary>
    /// A small vault of <paramref name="season"/>, made under its own tables: the first non-compact items of
    /// <paramref name="stash"/>, half in a "Charms" folder, and some gold. Returns how many items it holds.
    /// </summary>
    private static int MakeVault(string season, string tables, string stash, int take, long gold)
    {
        using var old = Season.Open(Game.Root, tables);
        var (read, _) = SaveVerifier.LoadStash(stash);
        var items = (read.PageList ?? []).SelectMany(page => page.PageItems?.Items ?? []).Where(item => !item.IsCompact).Take(take).ToList();

        int stored = 0;
        using (var vault = VaultLibrary.OpenCollection(season, false, VaultLibrary.DefaultCollection, 10))
        {
            long charms = vault.CreateFolder(null, "Charms");
            foreach (var item in items)
            {
                if (vault.Add(item, stored % 2 == 0 ? null : charms, out _) is not null)
                    stored++;
            }
        }

        using (var records = VaultLibrary.OpenVault(season, false, 10))
            records.RecordGold([new GoldMove("stash", gold)]);

        return stored;
    }

    /// <summary>
    /// The stamp the installed season writes: a snapshot knows it on the player's machine; here, the corpus
    /// character says it. Learned again before every migration: a migration installs the season's tables
    /// again when it ends, which reads the stamp from the snapshot - and a test has none, so it would be
    /// gone. Without it these tests would pass only where the player's own data folder holds S13's
    /// snapshot.
    /// </summary>
    private static void KnowTheStamp()
    {
        if (SeasonReading.CurrentStamp is null)
        {
            var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));
            SeasonReading.SetStamps(save.PlayerItemList!.Items.First(item => item.Version is { Length: > 0 }).Version, null);
        }
    }

    /// <summary>A migration, the stamp known first (see <see cref="KnowTheStamp"/>).</summary>
    private static SeasonMigration.Report Migrate(Season installed, SeasonMigration.Request request)
    {
        KnowTheStamp();
        return SeasonMigration.Run(installed, request);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PD2SPC_VAULTS", _vaultsBefore);
        SeasonReading.SetStamps(_stampsBefore.Current, _stampsBefore.Previous);
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_vaults, recursive: true); } catch (IOException) { }
    }

    /// <summary>Everything an item says, but its stamp: what a migration reported clean is to leave as it was.</summary>
    private static string Lines(Item item) =>
        string.Join("|", item.SocketedItems.Prepend(item).Select(each =>
            $"{each.Code.Trim()} {each.Quality} {each.IsEthereal}:" + string.Join(";", ItemStats.Every(each).Select(stat =>
                $"{stat.Stat}={stat.Value}/{stat.Param}/{stat.SkillId}/{stat.SkillLevel}/{stat.SkillTab}/{stat.CharacterClass}/{stat.MaxCharges}"))));

    /// <summary>An S12 vault: what the migration reads. Returns how many items it holds, its gold, and each row's id and lines, in the vault's order.</summary>
    private (int Items, long Gold, List<(uint? Id, string Lines)> Rows) MakeS12Vault()
    {
        using var s12 = Season.Open(Game.Root, S12Tables);
        var items = new List<Item>();
        foreach (var (file, take) in new[] { (S12Stash, 20), (S12Shared, 200) })
        {
            var (stash, _) = SaveVerifier.LoadStash(file);
            items.AddRange((stash.PageList ?? []).SelectMany(page => page.PageItems?.Items ?? []).Where(item => !item.IsCompact).Take(take));
        }

        int stored = 0;
        var rows = new List<(uint?, string)>();
        using (var vault = VaultLibrary.OpenCollection("S12", false, VaultLibrary.DefaultCollection, 10))
        {
            long grand = vault.CreateFolder(vault.CreateFolder(null, "Charms"), "Grand");
            long bases = vault.CreateFolder(null, "Bases");

            // A folder made and left empty, as the UI pad's "New folder": it goes too, empty as it is.
            vault.CreateFolder(vault.CreateFolder(null, "Kept"), "Empty");
            foreach (var (item, at) in items.Select((item, at) => (item, at)))
            {
                if (vault.Add(item, at % 2 == 0 ? bases : grand, out _) is { } key)
                {
                    stored++;
                    if (stored == 1)
                        vault.StoredLongAgo(key);
                }
            }
            vault.LinkFolder("Charms/Grand", grand);

            // Laid out by hand, and locked: the lock is the folder's, and goes where the folder goes.
            vault.SetLocked(bases, true);

            rows.AddRange(vault.RowsOf(vault.AllKeys()).Select(row => (row.OriginId, Lines(ItemBlob.Restore(row.Blob, 10)))));
        }

        using (var records = VaultLibrary.OpenVault("S12", false, 10))
            records.RecordGold([new GoldMove("stash", 123456)]);

        return (stored, 123456, rows);
    }

    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void An_S12_vault_migrates_into_the_installed_season_whole()
    {
        if (!game.Available || !Directory.Exists(S12Tables) || !File.Exists(S12Stash))
            return;

        Environment.SetEnvironmentVariable("PD2SPC_VAULTS", _vaults);
        var (stored, gold, was) = MakeS12Vault();
        Assert.True(stored >= 150, $"only {stored} items stored in the S12 vault");

        var installed = Season.Open(Game.Root, Game.Tables);
        Assert.Equal(stored, was.Count);
        string to = SeasonSnapshot.NameFor(installed);

        KnowTheStamp();
        string stamp = SeasonReading.CurrentStamp!;

        // The new-season notice's question: S12 is there, not migrated.
        Assert.Equal(["S12"], SeasonMigration.Pending(to));
        Assert.Equal([false], SeasonMigration.PendingModes("S12", to));
        Assert.Equal([(false, "S12 Main")], SeasonMigration.PendingVaults("S12", to));

        // A dry run judges everything and writes nothing.
        var dry = Migrate(installed, new SeasonMigration.Request("S12", false, DryRun: true, OldTables: S12Tables));
        Assert.Equal(stored, dry.Items.Count);
        Assert.True(dry.Rescaled);
        Assert.False(File.Exists(VaultLibrary.VaultPath(to, false, "S12 Main")));
        Assert.True(File.Exists(dry.ReportPath));

        var report = Migrate(installed, new SeasonMigration.Request("S12", false, DryRun: false, OldTables: S12Tables));
        output.WriteLine(report.Text());
        Assert.Equal(stored, report.Items.Count);
        Assert.Equal(0, report.Count(SeasonMigration.Outcome.Failed));

        // A vault of the old one's name takes the items - the old season's were in a collection, as every
        // older vault's were - and the records with them: "S12 Main", beside the new season's own.
        Assert.Equal("S12 Main", report.Vault);
        Assert.False(File.Exists(VaultLibrary.VaultPath(to, false)));
        using (var collection = VaultLibrary.OpenVault(to, false, "S12 Main", 10))
        {
            Assert.Equal(stored, collection.AllKeys().Count);

            var folders = collection.Folders();
            Assert.Contains(folders, folder => folder.Name == "Grand" && folders.Any(parent => parent.Id == folder.ParentId && parent.Name == "Charms"));
            Assert.Contains(folders, folder => folder.Name == "Bases");
            Assert.Contains(folders, folder => folder.Name == "Empty" && folders.Any(parent => parent.Id == folder.ParentId && parent.Name == "Kept"));
            Assert.Equal([folders.Single(folder => folder.Name == "Bases" && folder.ParentId is null).Id], collection.LockedFolders());
            Assert.False(collection.IsLocked(null));
            Assert.Contains(collection.RowsOf(collection.AllKeys()), row => row.Added == VaultDates.LongAgo);
            Assert.Contains(collection.FolderLinks(), link => link.Category == "Charms/Grand");
            Assert.Equal("S12", collection.About(SeasonMigration.MigratedFromKey));

            // Every item reads back in the new season, stamped as it writes - and one reported clean says what it said.
            // The report and both vaults run in one order: the old rows' (the id checks that they do).
            var rows = collection.RowsOf(collection.AllKeys());
            Assert.Equal(stored, rows.Count);
            int clean = 0;
            foreach (var (row, at) in rows.Select((row, at) => (row, at)))
            {
                var item = ItemBlob.Restore(row.Blob, 10);
                Assert.Equal(stamp, item.Version);
                Assert.Equal(was[at].Id, row.OriginId);

                if (report.Items[at].Outcome == SeasonMigration.Outcome.Clean)
                {
                    clean++;
                    Assert.Equal(was[at].Lines, Lines(item));
                }
            }

            // 208 of 220 from S12 into S14: enough that the comparison above is about something.
            Assert.True(clean >= stored / 2, $"only {clean} of {stored} items came across clean");
        }

        using (var vault = VaultLibrary.OpenVault(to, false, "S12 Main", 10))
        {
            Assert.Equal(gold, vault.GoldBalance());
            Assert.Equal("S12", vault.About(SeasonMigration.MigratedFromKey));
            Assert.Equal("S12/S12 Main", vault.About(SeasonMigration.MigratedVaultsKey));
        }

        // Answered: nothing more waits, and the Settings row says migrated.
        Assert.Empty(SeasonMigration.Pending(to));
        Assert.Equal(["S12"], SeasonMigration.MigratedFrom(to, false));
        Assert.Equal(["S12"], SeasonMigration.Older(to));

        // Migrated already: refused, not put in twice.
        var again = Assert.Throws<InvalidOperationException>(() => Migrate(installed, new SeasonMigration.Request("S12", false, DryRun: false, OldTables: S12Tables)));
        Assert.Contains("already", again.Message);

        // The old season is only read: its vault is as it was.
        using (var old = VaultLibrary.OpenCollection("S12", false, VaultLibrary.DefaultCollection, 10))
            Assert.Equal(stored, old.AllKeys().Count);
    }

    /// <summary>
    /// Two older seasons never migrated - S7 and S12 - both waiting, both migrated straight into the
    /// installed season, oldest first: each into a vault of its own name, with its own items, folders and
    /// gold, and each named in the list of seasons migrated from.
    /// </summary>
    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void Two_older_seasons_both_migrate_into_the_installed_one()
    {
        if (!game.Available || !Directory.Exists(S7Tables) || !File.Exists(S7Shared) || !Directory.Exists(S12Tables))
            return;

        Environment.SetEnvironmentVariable("PD2SPC_VAULTS", _vaults);
        int s7 = MakeVault("S7", S7Tables, S7Shared, 20, 700);
        int s12 = MakeVault("S12", S12Tables, S12Shared, 20, 1200);
        Assert.True(s7 > 0 && s12 > 0, $"S7 {s7}, S12 {s12}");

        var installed = Season.Open(Game.Root, Game.Tables);
        string to = SeasonSnapshot.NameFor(installed);
        KnowTheStamp();
        Assert.Equal(["S7", "S12"], SeasonMigration.Pending(to));

        var first = Migrate(installed, new SeasonMigration.Request("S7", false, DryRun: false, OldTables: S7Tables, KeepReport: false));
        output.WriteLine(first.Text());
        Assert.Equal(["S12"], SeasonMigration.Pending(to));

        var second = Migrate(installed, new SeasonMigration.Request("S12", false, DryRun: false, OldTables: S12Tables, KeepReport: false));
        output.WriteLine(second.Text());
        Assert.Empty(SeasonMigration.Pending(to));
        Assert.Equal(["S7", "S12"], SeasonMigration.MigratedFrom(to, false));

        Assert.Equal(["S12 Main", "S7 Main"], VaultLibrary.Vaults(to, false).Select(each => each.Name));
        foreach (var (name, held, report, gold) in new[] { ("S7 Main", s7, first, 700L), ("S12 Main", s12, second, 1200L) })
        {
            using var vault = VaultLibrary.OpenVault(to, false, name, 10);
            Assert.Equal(0, report.Count(SeasonMigration.Outcome.Failed));
            Assert.Equal(held, vault.AllKeys().Count);
            Assert.Single(vault.Folders(), folder => folder.Name == "Charms" && folder.ParentId is null);
            Assert.Equal(gold, vault.GoldBalance());
        }
    }

    /// <summary>
    /// A season with two vaults: each waits by its name, one is migrated and the other
    /// left - which stays waiting, to be migrated later - and the one migrated keeps its name and the play
    /// mode it kept. The new season's own first vault is not touched.
    /// </summary>
    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void A_vault_migrates_by_its_name_and_another_is_left()
    {
        if (!game.Available || !Directory.Exists(S12Tables) || !File.Exists(S12Shared))
            return;

        Environment.SetEnvironmentVariable("PD2SPC_VAULTS", _vaults);
        int main = MakeVault("S12", S12Tables, S12Shared, 10, 100);

        int playtest = 0;
        using (Season.Open(Game.Root, S12Tables))
        {
            var (read, _) = SaveVerifier.LoadStash(S12Shared);
            using var vault = VaultLibrary.OpenVault("S12", false, "Playtest", 10);
            foreach (var item in (read.PageList ?? []).SelectMany(page => page.PageItems?.Items ?? []).Where(item => !item.IsCompact).Skip(10).Take(6))
                playtest += vault.Add(item, null, out _) is null ? 0 : 1;
            vault.RecordGold([new GoldMove("stash", 42)]);
            vault.SetAbout(VaultLibrary.PlayModeKey, "Free");
        }

        Assert.True(main > 0 && playtest > 0, $"main {main}, playtest {playtest}");

        var installed = Season.Open(Game.Root, Game.Tables);
        string to = SeasonSnapshot.NameFor(installed);
        KnowTheStamp();

        // The new season's own first vault, there before anything is migrated.
        using (var own = VaultLibrary.OpenVault(to, false, 10))
            own.RecordGold([new GoldMove("stash", 9)]);
        SqliteConnection.ClearAllPools();

        Assert.Equal([(false, "Playtest"), (false, "S12 Main")], SeasonMigration.PendingVaults("S12", to));

        var report = Migrate(installed, new SeasonMigration.Request("S12", false, DryRun: false, OldTables: S12Tables, KeepReport: false, Vault: "Playtest"));
        output.WriteLine(report.Text());
        Assert.Equal("Playtest", report.Vault);
        Assert.Equal(playtest, report.Items.Count);

        using (var vault = VaultLibrary.OpenVault(to, false, "Playtest", 10))
        {
            Assert.Equal(0, report.Count(SeasonMigration.Outcome.Failed));
            Assert.Equal(playtest, vault.AllKeys().Count);
            Assert.Equal(42, vault.GoldBalance());
            Assert.Equal("Free", vault.About(VaultLibrary.PlayModeKey));
        }

        using (var own = VaultLibrary.OpenVault(to, false, 10))
        {
            Assert.Empty(own.AllKeys());
            Assert.Equal(9, own.GoldBalance());
        }

        // The other still waits, and so does its season; the one migrated is refused a second time.
        SqliteConnection.ClearAllPools();
        Assert.Equal([(false, "S12 Main")], SeasonMigration.PendingVaults("S12", to));
        Assert.Equal(["S12"], SeasonMigration.Pending(to));
        var again = Assert.Throws<InvalidOperationException>(() =>
            Migrate(installed, new SeasonMigration.Request("S12", false, DryRun: false, OldTables: S12Tables, KeepReport: false, Vault: "Playtest")));
        Assert.Contains("already", again.Message);
    }

    /// <summary>
    /// A migration that fails part way - a disk gone full - leaves the target as it was, no
    /// file where it was new, and the season waiting; tried again it adds each item once.
    /// </summary>
    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void A_migration_that_fails_part_way_leaves_nothing_and_a_retry_adds_each_item_once()
    {
        if (!game.Available || !Directory.Exists(S12Tables) || !File.Exists(S12Shared))
            return;

        Environment.SetEnvironmentVariable("PD2SPC_VAULTS", _vaults);
        int main = MakeVault("S12", S12Tables, S12Shared, 10, 100);
        Assert.True(main > 2, $"only {main} items");

        var installed = Season.Open(Game.Root, Game.Tables);
        string to = SeasonSnapshot.NameFor(installed);
        KnowTheStamp();
        SqliteConnection.ClearAllPools();

        string vaultName = Assert.Single(SeasonMigration.PendingVaults("S12", to)).Item2;
        string target = VaultLibrary.VaultPath(to, false, vaultName);
        var request = new SeasonMigration.Request("S12", false, DryRun: false, OldTables: S12Tables, KeepReport: false, Vault: vaultName);

        // The vault new: nothing of it stays.
        Environment.SetEnvironmentVariable(SeasonMigration.FakeFailAfter, "2");
        try
        {
            Assert.Throws<IOException>(() => Migrate(installed, request));
        }
        finally
        {
            Environment.SetEnvironmentVariable(SeasonMigration.FakeFailAfter, null);
        }

        SqliteConnection.ClearAllPools();
        Assert.False(File.Exists(target), "a vault was left by the migration that failed");
        Assert.Contains((false, vaultName), SeasonMigration.PendingVaults("S12", to));

        // The vault there already, some gold of its own in it: it stays as it was.
        using (var own = VaultLibrary.OpenVault(to, false, vaultName, 10))
            own.RecordGold([new GoldMove("stash", 9)]);
        SqliteConnection.ClearAllPools();

        Environment.SetEnvironmentVariable(SeasonMigration.FakeFailAfter, "2");
        try
        {
            Assert.Throws<IOException>(() => Migrate(installed, request));
        }
        finally
        {
            Environment.SetEnvironmentVariable(SeasonMigration.FakeFailAfter, null);
        }

        SqliteConnection.ClearAllPools();
        using (var vault = VaultLibrary.OpenVault(to, false, vaultName, 10))
        {
            Assert.Empty(vault.AllKeys());
            Assert.Equal(9, vault.GoldBalance());
        }

        SqliteConnection.ClearAllPools();

        // Tried again: each once, the gold once, and done.
        var report = Migrate(installed, request);
        Assert.Equal(0, report.Count(SeasonMigration.Outcome.Failed));

        using (var vault = VaultLibrary.OpenVault(to, false, vaultName, 10))
        {
            Assert.Equal(main, vault.AllKeys().Count);
            Assert.Equal(109, vault.GoldBalance());
        }

        SqliteConnection.ClearAllPools();
        Assert.DoesNotContain((false, vaultName), SeasonMigration.PendingVaults("S12", to));
    }

    /// <summary>
    /// A season migrated into the one after it is done: S11's vault went on with S12's, so only S12 waits for
    /// S13 - S11 is not offered again. Decided from the files alone, which are only read.
    /// </summary>
    [CorpusFact]
    public void A_season_migrated_into_the_next_does_not_wait_again()
    {
        if (!game.Available)
            return;

        Environment.SetEnvironmentVariable("PD2SPC_VAULTS", _vaults);
        using (var s11 = VaultLibrary.OpenVault("S11", false, 10))
            s11.RecordGold([new GoldMove("stash", 1)]);
        using (var s12 = VaultLibrary.OpenVault("S12", false, 10))
            s12.SetAbout(SeasonMigration.MigratedFromKey, "S11");

        Assert.Equal(["S11", "S12"], SeasonMigration.Older("S13"));
        Assert.Equal(["S12"], SeasonMigration.Pending("S13"));
        Assert.Empty(SeasonMigration.Pending("S12"));
    }
}
