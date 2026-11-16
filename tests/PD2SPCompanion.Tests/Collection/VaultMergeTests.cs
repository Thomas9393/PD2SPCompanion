using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Data.Sqlite;

using Xunit;
using Xunit.Abstractions;

namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// A mode's vaults as files:
/// its collections of before folded into its first vault; several vaults a
/// mode, made, copied, renamed, deleted, and the one of before moved to its name; and the one import - a
/// file as a vault of its own, or as a folder of ticked folders. In a vault folder of its own
/// (PD2SPC_VAULTS), never the player's.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class VaultMergeTests(Game game, ITestOutputHelper output) : IDisposable
{
    private const string Season = "S13";

    private readonly string _vaults = Path.Combine(Path.GetTempPath(), "pd2spc-merge-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string? _vaultsBefore = Environment.GetEnvironmentVariable("PD2SPC_VAULTS");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PD2SPC_VAULTS", _vaultsBefore);
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_vaults, recursive: true); } catch (IOException) { }
    }

    private void UseOwnVaults()
    {
        Directory.CreateDirectory(_vaults);
        Environment.SetEnvironmentVariable("PD2SPC_VAULTS", _vaults);
    }

    /// <summary>The corpus character's items.</summary>
    private static List<D2SLib.Model.Save.Item> Items()
    {
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));
        return [.. save.PlayerItemList!.Items];
    }

    /// <summary>Which folder each item of a vault is in, by its name and its id: the folder's path.</summary>
    private static Dictionary<(string Name, uint? Id), string> Lay(Vault vault)
    {
        var folders = vault.Folders().ToDictionary(folder => folder.Id);
        string PathOf(long? id)
        {
            var path = new List<string>();
            for (var at = id; at is { } folder && folders.TryGetValue(folder, out var each); at = each.ParentId)
                path.Insert(0, each.Name);
            return string.Join("/", path);
        }

        var lay = new Dictionary<(string, uint?), string>();
        foreach (var row in vault.RowsOf(vault.AllKeys()))
            lay[(row.Name, row.OriginId)] = PathOf(row.FolderId);
        return lay;
    }

    [CorpusFact]
    public void Collections_are_folded_into_the_vault()
    {
        if (!game.Available)
            return;

        UseOwnVaults();
        var items = Items();
        int half = items.Count / 2;

        // As it stood before the merge: records in the mode's vault file, items in two collections.
        using (var records = VaultLibrary.OpenVault(Season, false, 10))
            records.RecordGold([new GoldMove("stash", 1234)]);

        Dictionary<(string, uint?), string> defaultLay;
        int inDefault = 0, inLibrary = 0;
        using (var first = VaultLibrary.OpenCollection(Season, false, VaultLibrary.DefaultCollection, 10))
        {
            long charms = first.CreateFolder(null, "Charms");
            long small = first.CreateFolder(charms, "Small");
            first.CreateFolder(null, "Empty");
            for (int i = 0; i < half; i++)
            {
                if (first.Add(items[i], i % 3 == 0 ? small : i % 3 == 1 ? charms : null, out _) is { } key)
                {
                    inDefault++;
                    if (inDefault == 1)
                        first.StoredLongAgo(key);
                }
            }

            defaultLay = Lay(first);
        }

        using (var library = VaultLibrary.OpenCollection(Season, false, "Library", 10))
        {
            long bases = library.CreateFolder(null, "Bases");
            for (int i = half; i < items.Count; i++)
            {
                if (library.Add(items[i], i % 2 == 0 ? bases : null, out _) is not null)
                    inLibrary++;
            }
        }

        Assert.True(VaultLibrary.NeedsMerge(Season, false));
        Assert.False(VaultLibrary.NeedsMerge(Season, true));

        var report = VaultLibrary.MergeCollections(Season, false, 10);
        output.WriteLine($"{report.Collections} collections, {report.Items} items, {report.Failed} failed; backup {report.Backup}; retired {report.Retired}");

        Assert.Equal(2, report.Collections);
        Assert.Equal(inDefault + inLibrary, report.Items);
        Assert.Equal(0, report.Failed);
        Assert.False(VaultLibrary.NeedsMerge(Season, false));
        Assert.True(File.Exists(report.Backup));
        Assert.True(File.Exists(Path.Combine(report.Retired, "Default.pd2lib")));
        Assert.True(File.Exists(Path.Combine(report.Retired, "Library.pd2lib")));

        using var vault = VaultLibrary.OpenVault(Season, false, 10);
        Assert.Equal(inDefault + inLibrary, vault.Count());
        Assert.Equal(1234, vault.GoldBalance());
        Assert.Equal(VaultLibrary.VaultKind, vault.Kind);

        var folders = vault.Folders();
        Assert.Single(folders, folder => folder.Name == "Charms" && folder.ParentId is null);
        Assert.Single(folders, folder => folder.Name == "Empty" && folder.ParentId is null);
        var libraryFolder = Assert.Single(folders, folder => folder.Name == "Library" && folder.ParentId is null);
        Assert.Single(folders, folder => folder.Name == "Bases" && folder.ParentId == libraryFolder.Id);

        // The Default's items are in the folders they were in.
        var lay = Lay(vault);
        foreach (var (item, was) in defaultLay)
            Assert.Equal(was, lay[item]);

        // The other collection's are all under its name.
        Assert.Equal(inLibrary, lay.Values.Count(path => path == "Library" || path == "Library/Bases"));
        Assert.Contains(vault.RowsOf(vault.AllKeys()), row => row.Added == VaultDates.LongAgo);
    }

    [CorpusFact]
    public void A_file_comes_in_as_a_vault_or_as_a_folder_of_ticked_folders()
    {
        if (!game.Available)
            return;

        UseOwnVaults();
        var items = Items();
        string file = Path.Combine(_vaults, "Playtest.pd2vault");

        int inCharms = 0, inSmall = 0, inOther = 0, atTop = 0;
        using (var vault = VaultLibrary.OpenVault(Season, false, 10))
        {
            long charms = vault.CreateFolder(null, "Charms");
            long small = vault.CreateFolder(charms, "Small");
            long other = vault.CreateFolder(null, "Other");
            vault.RecordGold([new GoldMove("stash", 77)]);

            for (int i = 0; i < items.Count; i++)
            {
                long? into = i % 4 == 0 ? charms : i % 4 == 1 ? small : i % 4 == 2 ? other : null;
                if (vault.Add(items[i], into, out _) is null)
                    continue;

                if (into == charms) inCharms++;
                else if (into == small) inSmall++;
                else if (into == other) inOther++;
                else atTop++;
            }

            vault.CopyTo(file);
        }

        Assert.True(inCharms > 0 && inSmall > 0 && inOther > 0 && atTop > 0);
        int all = inCharms + inSmall + inOther + atTop;

        using var incoming = VaultLibrary.Stage(file, Season, 10);
        Assert.Equal("Playtest", incoming.Name);
        Assert.Equal(all, incoming.Items);
        Assert.Equal(atTop, incoming.TopItems);
        long theirSmall = incoming.Folders.Single(folder => folder.Name == "Small").Id;
        long theirCharms = incoming.Folders.Single(folder => folder.Name == "Charms").Id;

        // As a folder of the other mode's vault, Small's items and the top's ticked: Charms is made for
        // Small to lie in, with none of its own; Other is not made. No gold comes (nor, until format 20, made marks).
        using var hardcore = VaultLibrary.OpenVault(Season, true, 10);
        var first = VaultLibrary.AddAsFolder(hardcore, incoming, folder => folder is null || folder == theirSmall);
        Assert.Equal(("Playtest", inSmall + atTop, 0), first);
        Assert.Equal(0, hardcore.GoldBalance());

        var folders = hardcore.Folders();
        var top = Assert.Single(folders, folder => folder.Name == "Playtest" && folder.ParentId is null);
        var charmsHere = Assert.Single(folders, folder => folder.Name == "Charms" && folder.ParentId == top.Id);
        var smallHere = Assert.Single(folders, folder => folder.Name == "Small" && folder.ParentId == charmsHere.Id);
        Assert.Equal(atTop, hardcore.Count(top.Id));
        Assert.Equal(0, hardcore.Count(charmsHere.Id));
        Assert.Equal(inSmall, hardcore.Count(smallHere.Id));
        Assert.DoesNotContain(folders, folder => folder.Name == "Other");

        // Again, everything: under a name of its own, never into the folder there.
        var second = VaultLibrary.AddAsFolder(hardcore, incoming);
        Assert.Equal(("Playtest (2)", all, 0), second);
        Assert.Equal(inSmall + atTop + all, hardcore.Count());
        Assert.Equal(inCharms, hardcore.Count(hardcore.Folders().Single(folder =>
            folder.Name == "Charms" && hardcore.Folders().Single(each => each.Id == folder.ParentId).Name == "Playtest (2)").Id));

        // As a vault of its own: whole, the gold too, beside the mode's first - and a second time under a number.
        Assert.Equal("Playtest", VaultLibrary.AddAsVault(incoming, Season, true, 10));
        Assert.Equal("Playtest (2)", VaultLibrary.AddAsVault(incoming, Season, true, 10));
        using (var added = VaultLibrary.OpenVault(Season, true, "Playtest", 10))
        {
            Assert.Equal(all, added.Count());
            Assert.Equal(77, added.GoldBalance());
            Assert.True(added.IsHardcore);
            Assert.Equal(VaultLibrary.VaultKind, added.Kind);
        }

        Assert.Equal(["Playtest", "Playtest (2)", VaultLibrary.MainName(Season)],
                     VaultLibrary.Vaults(Season, true).Select(each => each.Name));
        Assert.True(theirCharms != theirSmall);

        // Not a vault file: refused, and nothing left of it.
        string junk = Path.Combine(_vaults, "junk.pd2vault");
        File.WriteAllText(junk, "not a vault");
        var refused = Assert.ThrowsAny<Exception>(() => VaultLibrary.Stage(junk, Season, 10));
        output.WriteLine(refused.Message);
    }

    /// <summary>
    /// Two purses' gold gone from the file and in neither place: each is put back. The first put-back is a write of the
    /// vault's gold, and the second purse read it as its own save's and was left out with no word.
    /// </summary>
    [CorpusFact]
    public void Gold_of_two_purses_lost_is_put_back_for_both()
    {
        if (!game.Available)
            return;

        UseOwnVaults();
        string character = Path.Combine(_vaults, "Purses.d2s");
        File.Copy(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"), character);
        var file = SaveCounts.Read(character);

        using var vault = Vault.Open(Path.Combine(_vaults, "gold.pd2lib"), Season, 10);
        long written = vault.GoldWrites(), balance = vault.GoldBalance();
        PendingMove Gone(string purse, long amount) =>
            new(MoveRecovery.Gold, character, "Purses", "gold", null, "", null, "Main", null, null,
                purse, amount, file.Purse(purse) + amount, file.Purse(purse), written, written + 1);

        vault.JournalMoves([Gone(MoveRecovery.CarriedPurse, 100), Gone(MoveRecovery.StashedPurse, 200)]);

        var said = MoveRecovery.Recover(vault);

        Assert.Equal(2, said.Count);
        Assert.Equal(balance + 300, vault.GoldBalance());
    }

    /// <summary>Marks a vault's file as written by a build newer than this one.</summary>
    private static void MakeNewer(string file)
    {
        using var vault = new SqliteConnection($"Data Source={file}");
        vault.Open();
        using var change = vault.CreateCommand();
        change.CommandText = "UPDATE vault SET schema_version = 9999 WHERE id = 1;";
        change.ExecuteNonQuery();
        vault.Close();
        SqliteConnection.ClearAllPools();
    }

    [CorpusFact]
    public void A_newer_export_is_refused_with_no_copy_left_in_the_temp_folder()
    {
        UseOwnVaults();
        string file = Path.Combine(_vaults, "newer.pd2lib");
        using (Vault.Open(file, Season, 10))
        {
        }
        MakeNewer(file);

        string[] Staged() => Directory.GetFiles(Path.GetTempPath(), "pd2spc-import-*");
        var before = Staged();
        var refused = Assert.ThrowsAny<InvalidOperationException>(() => VaultLibrary.Stage(file, Season, 10));
        Assert.Contains("newer", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Staged().Except(before));
    }

    [CorpusTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_vault_that_will_not_open_can_still_be_deleted(bool newer)
    {
        UseOwnVaults();
        string path = VaultLibrary.VaultPath(Season, false, "Broken");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (newer)
        {
            using (Vault.Open(path, Season, 10))
            {
            }
            MakeNewer(path);
        }
        else
        {
            File.WriteAllText(path, "not a vault, and long enough to read as a damaged file");
        }

        byte[] held = File.ReadAllBytes(path);
        string backup = VaultLibrary.DeleteVault(Season, false, "Broken", 10);

        Assert.False(File.Exists(path));
        Assert.True(File.Exists(backup));

        // Opened on the way, a newer vault's own header may move; a damaged file is untouched.
        if (!newer)
            Assert.Equal(held, File.ReadAllBytes(backup));
    }

    [CorpusFact]
    public void A_mode_has_several_vaults()
    {
        if (!game.Available)
            return;

        UseOwnVaults();
        var items = Items();
        string main = VaultLibrary.MainName(Season);
        Assert.Equal("S13 Main", main);

        // The one vault of an older version, where it was kept: listed under the name it will have, read
        // from where it lies, and moved to its name.
        string old = Path.Combine(_vaults, Season, "Profiles", "Softcore.pd2lib");
        int held = 0;
        using (var before = Vault.Open(old, Season, 10))
        {
            before.Describe(VaultLibrary.VaultKind, false);
            foreach (var item in items.Take(6))
                held += before.Add(item, null, out _) is null ? 0 : 1;
            before.RecordGold([new GoldMove("stash", 500)]);
            before.SetAbout(VaultLibrary.PlayModeKey, "Legit");
        }

        SqliteConnection.ClearAllPools();
        var listed = Assert.Single(VaultLibrary.Vaults(Season, false));
        Assert.Equal((main, held, "Legit", old), (listed.Name, listed.Items, listed.PlayMode, listed.Path));
        Assert.Equal(old, VaultLibrary.ExistingPath(Season, false, main));
        Assert.Equal(old, Assert.Single(VaultLibrary.ItemSources(Season, false, main)).Path);

        Assert.Equal(1, VaultLibrary.Rehouse(Season));
        Assert.Equal(0, VaultLibrary.Rehouse(Season));
        Assert.False(File.Exists(old));
        Assert.False(Directory.Exists(Path.GetDirectoryName(old)));
        Assert.Equal(VaultLibrary.VaultPath(Season, false), Assert.Single(VaultLibrary.Vaults(Season, false)).Path);

        // A second, empty; a third, a copy of the first with everything in it.
        Assert.Null(VaultLibrary.RefuseName(Season, false, "Playtest"));
        Assert.NotNull(VaultLibrary.RefuseName(Season, false, main));
        Assert.NotNull(VaultLibrary.RefuseName(Season, false, "a/b"));
        Assert.NotNull(VaultLibrary.RefuseName(Season, false, " "));
        Assert.Null(VaultLibrary.RefuseName(Season, false, main, except: main));

        VaultLibrary.CreateVault(Season, false, "Playtest", null, 10);
        VaultLibrary.CreateVault(Season, false, "Copy", main, 10);
        Assert.Throws<InvalidOperationException>(() => VaultLibrary.CreateVault(Season, false, "Copy", null, 10));
        SqliteConnection.ClearAllPools();

        var vaults = VaultLibrary.Vaults(Season, false);
        Assert.Equal(["Copy", "Playtest", main], vaults.Select(each => each.Name));
        Assert.Equal([held, 0, held], vaults.Select(each => each.Items));
        Assert.Empty(VaultLibrary.Vaults(Season, true));

        using (var copy = VaultLibrary.OpenVault(Season, false, "Copy", 10))
        {
            Assert.Equal(500, copy.GoldBalance());
            Assert.Equal("Legit", copy.About(VaultLibrary.PlayModeKey));
        }

        // A copy is its own file: what goes into one is not in the other.
        using (var copy = VaultLibrary.OpenVault(Season, false, "Copy", 10))
            Assert.NotNull(copy.Add(items[7], null, out _));
        using (var first = VaultLibrary.OpenVault(Season, false, 10))
            Assert.Equal(held, first.Count());

        // Renamed, and deleted with a copy kept.
        SqliteConnection.ClearAllPools();
        VaultLibrary.RenameVault(Season, false, "Copy", "Self found");
        Assert.Equal(["Playtest", main, "Self found"], VaultLibrary.Vaults(Season, false).Select(each => each.Name));

        string backup = VaultLibrary.DeleteVault(Season, false, "Self found", 10);
        Assert.True(File.Exists(backup));
        Assert.Equal(["Playtest", main], VaultLibrary.Vaults(Season, false).Select(each => each.Name));
        Assert.Equal("Self found (2)", VaultLibrary.FreeName(Season, false, VaultLibrary.FreeName(Season, false, "Self found") + " (2)"));

        // A character renamed, in every vault of the season.
        using (var playtest = VaultLibrary.OpenVault(Season, false, "Playtest", 10))
        {
            playtest.RecordTrades([new TradeRecord(DateTimeOffset.UnixEpoch, "Tester", "bought", "listing", null, null, 4242,
                                                   "Shako", "1 Ist", "1 Ist", "Legit")]);
        }
        SqliteConnection.ClearAllPools();
        Assert.True(VaultLibrary.RenameCharacter(Season, "Tester", "Renamed", 10) >= 1);
        using (var playtest = VaultLibrary.OpenVault(Season, false, "Playtest", 10))
            Assert.Contains(playtest.Trades(), trade => trade.ItemId == 4242 && trade.Character == "Renamed");
    }
}
