using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Saves;

/// <summary>
/// A patch within a season that widens a stat (EncodingRepair),
/// played out on copies: a character and a vault written under an earlier build's tables - the installed ones with one
/// stat two bits wider - are found by the survey to be that build's, and the repair writes them in the installed
/// encoding with every value as it was.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class EncodingRepairTests(Game game) : IDisposable
{
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "pd2spc-encoding-repair-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_scratch, recursive: true); } catch (IOException) { }
    }

    private static List<Item> ItemsOf(D2S save) =>
        [.. save.PlayerItemList.Items.Concat(save.MercenaryItemList?.ItemList?.Items ?? []).SelectMany(item => item.SocketedItems.Prepend(item))];

    private static string Values(IEnumerable<Item> items) =>
        string.Join("|", items.Where(item => !item.IsCompact)
            .Select(item => item.Code + ":" + string.Join(";", ItemStats.Every(item).Select(stat => $"{stat.Stat}={stat.Value}/{stat.Param}"))));

    /// <summary>
    /// The earlier build: the installed tables (the test's own, where it lays a season's over the install) with
    /// <paramref name="stat"/>'s field two bits wider - as a patch would have left it, read back from the other side -
    /// and, where one is named, a row the installed tables do not have.
    /// </summary>
    private string EarlierBuild(Season installed, string stat, (string Name, int Id)? gone = null)
    {
        string folder = Path.Combine(_scratch, "builds", "earlier");
        if (Game.Tables is { } tables)
        {
            foreach (string file in Directory.EnumerateFiles(tables, "*", SearchOption.AllDirectories))
            {
                string to = Path.Combine(folder, Path.GetRelativePath(tables, file));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(file, to);
            }
        }

        string path = GameData.ExcelPath("itemstatcost");
        var lines = System.Text.Encoding.Latin1.GetString(installed.Game.Read(path)!.Value.Data).Split("\r\n").ToList();
        var head = lines[0].Split('\t').ToList();
        // Every season's width of it: the items read by the season before's columns as well as by this one's.
        int name = head.IndexOf("Stat");
        var widths = head.Select((column, at) => (column, at)).Where(each => each.column.StartsWith("Save Bits")).Select(each => each.at).ToList();
        for (int at = 1; at < lines.Count; at++)
        {
            var cells = lines[at].Split('\t');
            if (cells.Length > widths.Max() && cells[name] == stat)
            {
                foreach (int bits in widths.Where(bits => int.TryParse(cells[bits], out _)))
                    cells[bits] = (int.Parse(cells[bits]) + 2).ToString();
                lines[at] = string.Join('\t', cells);

                if (gone is { } row)
                {
                    cells[name] = row.Name;
                    cells[head.IndexOf("ID")] = row.Id.ToString();
                    lines.Insert(++at, string.Join('\t', cells));
                }
            }
        }

        string target = Path.Combine(folder, path.Replace('\\', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(target, System.Text.Encoding.Latin1.GetBytes(string.Join("\r\n", lines)));
        return folder;
    }

    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void A_character_and_a_vault_written_by_an_earlier_build_are_found_and_repaired_with_every_value_kept()
    {
        if (!game.Available || GameProcess.IsRunningFrom(Game.Root))
            return;

        string save = Game.CopyOfCharacter(_scratch);
        string vaultPath = Path.Combine(_scratch, "Test.pd2lib");
        var installed = Season.Open(Game.Root, Game.Tables);
        string season = SeasonSnapshot.NameFor(installed);
        try
        {
            // The character as the installed build reads it, and the stat most of its items hold, to widen.
            var (character, _) = SaveVerifier.Load(save);
            string before = Values(ItemsOf(character));
            string widened = ItemsOf(character).Where(item => !item.IsCompact).SelectMany(ItemStats.Every)
                .GroupBy(stat => stat.Stat).OrderByDescending(group => group.Count()).First().Key!;
            var kept = ItemsOf(character).Where(item => !item.IsCompact && item.SocketedItems.Count == 0).Take(5).ToList();
            string keptBefore = Values(kept);

            // Written by the earlier build: the character, and a vault of five of its items.
            var earlier = new EncodingRepair.Earlier("test build", EarlierBuild(installed, widened), SameSeason: true);
            using (Season.Open(Game.Root, earlier.Folder, announce: false))
            {
                File.WriteAllBytes(save, SaveWriter.Prepared(character));
                using var vault = Vault.Open(vaultPath, season, 10);
                foreach (var item in kept)
                    Assert.NotNull(vault.Add(item, out _));
            }
            installed.Reinstall();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            // B: both found the earlier build's.
            var survey = EncodingRepair.Survey(installed, [save, vaultPath], [earlier]);
            Assert.All(survey, finding => Assert.Equal(EncodingRepair.Verdict.Repairable, finding.Verdict));
            Assert.All(survey, finding => Assert.Equal("test build", finding.By?.Name));

            // C: written in the installed encoding, every value as it was, a backup of each.
            var repaired = EncodingRepair.Repair(installed, survey);
            Assert.All(repaired, each => Assert.Null(each.Refused));
            Assert.All(repaired, each => Assert.NotNull(each.Backup));

            var (again, result) = SaveVerifier.Load(save);
            Assert.True(result.RoundTripped);
            Assert.Equal(before, Values(ItemsOf(again)));

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            using (var vault = Vault.Open(vaultPath, season, 10))
            {
                var items = vault.RowsOf(vault.AllKeys()).Select(row => ItemBlob.Restore(row.Blob, 10)).ToList();
                Assert.Equal(keptBefore, Values(items));
            }

            // And now they read as they are.
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Assert.All(EncodingRepair.Survey(installed, [save, vaultPath], [earlier]),
                       finding => Assert.Equal(EncodingRepair.Verdict.Reads, finding.Verdict));
        }
        finally
        {
            installed.Reinstall();
        }
    }

    private static List<Item> ItemsOf(SharedStash stash) =>
        [.. stash.Items.Items.SelectMany(item => item.SocketedItems.Prepend(item))];

    private string CopyOfStash()
    {
        Directory.CreateDirectory(_scratch);
        string to = Path.Combine(_scratch, "pd2_shared.stash");
        File.Copy(Path.Combine(Game.Corpus, "saves", "pd2_shared.stash"), to);
        return to;
    }

    /// <summary>The corpus stash as an earlier build wrote it, the stat most of its items hold two bits wider there.</summary>
    private EncodingRepair.Earlier WrittenByAnEarlierBuild(Season installed, string stash, Action<SharedStash>? before = null, (string Name, int Id)? gone = null)
    {
        var read = SharedStash.Read(stash);
        string widened = ItemsOf(read).Where(item => !item.IsCompact).SelectMany(ItemStats.Every)
            .GroupBy(stat => stat.Stat).OrderByDescending(group => group.Count()).First().Key!;
        before?.Invoke(read);

        var earlier = new EncodingRepair.Earlier("test build", EarlierBuild(installed, widened, gone), SameSeason: true);
        using (Season.Open(Game.Root, earlier.Folder, announce: false))
            File.WriteAllBytes(stash, read.Write());
        installed.Reinstall();
        return earlier;
    }

    /// <summary>The header past its size and checksum, which a write puts right: the account's statistics and gold, what the items are not.</summary>
    private static byte[] HeaderOf(string stash) => File.ReadAllBytes(stash)[0x10..SharedStash.Read(stash).HeaderLength];

    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void A_shared_stash_written_by_an_earlier_build_is_found_and_repaired_with_every_value_kept()
    {
        if (!game.Available || GameProcess.IsRunningFrom(Game.Root))
            return;

        string stash = CopyOfStash();
        byte[] original = File.ReadAllBytes(stash);
        var installed = Season.Open(Game.Root, Game.Tables);
        try
        {
            string before = Values(ItemsOf(SharedStash.Read(stash)));
            byte[] header = HeaderOf(stash);
            uint gold = SharedStash.Read(stash).Gold;

            var earlier = WrittenByAnEarlierBuild(installed, stash);
            byte[] written = File.ReadAllBytes(stash);
            Assert.NotEqual(original, written);

            // B: found the earlier build's, its files in the list FilesOf gives.
            Assert.Contains(stash, EncodingRepair.FilesOf(_scratch, null));
            var finding = Assert.Single(EncodingRepair.Survey(installed, [stash], [earlier]));
            Assert.Equal(EncodingRepair.Verdict.Repairable, finding.Verdict);
            Assert.Equal("test build", finding.By?.Name);
            Assert.True(finding.Items > 0);
            Assert.Equal(written, File.ReadAllBytes(stash));

            // C: written in the installed encoding, every value as it was, and a backup of what it was.
            var repaired = Assert.Single(EncodingRepair.Repair(installed, [finding]));
            Assert.Null(repaired.Refused);
            Assert.Equal(0, repaired.Restamped);
            Assert.Equal(finding.Items, repaired.Items);
            Assert.Equal(written, File.ReadAllBytes(repaired.Backup!));

            var again = SharedStash.Read(stash);
            Assert.False(again.LastSeason);
            Assert.Equal(before, Values(ItemsOf(again)));
            Assert.Equal(gold, again.Gold);
            Assert.Equal(header, HeaderOf(stash));
            Assert.Equal(original, File.ReadAllBytes(stash));

            // And now it reads as it is.
            Assert.Equal(EncodingRepair.Verdict.Reads, Assert.Single(EncodingRepair.Survey(installed, [stash], [earlier])).Verdict);
        }
        finally
        {
            installed.Reinstall();
        }
    }

    [CorpusFact]
    public void A_shared_stash_with_nothing_wrong_is_left_alone()
    {
        if (!game.Available || GameProcess.IsRunningFrom(Game.Root))
            return;

        string stash = CopyOfStash();
        byte[] original = File.ReadAllBytes(stash);
        var installed = Season.Open(Game.Root, Game.Tables);
        try
        {
            var finding = Assert.Single(EncodingRepair.Survey(installed, [stash], []));
            Assert.Equal(EncodingRepair.Verdict.Reads, finding.Verdict);
            Assert.Empty(EncodingRepair.Repair(installed, [finding]));
            Assert.Equal(original, File.ReadAllBytes(stash));
        }
        finally
        {
            installed.Reinstall();
        }
    }

    /// <summary>
    /// An item holding a stat the installed tables have no row for: the writer has nothing to write it by. That file is
    /// refused, said, and left as it was - and the next file in the list is still repaired.
    /// </summary>
    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void A_file_that_will_not_write_is_refused_and_left_and_the_others_still_repaired()
    {
        if (!game.Available || GameProcess.IsRunningFrom(Game.Root))
            return;

        string stash = CopyOfStash();
        var installed = Season.Open(Game.Root, Game.Tables);
        try
        {
            var gone = ("gone_property", 1000);
            var earlier = WrittenByAnEarlierBuild(installed, stash, gone: gone, before: read =>
                ItemStats.Home(read.Items.Items.First(item => !item.IsCompact)).Stats.Add(new ItemStat { Stat = gone.Item1, Id = (ushort)gone.Item2, Value = 1 }));
            byte[] written = File.ReadAllBytes(stash);

            string other = Path.Combine(_scratch, "other.stash");
            File.Copy(Path.Combine(Game.Corpus, "saves", "pd2_shared.stash"), other);
            string otherBefore = Values(ItemsOf(SharedStash.Read(other)));
            WrittenByAnEarlierBuild(installed, other, gone: gone);

            var findings = EncodingRepair.Survey(installed, [stash, other], [earlier]);
            Assert.All(findings, finding => Assert.Equal(EncodingRepair.Verdict.Repairable, finding.Verdict));

            var repaired = EncodingRepair.Repair(installed, findings);

            Assert.Equal(2, repaired.Count);
            Assert.NotNull(repaired[0].Refused);
            Assert.Null(repaired[0].Backup);
            Assert.Equal(written, File.ReadAllBytes(stash));

            Assert.Null(repaired[1].Refused);
            Assert.Equal(otherBefore, Values(ItemsOf(SharedStash.Read(other))));
        }
        finally
        {
            installed.Reinstall();
        }
    }

    /// <summary>
    /// A vault with one item the installed tables cannot write, in the middle. Refused whole,
    /// no row written, its copy named - and the file still reads as the earlier build's, so it can be looked at again.
    /// </summary>
    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void A_vault_with_one_item_that_will_not_write_is_refused_whole_with_its_backup_named()
    {
        if (!game.Available || GameProcess.IsRunningFrom(Game.Root))
            return;

        string save = Game.CopyOfCharacter(_scratch);
        string vaultPath = Path.Combine(_scratch, "Test.pd2lib");
        var installed = Season.Open(Game.Root, Game.Tables);
        string season = SeasonSnapshot.NameFor(installed);
        try
        {
            var (character, _) = SaveVerifier.Load(save);
            var kept = ItemsOf(character).Where(item => !item.IsCompact && item.SocketedItems.Count == 0).Take(5).ToList();
            string widened = kept.SelectMany(ItemStats.Every).GroupBy(stat => stat.Stat).OrderByDescending(group => group.Count()).First().Key!;
            var gone = ("gone_property", 1000);

            // Written by the earlier build, the third of the five holding a stat the installed tables have no row for.
            var earlier = new EncodingRepair.Earlier("test build", EarlierBuild(installed, widened, gone), SameSeason: true);
            ItemStats.Home(kept[2]).Stats.Add(new ItemStat { Stat = gone.Item1, Id = (ushort)gone.Item2, Value = 1 });
            using (Season.Open(Game.Root, earlier.Folder, announce: false))
            {
                using var vault = Vault.Open(vaultPath, season, 10);
                foreach (var item in kept)
                    Assert.NotNull(vault.Add(item, out _));
            }
            installed.Reinstall();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            byte[][] Blobs()
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                using var vault = Vault.Open(vaultPath, season, 10);
                return [.. vault.RowsOf(vault.AllKeys()).Select(row => row.Blob)];
            }

            var before = Blobs();
            Assert.Equal(5, before.Length);

            var survey = EncodingRepair.Survey(installed, [vaultPath], [earlier]);
            Assert.Equal(EncodingRepair.Verdict.Repairable, Assert.Single(survey).Verdict);

            var repaired = Assert.Single(EncodingRepair.Repair(installed, survey));
            Assert.NotNull(repaired.Refused);
            Assert.True(File.Exists(repaired.Backup), $"no copy named: {repaired.Backup}");

            Assert.Equal(before, Blobs());

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Assert.Equal(EncodingRepair.Verdict.Repairable, Assert.Single(EncodingRepair.Survey(installed, [vaultPath], [earlier])).Verdict);
        }
        finally
        {
            installed.Reinstall();
        }
    }

    /// <summary>
    /// An item whose stamp the installed tables read by no column, and which no encoding kept names either: it takes
    /// this season's stamp, and is no one's legacy, there being no season to say it is of.
    /// </summary>
    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void An_item_of_neither_season_takes_this_seasons_stamp_with_no_record()
    {
        if (!game.Available || GameProcess.IsRunningFrom(Game.Root))
            return;

        string stash = CopyOfStash();
        var installed = Season.Open(Game.Root, Game.Tables);
        Item? odd = null;
        try
        {
            var earlier = WrittenByAnEarlierBuild(installed, stash, read =>
            {
                odd = read.Items.Items.First(item => !item.IsCompact && item.Version == Game.Stamp || !item.IsCompact);
                odd.Version = "101";
            });
            string code = odd!.Code;
            uint id = odd.Id;

            var finding = Assert.Single(EncodingRepair.Survey(installed, [stash], [earlier]));
            Assert.Equal(EncodingRepair.Verdict.Repairable, finding.Verdict);

            var repaired = Assert.Single(EncodingRepair.Repair(installed, [finding]));
            Assert.Null(repaired.Refused);
            Assert.Equal(0, repaired.Restamped);

            var after = SharedStash.Read(stash).Items.Items.First(item => !item.IsCompact && item.Id == id && item.Code == code);
            Assert.Equal(SeasonReading.CurrentStamp, after.Version);
            Assert.Null(LegacyRecord.SeasonOf(after));
        }
        finally
        {
            installed.Reinstall();
        }
    }

    /// <summary>
    /// A season that ships an older season's widths no more: the items' stamps are the ones the encoding that wrote them
    /// knew, and none the installed tables read. Each takes this season's stamp, fitted, with its values - and stays a
    /// legacy item of the season it was, by the record. A season that has not happened is played by giving the reader a
    /// stamp of its own, in a finally put back.
    /// </summary>
    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void Items_no_column_reads_take_this_seasons_stamp_and_stay_legacy_by_the_record()
    {
        if (!game.Available || GameProcess.IsRunningFrom(Game.Root))
            return;

        const string NextSeason = "106";
        string stash = CopyOfStash();
        var installed = Season.Open(Game.Root, Game.Tables);
        var recorded = new List<Item>();
        try
        {
            var read = SharedStash.Read(stash);
            string now = SeasonReading.CurrentStamp!, before = SeasonReading.PreviousStamp!;
            var seasonOf = new Dictionary<(uint, string), string?>();
            foreach (var item in ItemsOf(read).Where(item => !item.IsCompact))
                seasonOf[(item.Id, item.Code)] = item.Version == now ? Game.Season : item.Version == before ? D2SLib.Model.TXT.StatEncoding.PreviousSeason : null;
            string values = Values(ItemsOf(read));

            var earlier = WrittenByAnEarlierBuild(installed, stash);
            var finding = Assert.Single(EncodingRepair.Survey(installed, [stash], [earlier]));
            Assert.Equal(EncodingRepair.Verdict.Repairable, finding.Verdict);

            SeasonReading.SetStamps(NextSeason, null);
            var repaired = Assert.Single(EncodingRepair.Repair(installed, [finding]));

            Assert.Null(repaired.Refused);
            Assert.Equal(ItemsOf(read).Count(item => !item.IsCompact && seasonOf[(item.Id, item.Code)] is not null), repaired.Restamped);
            Assert.True(repaired.Restamped > 0);

            var again = SharedStash.Read(stash);
            recorded.AddRange(ItemsOf(again));
            Assert.All(ItemsOf(again).Where(item => !item.IsCompact), item =>
            {
                Assert.Equal(NextSeason, item.Version);
                Assert.Equal(seasonOf[(item.Id, item.Code)], LegacyRecord.SeasonOf(item));
                if (seasonOf[(item.Id, item.Code)] is not null)
                    Assert.True(ItemSeason.IsLegacy(item));
            });

            Assert.Equal(values, Values(ItemsOf(again)));
        }
        finally
        {
            // The record is the process's, and a corpus item on it would be a legacy item to every test after this one.
            foreach (var item in recorded.Where(item => !item.IsCompact))
                LegacyRecord.Forget(item);
            installed.Reinstall();
        }
    }
}
