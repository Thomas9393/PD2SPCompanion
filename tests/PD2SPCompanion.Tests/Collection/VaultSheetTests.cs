using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Data.Sqlite;

using Xunit;
using Xunit.Abstractions;

namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// A vault folder's sheet: no item has a place of
/// its own - a folder is laid out from its order by a rule (VaultLayoutTests has the rule), an item is in a folder
/// and nowhere more particular, and the file of the format before, which kept a page and a cell for each, opens
/// without them. With what a place was never needed for and this file's forerunner (VaultPlacesTests) also held:
/// a folder's lock, what a search finds where, and a long folder read quickly.
/// On a real character's items, since an item's size is its base's row of the game's tables.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class VaultSheetTests(Game game, ITestOutputHelper output) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pd2spc-vault-sheet-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private string FilePath => Path.Combine(_folder, "sheet.pd2lib");

    private Vault Open()
    {
        Directory.CreateDirectory(_folder);
        return Vault.Open(FilePath, "S13", 10);
    }

    /// <summary>The corpus character's items, stored: the keys, in the order stored.</summary>
    private static List<long> Store(Vault vault, long? folder, int most = int.MaxValue)
    {
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));
        var keys = new List<long>();
        foreach (var item in save.PlayerItemList!.Items)
        {
            if (keys.Count < most && vault.Add(item, folder, out _) is { } key)
                keys.Add(key);
        }

        return keys;
    }

    private static void NoneOverlap(IReadOnlyList<VaultMark> marks, int columns)
    {
        var taken = new HashSet<(int, int)>();
        foreach (var mark in marks)
        {
            Assert.InRange(mark.X, 0, columns - mark.Width);
            Assert.InRange(mark.Y, 0, Vault.PageRows - 1);

            for (int x = mark.X; x < mark.X + mark.Width; x++)
            {
                for (int row = mark.Row; row < mark.Row + mark.Height; row++)
                    Assert.True(taken.Add((x, row)), $"two items at column {x}, row {row}");
            }
        }
    }

    [CorpusFact]
    public void A_folder_is_laid_out_from_its_order_at_the_width_asked()
    {
        if (!game.Available)
            return;

        using var vault = Open();
        var keys = Store(vault, null);

        // In the order stored: the first stored opens the first lane, top left.
        var (marks, items, rows) = vault.Sheet(null, "Stored", false, 20);
        Assert.Equal(keys, items.Select(item => item.Key));
        Assert.Equal(keys.Count, marks.Count);
        NoneOverlap(marks, 20);
        Assert.Equal((0, 0, 0), (marks.Single(mark => mark.Key == keys[0]).Page, marks.Single(mark => mark.Key == keys[0]).X, marks.Single(mark => mark.Key == keys[0]).Y));
        Assert.Equal(rows, marks.Max(mark => mark.Row + mark.Height));

        // The marks come as a reader meets them, row by row.
        Assert.Equal(marks.OrderBy(mark => mark.Row).ThenBy(mark => mark.X).Select(mark => mark.Key), marks.Select(mark => mark.Key));

        // By name, the other way, and narrower: another order, more rows, nothing over anything.
        var byName = vault.Sheet(null, "Name", true, 10);
        Assert.Equal(items.OrderByDescending(item => item.Name, StringComparer.OrdinalIgnoreCase).Select(item => item.Name), byName.Items.Select(item => item.Name));
        NoneOverlap(byName.Marks, 10);
        Assert.True(byName.Rows > rows, $"{byName.Rows} rows at ten columns against {rows} at twenty");

        // An item's footprint is its base's, as the game has it; nothing of the item is read for it.
        Assert.All(items, item => Assert.InRange(item.Width * item.Height, 1, 8));
    }

    [CorpusFact]
    public void An_item_is_in_a_folder_and_nowhere_more_particular()
    {
        if (!game.Available)
            return;

        using var vault = Open();
        long charms = vault.CreateFolder(null, "Charms");
        var keys = Store(vault, null, most: 6);

        // Moved, it is in the other folder's sheet and out of this one's; nothing else of it changes.
        var before = vault.RowsOf([keys[2]]).Single();
        Assert.Equal(1, vault.MoveItems([keys[2]], charms));
        Assert.DoesNotContain(vault.Sheet(null, "Stored", false, 20).Marks, mark => mark.Key == keys[2]);
        Assert.Equal([keys[2]], vault.Sheet(charms, "Stored", false, 20).Marks.Select(mark => mark.Key));
        Assert.Equal(before with { FolderId = charms }, vault.RowsOf([keys[2]]).Single() with { Blob = before.Blob });

        // A folder deleted with its contents kept: they are at the top again.
        vault.DeleteFolder(charms, everything: false);
        Assert.Contains(vault.Sheet(null, "Stored", false, 20).Marks, mark => mark.Key == keys[2]);
    }

    [CorpusFact]
    public void A_row_put_back_is_in_its_folder_under_its_own_date()
    {
        if (!game.Available)
            return;

        using var vault = Open();
        long charms = vault.CreateFolder(null, "Charms");
        var keys = Store(vault, charms, most: 5);
        vault.StoredLongAgo(keys[4]);

        var row = vault.RowsOf([keys[4]]).Single();
        vault.Remove([keys[4]]);
        long back = vault.PutBack(row, out _) ?? 0;

        var again = vault.RowsOf([back]).Single();
        Assert.Equal(charms, again.FolderId);
        Assert.Equal(VaultDates.LongAgo, again.Added);

        // And in the folder's order where its date puts it: the first, stored long ago.
        Assert.Equal(back, vault.Footprints(charms, "Stored", false)[0].Key);
    }

    /// <summary>
    /// A vault of the format before (18), which kept a page and a cell for every item and a width of its own,
    /// opened by this build: the three columns and the width are gone, every item is there, in its folder, and a
    /// copy of the file as it was is in Backups.
    /// </summary>
    [CorpusFact]
    public void A_vault_that_kept_places_opens_without_them()
    {
        if (!game.Available)
            return;

        List<long> keys;
        long charms;
        using (var vault = Open())
        {
            charms = vault.CreateFolder(null, "Charms");
            keys = Store(vault, null, most: 6);
            vault.MoveItems([keys[1], keys[3]], charms);

            // As the build before wrote it.
            using var old = vault.Connection.CreateCommand();
            old.CommandText = """
                ALTER TABLE item ADD COLUMN page   INTEGER;
                ALTER TABLE item ADD COLUMN cell_x INTEGER;
                ALTER TABLE item ADD COLUMN cell_y INTEGER;
                CREATE INDEX item_by_place ON item(folder_id, page);
                UPDATE item SET page = key % 3, cell_x = (key * 2) % 18, cell_y = key % 15;
                INSERT INTO about (key, value) VALUES ('width', '30') ON CONFLICT(key) DO UPDATE SET value = '30';
                UPDATE vault SET schema_version = 18 WHERE id = 1;
                """;
            old.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
        using var opened = Open();

        static List<string> Names(Vault vault, string sql)
        {
            var found = new List<string>();
            using var read = vault.Connection.CreateCommand();
            read.CommandText = sql;
            using var row = read.ExecuteReader();
            while (row.Read())
                found.Add(row.GetString(0));
            return found;
        }

        Assert.Equal(["key", "blob", "hash", "origin_id", "added_at", "folder_id"], Names(opened, "SELECT name FROM pragma_table_info('item');"));
        Assert.DoesNotContain("item_by_place", Names(opened, "SELECT name FROM sqlite_master WHERE type = 'index';"));
        Assert.Empty(Names(opened, "SELECT value FROM about WHERE key = 'width';"));
        Assert.Equal([$"{Vault.SchemaVersion}"], Names(opened, "SELECT CAST(schema_version AS TEXT) FROM vault WHERE id = 1;"));

        Assert.Equal(keys.Count, opened.Count());
        Assert.Equal(new[] { keys[1], keys[3] }, opened.Footprints(charms, "Stored", false).Select(item => item.Key));
        Assert.Equal(4, opened.Sheet(null, "Stored", false, 20).Marks.Count);

        // The file as it was, kept beside it before the step.
        Assert.NotEmpty(Directory.GetFiles(Vault.BackupFolder, "sheet.v18.*"));
    }

    [CorpusFact]
    public void A_search_says_which_folders_hold_a_find_and_which_items_in_one()
    {
        if (!game.Available)
            return;

        using var vault = Open();
        long charms = vault.CreateFolder(null, "Charms");
        var top = Store(vault, null);
        Store(vault, charms, most: 12);

        // A word the first item's name holds: found at the top, and in the folder where the same item is again.
        string name = vault.RowsOf([top[0]]).Single().Name;
        var query = new VaultQuery { Name = name };

        var perFolder = vault.HitsPerFolder(query, out int atTop);
        var here = vault.HitsIn(null, query);
        Assert.True(atTop >= 1);
        Assert.Equal(atTop, here.Count);
        Assert.Contains(top[0], here);
        Assert.Equal(vault.HitsIn(charms, query).Count, perFolder.GetValueOrDefault(charms));
        Assert.Equal(vault.CountEverywhere(query), atTop + perFolder.Values.Sum());

        // Every find of the folder is on its sheet.
        var marks = vault.Sheet(null, "Stored", false, 20).Marks.Select(mark => mark.Key).ToHashSet();
        Assert.All(here, key => Assert.Contains(key, marks));
    }

    /// <summary>
    /// A long folder: the corpus character's items forty times
    /// over in one folder - some 3,700 of them. What the view asks as the folder is shown and scrolled must stay
    /// quick - the whole folder laid out at once, a stretch's items read as it comes into sight; the times are
    /// printed, and held to bounds loose enough for a slow machine and tight enough to catch a read gone quadratic.
    /// </summary>
    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void A_folder_of_thousands_is_laid_out_and_read_quickly()
    {
        if (!game.Available)
            return;

        using var vault = Open();
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));

        var batch = new List<(VaultCapture, long?)>();
        for (int round = 0; round < 40; round++)
        {
            foreach (var item in save.PlayerItemList!.Items)
            {
                if (vault.Capture(item, out _) is { } captured)
                    batch.Add((captured, null));
            }
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        long Lap()
        {
            long took = clock.ElapsedMilliseconds;
            clock.Restart();
            return took;
        }

        var keys = vault.AddCaptured(batch);
        long stored = Lap();

        var (marks, _, rows) = vault.Sheet(null, "Stored", false, 20);
        long laidOut = Lap();
        var byName = vault.Sheet(null, "Name", false, 20);
        long sorted = Lap();
        int middle = rows / Vault.PageRows / 2;
        var stretch = vault.RowsOf(marks.Where(mark => mark.Page == middle || mark.Page == middle + 1).Select(mark => mark.Key));
        long rowRead = Lap();
        var hits = vault.HitsIn(null, new VaultQuery { Name = "charm" });
        var perFolder = vault.HitsPerFolder(new VaultQuery { Name = "charm" }, out int atTop);
        long searched = Lap();
        vault.Add(save.PlayerItemList!.Items[0], null, out _);
        long oneMore = Lap();

        output.WriteLine($"{keys.Count} items on {rows} rows of 20 columns");
        output.WriteLine($"stored {stored} ms; laid out {laidOut} ms; by name {sorted} ms");
        output.WriteLine($"two stretches' rows ({stretch.Count}) {rowRead} ms; search ({hits.Count} found) {searched} ms; one more stored {oneMore} ms");

        Assert.Equal(keys.Count, marks.Count);
        Assert.Equal(keys.Count, byName.Marks.Count);
        NoneOverlap(marks, 20);
        Assert.True(rows > 25 * Vault.PageRows, $"{rows} rows");
        Assert.NotEmpty(stretch);
        Assert.Equal(atTop, hits.Count);
        Assert.Empty(perFolder);

        Assert.True(stored < 20_000, $"stored in {stored} ms");
        foreach (var (what, took) in new[] { ("layout", laidOut), ("layout by name", sorted), ("two stretches", rowRead),
                                             ("search", searched), ("one more stored", oneMore) })
        {
            Assert.True(took < 1_500, $"{what} took {took} ms");
        }
    }

    /// <summary>
    /// A vault's file brought in as a folder (Vault.Absorb) brings its folders' locks: a lock onto a folder made
    /// for it, the source's top onto the folder it comes in as - and a folder that was here already, of the same
    /// name, keeps the lock it has not.
    /// </summary>
    [CorpusFact]
    public void A_vault_brought_in_as_a_folder_brings_its_locks()
    {
        if (!game.Available)
            return;

        Directory.CreateDirectory(_folder);
        using var source = Vault.Open(Path.Combine(_folder, "source.pd2lib"), "S13", 10);
        long laid = source.CreateFolder(null, "Laid out");
        long loose = source.CreateFolder(null, "Loose");
        long joined = source.CreateFolder(null, "Joined");
        var first = Store(source, laid, most: 3);
        Store(source, loose, most: 2);
        Store(source, joined, most: 2);
        Store(source, null, most: 1);
        source.StoredLongAgo(first[0]);
        source.SetLocked(laid, true);
        source.SetLocked(joined, true);
        source.SetLocked(null, true);

        using var here = Open();
        long old = here.CreateFolder(null, "Old");
        long mine = here.CreateFolder(old, "Joined");

        var (added, failed) = here.Absorb(source, ["Old"]);
        Assert.Equal(8, added);
        Assert.Equal(0, failed);

        var folders = here.Folders();
        long Under(long parent, string name) => folders.Single(folder => folder.ParentId == parent && folder.Name == name).Id;

        // "Old" was here: the source's top, locked, does not lock it. "Joined" was here too.
        Assert.Equal([Under(old, "Laid out")], here.LockedFolders());
        Assert.False(here.IsLocked(old));
        Assert.False(here.IsLocked(mine));
        Assert.False(here.IsLocked(Under(old, "Loose")));
        Assert.Contains(here.RowsOf(here.AllKeys()), row => row.Added == VaultDates.LongAgo && row.FolderId == Under(old, "Laid out"));

        // Brought in as a folder that is made for it, the source's top locks that folder.
        var again = here.Absorb(source, ["Brought"]);
        Assert.Equal(8, again.Added);
        folders = here.Folders();
        long brought = folders.Single(folder => folder.ParentId is null && folder.Name == "Brought").Id;
        Assert.True(here.IsLocked(brought));
        Assert.True(here.IsLocked(Under(brought, "Laid out")));
        Assert.True(here.IsLocked(Under(brought, "Joined")));
        Assert.False(here.IsLocked(Under(brought, "Loose")));
    }

    /// <summary>
    /// A folder's lock: set and let go, the top's apart from a folder's, kept in the
    /// vault's file - and gone with its folder, so the folder that comes by the same id is not born locked.
    /// </summary>
    [CorpusFact]
    public void A_folder_is_locked_let_go_and_its_lock_goes_with_it()
    {
        if (!game.Available)
            return;

        long shown, other;
        using (var vault = Open())
        {
            shown = vault.CreateFolder(null, "Shown");
            other = vault.CreateFolder(null, "Other");

            Assert.False(vault.IsLocked(shown));
            Assert.False(vault.IsLocked(null));
            Assert.Empty(vault.LockedFolders());

            vault.SetLocked(shown, true);
            vault.SetLocked(null, true);
            Assert.True(vault.IsLocked(shown));
            Assert.True(vault.IsLocked(null));
            Assert.False(vault.IsLocked(other));
            Assert.Equal([shown], vault.LockedFolders());

            vault.SetLocked(null, false);
            Assert.False(vault.IsLocked(null));
        }

        // Kept in the file.
        SqliteConnection.ClearAllPools();
        using (var vault = Open())
        {
            Assert.True(vault.IsLocked(shown));

            // The folder deleted, its lock is: the next folder is given its id.
            vault.DeleteFolder(shown, everything: true);
            Assert.Empty(vault.LockedFolders());
            long next = vault.CreateFolder(null, "Next");
            Assert.False(vault.IsLocked(next));

            // A folder that is not there cannot be locked.
            Assert.ThrowsAny<Exception>(() => vault.SetLocked(987654, true));
        }
    }

    [CorpusFact]
    public void A_folder_deleted_alone_counts_those_of_its_folders_that_took_another_name_at_the_top()
    {
        if (!game.Available)
            return;

        using var vault = Open();
        long charms = vault.CreateFolder(null, "Charms");
        vault.CreateFolder(null, "Small");
        vault.CreateFolder(charms, "Small");
        vault.CreateFolder(charms, "Grand");

        // Two lifted to the top; the top had a Small already, so one of them is "Small (2)" there - counted, for
        // the page to say.
        var done = vault.DeleteFolder(charms, everything: false);
        Assert.Equal(2, done.Folders);
        Assert.Equal(1, done.Renamed);
        Assert.Contains(vault.Folders(), folder => folder is { Name: "Small (2)", ParentId: null });
        Assert.Contains(vault.Folders(), folder => folder is { Name: "Grand", ParentId: null });
    }
}
