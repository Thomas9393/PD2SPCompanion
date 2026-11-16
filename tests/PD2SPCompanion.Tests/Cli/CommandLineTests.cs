using System.Diagnostics;
using D2SLib.Model.Save;
using Microsoft.Data.Sqlite;

namespace PD2SPCompanion.Tests.Cli;

/// <summary>
/// The command line tool itself, run as a player runs it, on copies in a scratch folder: what
/// it refuses, what it leaves alone, and what it writes. A refusal comes before the tables are opened; the rest open the
/// installed game's, read only, and are the slow kind.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class CommandLineTests(Game game) : IDisposable
{
    private readonly string _scratch = Directory.CreateTempSubdirectory("pd2spc-cli-").FullName;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_scratch, recursive: true);
    }

    private static string Tool([System.Runtime.CompilerServices.CallerFilePath] string here = "")
    {
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        string repo = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", ".."));
        return Path.Combine(repo, "src", "PD2SPCompanion.Cli", "bin", configuration, "net10.0", "PD2SPCompanion.Cli.exe");
    }

    private static (int Code, string Output) Run(string[] words, string? fakeGame = null, bool noGameGiven = false)
    {
        var start = new ProcessStartInfo(Tool()) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string word in words)
            start.ArgumentList.Add(word);
        start.Environment["PD2SPC_FAKE_GAME"] = fakeGame ?? Path.Combine(Path.GetTempPath(), $"no-game-{Guid.NewGuid():N}");

        // The tool takes no location but --game and PD2SPC_GAME. A refusal comes before the tables are opened, so a folder
        // that does not exist will do for those where no game is known.
        start.Environment.Remove("PD2SPC_GAME");
        if (!noGameGiven)
            start.Environment["PD2SPC_GAME"] = Game.Root.Length > 0 ? Game.Root : Path.Combine(Path.GetTempPath(), $"no-game-{Guid.NewGuid():N}");

        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output + errors.GetAwaiter().GetResult());
    }

    private string Copy(string name, params string[] corpusPath)
    {
        string to = Path.Combine(_scratch, name);
        File.Copy(Path.Combine([Game.Corpus, .. corpusPath]), to);
        return to;
    }

    private string Hero() => Copy("Hero.d2s", "saves", "PhySpearZon.d2s");

    /// <summary>Season 13's stamp on its items: the first as well.</summary>
    private string LegacyHero() => Copy("Old.d2s", "sheet", "ingame-2026-10-31", "LegacyTestCopies.d2s");

    // ---- thincheck ---------------------------------------------------------------------------------------------

    [Fact]
    public void Thincheck_leaves_the_folder_it_is_named_as_it_was()
    {
        string folder = Directory.CreateDirectory(Path.Combine(_scratch, "named")).FullName;
        string kept = Path.Combine(folder, "keep.d2s");
        File.WriteAllBytes(kept, [1, 2, 3]);
        Directory.CreateDirectory(Path.Combine(folder, "PD2SPCompanion Backups"));

        var (code, output) = Run(["thincheck", folder]);

        Assert.True(code == 0, output);
        Assert.Contains("all passed", output);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(kept));
        Assert.Equal(["keep.d2s", "PD2SPCompanion Backups"], Directory.GetFileSystemEntries(folder).Select(Path.GetFileName).Order());
    }

    // ---- armory, armorybuild -----------------------------------------------------------------------------------

    [Theory]
    [InlineData("armory", "SomeName")]
    [InlineData("armorybuild", "some.json")]
    public void An_armory_command_refuses_an_out_that_exists(string command, string what)
    {
        string out_ = Path.Combine(_scratch, "Hammerdin.d2s");
        File.WriteAllBytes(out_, [7, 7, 7]);

        var (code, output) = Run([command, what, "--template", "t.d2s", "--out", out_]);

        Assert.True(code == 2, output);
        Assert.Contains("nothing is written over a character", output);
        Assert.Equal(new byte[] { 7, 7, 7 }, File.ReadAllBytes(out_));
    }

    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void Armorybuild_writes_a_new_character_and_reads_it_back()
    {
        if (!game.Available)
            return;

        string armory = Path.Combine(Game.Corpus, "aliases", "ladder-1--ladder-s14-softcore.json");
        string out_ = Path.Combine(_scratch, "Built.d2s");

        var (code, output) = Run(["armorybuild", armory, "--template", Hero(), "--out", out_]);

        Assert.True(code == 0, output);
        Assert.Contains("reads back as", output);
        Assert.True(File.Exists(out_));
        Assert.False(File.Exists(out_ + SaveWriter.PendingSuffix));
    }

    // ---- give and its kin --------------------------------------------------------------------------------

    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void Give_writes_as_every_save_is_written_and_keeps_its_own_backup()
    {
        if (!game.Available)
            return;

        string save = Hero();
        byte[] before = File.ReadAllBytes(save);
        string kept = Path.Combine(_scratch, "kept");

        var (code, output) = Run(["give", save, "--base", "cap:0", "--backup", kept]);

        Assert.True(code == 0, output);
        Assert.Contains("backup: ", output);
        Assert.Equal(before, File.ReadAllBytes(Assert.Single(Directory.GetFiles(kept))));

        // The character's own backup folder, and the bytes as written, as the app's list and its offer know them.
        Assert.Contains(SaveWriter.BackupsOf(save), file => before.AsSpan().SequenceEqual(File.ReadAllBytes(file.FullName)));
        Assert.Equal(File.ReadAllBytes(save), File.ReadAllBytes(SaveWriter.AsWrittenPath(save)));
    }

    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void What_give_makes_is_this_seasons_on_a_character_whose_first_item_is_legacy()
    {
        if (!game.Available || Game.Tables is not null || Game.Season != "S14")
            return;

        var (code, output) = Run(["give", LegacyHero(), "--base", "cap:0", "--backup", Path.Combine(_scratch, "kept")]);

        Assert.True(code == 0, output);
        Assert.Contains("v105", output);
        Assert.DoesNotContain("v103", output);
    }

    // ---- corrupt --merc ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("--merc")]
    [InlineData("--as-quality", "Rare")]
    public void Corrupt_refuses_what_only_goes_with_list(params string[] extra)
    {
        string save = Path.Combine(_scratch, "Hero.d2s");
        File.WriteAllBytes(save, [1, 2, 3]);

        var (code, output) = Run(["corrupt", save, "--item", "2", "--id", "300", .. extra]);

        Assert.True(code == 2, output);
        Assert.Contains("only go with --list", output);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(save));
    }

    // ---- migratefiles --out -----------------------------------------------------------------------------------

    [Fact]
    public void Migratefiles_refuses_an_out_where_a_stash_would_land_on_one()
    {
        string corpus = Directory.CreateDirectory(Path.Combine(_scratch, "corpus", "a")).Parent!.FullName;
        File.WriteAllBytes(Path.Combine(corpus, "a", "x.sss"), [1]);
        string into = Directory.CreateDirectory(Path.Combine(_scratch, "into", "a")).Parent!.FullName;
        File.WriteAllBytes(Path.Combine(into, "a", "x.sss"), [2]);

        var (code, output) = Run(["migratefiles", "--from", _scratch, "--corpus", corpus, "--out", into]);

        Assert.True(code == 2, output);
        Assert.Contains("nothing is written over it", output);
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(Path.Combine(into, "a", "x.sss")));
    }

    [Fact]
    public void Migratefiles_refuses_an_out_naming_its_source()
    {
        string corpus = Directory.CreateDirectory(Path.Combine(_scratch, "corpus")).FullName;
        File.WriteAllBytes(Path.Combine(corpus, "x.sss"), [1]);

        var (code, output) = Run(["migratefiles", "--from", _scratch, "--corpus", corpus, "--out", corpus]);

        Assert.True(code == 2, output);
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(corpus, "x.sss")));
    }

    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void Migratefiles_keeps_two_of_one_name_apart_under_out()
    {
        string stash = Path.Combine(Game.Corpus, "packs", "tdlofcc-s12", "PD2-PlugY-main", "Save", "Library", "_LOD_SharedStashSave.sss");
        string tables = Path.Combine(Game.Corpus, "packs", "tdlofcc-s12", "PD2-PlugY-main", "ProjectD2", "data", "global", "excel", "modpacks", "official");
        if (!game.Available || !File.Exists(stash) || !Directory.Exists(tables))
            return;

        string corpus = Path.Combine(_scratch, "corpus");
        foreach (string part in new[] { "one", "two" })
        {
            Directory.CreateDirectory(Path.Combine(corpus, part));
            File.Copy(stash, Path.Combine(corpus, part, Path.GetFileName(stash)));
        }
        string into = Path.Combine(_scratch, "into");

        var (code, output) = Run(["migratefiles", "--from", tables, "--corpus", corpus, "--out", into, "--stamp", "105"]);

        Assert.True(code == 0, output);
        Assert.True(File.Exists(Path.Combine(into, "one", Path.GetFileName(stash))), output);
        Assert.True(File.Exists(Path.Combine(into, "two", Path.GetFileName(stash))), output);
    }

    // ---- --game and --tables taken for a path -------------------------------------------------------------------

    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void A_list_command_does_not_take_the_game_folder_for_one_of_its_paths()
    {
        if (!game.Available)
            return;

        var (quests, said) = Run(["quests", Hero(), "--game", Game.Root]);
        Assert.True(quests == 0, said);

        string empty = Directory.CreateDirectory(Path.Combine(_scratch, "empty")).FullName;
        var (leftoff, told) = Run(["leftoff", empty, "--game", Game.Root]);
        Assert.True(leftoff == 0, told);
        Assert.Contains("0 listings rebuilt", told);
    }

    // ---- no game folder given ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("info")]
    [InlineData("density", "5")]
    [InlineData("vaultrename", "v.pd2lib", "Old", "New")]
    public void A_command_that_reads_the_game_asks_for_the_folder(params string[] words)
    {
        var (code, output) = Run(words, noGameGiven: true);

        Assert.True(code == 2, output);
        Assert.Contains("--game", output);
        Assert.Contains("PD2SPC_GAME", output);
        Assert.Contains("usage:", output);
    }

    [Fact]
    public void A_command_that_does_not_read_the_game_goes_on_with_none_given()
    {
        var (code, output) = Run(["thincheck", _scratch], noGameGiven: true);

        Assert.True(code == 0, output);
    }

    [Fact]
    public void With_no_folder_given_any_game_running_refuses_a_command_that_writes()
    {
        string running = Path.Combine(_scratch, "running");
        File.WriteAllBytes(running, [1]);
        string folder = Directory.CreateDirectory(Path.Combine(_scratch, "named")).FullName;

        var (code, output) = Run(["thincheck", folder], fakeGame: running, noGameGiven: true);

        Assert.True(code == 1, output);
        Assert.Contains("refused", output);
    }

    [Fact]
    public void A_game_folder_no_path_can_be_made_of_is_wrong_usage_and_no_crash()
    {
        var (code, output) = Run(["thincheck", _scratch, "--game", "  "], noGameGiven: true);

        Assert.True(code == 2, output);
        Assert.Contains("usage:", output);
    }

    // ---- vaultrename ------------------------------------------------------------------------------------------

    [Fact]
    public void Vaultrename_makes_no_vault_at_a_mistyped_path()
    {
        string path = Path.Combine(_scratch, "mistyped.pd2lib");

        var (code, output) = Run(["vaultrename", path, "Old", "New"]);

        Assert.True(code == 2, output);
        Assert.False(File.Exists(path));
    }

    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void Vaultrename_keeps_the_search_rows_of_a_vault_that_wants_them_rebuilt()
    {
        if (!game.Available)
            return;

        string vault = Path.Combine(_scratch, "v.pd2lib");
        var (added, said) = Run(["vault", "--file", vault, "--add", Hero()]);
        Assert.True(added == 0, said);

        long Rows()
        {
            using var db = new SqliteConnection($"Data Source={vault}");
            db.Open();
            using var count = db.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM item_meta";
            return Convert.ToInt64(count.ExecuteScalar());
        }

        long before = Rows();
        Assert.True(before > 0);

        // As one last opened by an older build: the rows are to be made again on the next open.
        using (var db = new SqliteConnection($"Data Source={vault}"))
        {
            db.Open();
            using var stale = db.CreateCommand();
            stale.CommandText = "UPDATE vault SET projection_version = 0 WHERE id = 1";
            Assert.Equal(1, stale.ExecuteNonQuery());
        }
        SqliteConnection.ClearAllPools();

        var (code, output) = Run(["vaultrename", vault, "Old", "New"]);

        Assert.True(code == 0, output);
        Assert.Contains("moved", output);
        Assert.Equal(before, Rows());
    }

    // ---- the game's gate on a check that writes -----------------------------------------------------------------

    [Theory]
    [InlineData("thincheck")]
    [InlineData("safewritecheck")]
    public void A_check_writing_to_the_game_folder_is_refused_while_the_game_runs(string command)
    {
        string root = Directory.CreateDirectory(Path.Combine(_scratch, "game")).FullName;
        string saves = Directory.CreateDirectory(Path.Combine(root, "Save")).FullName;
        string kept = Path.Combine(saves, "Hero.d2s");
        File.WriteAllBytes(kept, [1]);
        string running = Path.Combine(_scratch, "running");
        File.WriteAllBytes(running, [1]);

        var (code, output) = Run([command, saves, "--game", root], fakeGame: running);

        Assert.True(code == 1, output);
        Assert.Contains("refused", output);
        Assert.Equal([kept], Directory.GetFileSystemEntries(saves));
    }

    [Fact]
    public void A_check_writing_elsewhere_goes_on_while_the_game_runs()
    {
        string root = Directory.CreateDirectory(Path.Combine(_scratch, "game")).FullName;
        string elsewhere = Directory.CreateDirectory(Path.Combine(_scratch, "elsewhere")).FullName;
        string running = Path.Combine(_scratch, "running");
        File.WriteAllBytes(running, [1]);

        var (code, output) = Run(["thincheck", elsewhere, "--game", root], fakeGame: running);

        Assert.True(code == 0, output);
    }

    [Fact]
    public void Lastseason_refuses_an_out_that_exists()
    {
        string out_ = Path.Combine(_scratch, "x.d2s");
        File.WriteAllBytes(out_, [3]);

        var (code, output) = Run(["lastseason", "in.d2s", "--out", out_]);

        Assert.True(code == 2, output);
        Assert.Equal(new byte[] { 3 }, File.ReadAllBytes(out_));
    }

    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void Lastseason_writes_a_stash_in_last_seasons_encoding_though_the_stamps_are_known()
    {
        if (!game.Available || !SeasonReading.HasPreviousSeason)
            return;

        // The pad's: its items are this season's, by their stamp, where the corpus's own stash holds last season's.
        string stash = Copy("pd2_shared.stash", "ui-pad", "pad", "saves", "pd2_shared.stash");
        string out_ = Path.Combine(_scratch, "old.stash");

        var (code, output) = Run(["lastseason", stash, "--out", out_]);
        Assert.True(code == 0, output);

        SeasonReading.SetStamps(null, null);
        try
        {
            Assert.True(SharedStash.Read(out_).LastSeason);
        }
        finally
        {
            Game.UseStamps();
        }
    }

    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void Armorystress_writes_over_no_character()
    {
        if (!game.Available)
            return;

        string armory = Path.Combine(Game.Corpus, "aliases", "ladder-1--ladder-s14-softcore.json");
        string name = Path.GetFileNameWithoutExtension(armory);
        string folder = Directory.CreateDirectory(Path.Combine(_scratch, "stress")).FullName;
        File.Copy(armory, Path.Combine(folder, name + ".json"));

        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(armory));
        string character = new ArmoryImport.Armory(document.RootElement.Clone()).Name;
        string there = Path.Combine(folder, character + ".d2s");
        File.WriteAllBytes(there, [9]);

        var (code, output) = Run(["armorystress", name, "--template", Hero(), "--out", folder]);

        Assert.True(code == 1, output);
        Assert.Contains("nothing is written over a character", output);
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(there));
    }

    // ---- an edit makes a legacy item this season's ------------------------------------------------------------

    /// <summary>A gear item of Season 13's stamp, and its index, in the legacy character.</summary>
    private static (int Index, Item Item) LegacyGear(string save) =>
        SaveVerifier.Load(save).Save.PlayerItemList.Items.Select((item, index) => (index, item))
            .First(each => each.item.Version == "103" && !each.item.IsCompact && each.item.Quality == ItemQuality.Unique
                           && ItemTypes.IsGear(ItemTypes.TypeOf(each.item.Code)) && ItemBases.Of(each.item.Code) is not null);

    private static T WithStamps<T>(Func<T> run)
    {
        var kept = (SeasonReading.CurrentStamp, SeasonReading.PreviousStamp);
        SeasonReading.SetStamps("105", "103");
        try { return run(); }
        finally { SeasonReading.SetStamps(kept.CurrentStamp, kept.PreviousStamp); }
    }

    [CorpusFact]
    public void Addstat_makes_a_legacy_item_this_seasons()
    {
        if (!game.Available || Game.Tables is not null || Game.Season != "S14")
            return;

        string save = LegacyHero();
        WithStamps(() =>
        {
            var (index, _) = LegacyGear(save);
            SaveWriter.AddItemStat(save, index, 39, dryRun: false, value: 10);
            Assert.Equal("105", SaveVerifier.Load(save).Save.PlayerItemList.Items[index].Version);
            return 0;
        });
    }

    [CorpusFact]
    public void Set_makes_a_legacy_item_this_seasons()
    {
        if (!game.Available || Game.Tables is not null || Game.Season != "S14")
            return;

        string save = LegacyHero();
        WithStamps(() =>
        {
            var (index, item) = LegacyGear(save);
            var stat = item.StatLists.SelectMany(list => list.Stats).First(each => each.Value > 1 && each.Param is null or 0 && each.SkillId is null);
            SaveWriter.SetItemStat(save, index, stat.Stat, stat.Value - 1, dryRun: false);
            var back = SaveVerifier.Load(save).Save.PlayerItemList.Items[index];
            Assert.Equal("105", back.Version);
            Assert.Equal(stat.Value - 1, back.StatLists.SelectMany(list => list.Stats).First(each => each.Stat == stat.Stat).Value);
            return 0;
        });
    }

    [CorpusFact]
    public void Reroll_makes_a_legacy_item_this_seasons_and_a_dry_run_writes_nothing()
    {
        if (!game.Available || Game.Tables is not null || Game.Season != "S14")
            return;

        string save = LegacyHero();
        byte[] before = File.ReadAllBytes(save);
        WithStamps(() =>
        {
            var (index, _) = LegacyGear(save);
            SaveWriter.RerollItem(save, index, dryRun: true);
            Assert.Equal(before, File.ReadAllBytes(save));

            SaveWriter.RerollItem(save, index, dryRun: false);
            Assert.Equal("105", SaveVerifier.Load(save).Save.PlayerItemList.Items[index].Version);
            return 0;
        });
    }

    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void Ethereal_makes_a_legacy_item_this_seasons()
    {
        if (!game.Available || Game.Tables is not null || Game.Season != "S14")
            return;

        string save = LegacyHero();
        int index = WithStamps(() => LegacyGear(save).Index);

        var (code, output) = Run(["ethereal", save, "--item", index.ToString()]);

        Assert.True(code == 0, output);
        WithStamps(() =>
        {
            Assert.Equal("105", SaveVerifier.Load(save).Save.PlayerItemList.Items[index].Version);
            return 0;
        });
    }

    [CorpusFact]
    public void A_new_items_stamp_is_this_seasons_beside_a_legacy_first_item_and_the_files_beside_an_older_one()
    {
        if (!game.Available)
            return;

        WithStamps(() =>
        {
            D2S Holding(params string[] stamps)
            {
                var save = new D2S { PlayerItemList = new ItemList() };
                foreach (string stamp in stamps)
                    save.PlayerItemList.Items.Add(new Item { Version = stamp });
                return save;
            }

            Assert.Equal("105", NewCharacter.StampForNew(Holding("103", "105")));
            Assert.Equal("105", NewCharacter.StampForNew(Holding("105")));
            Assert.Equal("101", NewCharacter.StampForNew(Holding("101")));
            Assert.Equal("105", NewCharacter.StampForNew(Holding()));
            Assert.Equal("105", NewCharacter.ItemEra(Holding()));
            return 0;
        });
    }
}
