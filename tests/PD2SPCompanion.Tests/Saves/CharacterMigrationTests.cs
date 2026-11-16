using Xunit;

namespace PD2SPCompanion.Tests.Saves;

/// <summary>
/// A character of the season before brought into the installed one in place (<see cref="CharacterMigration"/>):
/// ShockSacri as S13 left it - 1,166,036 carried and 125,338 in its
/// own stash, Ferocity on its mercenary - with a real S14 shared stash, both copied. Season 14's own tables only:
/// it needs S13's snapshot for the old season's view, copied into the run's data folder from the player's.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class CharacterMigrationTests(Game game)
{
    private static string Specimens => Path.Combine(Game.Corpus, "specimens", "s14-launch-2026-10-23");

    private static string Snapshot13 => TestPaths.S13Tables;

    private bool Ready =>
        game.Available && Game.Tables is null && D2SLib.Model.TXT.StatEncoding.PreviousSeason == "S13"
        && File.Exists(Path.Combine(Specimens, "before", "ShockSacri.d2s")) && Directory.Exists(Snapshot13)
        && !GameProcess.IsRunningFrom(Game.Root);

    /// <summary>The two files in a folder of the test's own, S13's snapshot where the run's data folder keeps them.</summary>
    private static (string Save, string Stash) Copies()
    {
        string scratch = Path.Combine(Path.GetTempPath(), "pd2spc-charmig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        string save = Path.Combine(scratch, "ShockSacri.d2s"), stash = Path.Combine(scratch, "pd2_shared.stash");
        File.Copy(Path.Combine(Specimens, "before", "ShockSacri.d2s"), save);
        File.Copy(Path.Combine(Specimens, "session2", "pd2_shared.stash"), stash);

        string kept = SeasonSnapshot.FolderFor("S13");
        if (!Directory.Exists(kept))
        {
            foreach (string file in Directory.EnumerateFiles(Snapshot13, "*", SearchOption.AllDirectories))
            {
                string to = Path.Combine(kept, Path.GetRelativePath(Snapshot13, file));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(file, to);
            }
        }

        return (save, stash);
    }

    private static T WithStamps<T>(Func<T> run)
    {
        var kept = (SeasonReading.CurrentStamp, SeasonReading.PreviousStamp);
        SeasonReading.SetStamps("105", "103");
        try { return run(); }
        finally { SeasonReading.SetStamps(kept.CurrentStamp, kept.PreviousStamp); }
    }

    private static List<D2SLib.Model.Save.Item> ItemsOf(D2SLib.Model.Save.D2S save) =>
        [.. save.PlayerItemList.Items.Concat(save.MercenaryItemList?.ItemList?.Items ?? [])];

    private static string Lines(D2SLib.Model.Save.Item item) =>
        string.Join(";", ItemStats.Every(item).Select(stat => $"{stat.Stat}={stat.Value}/{stat.Param}"));

    [CorpusFact]
    public void Its_gold_goes_to_the_shared_stash_and_its_items_stay_as_they_are()
    {
        if (!Ready)
            return;

        var (save, stash) = Copies();
        using var season = Season.Open(Game.Root, announce: false);
        var before = ItemsOf(WithStamps(() => SaveVerifier.Load(save)).Save);
        var report = WithStamps(() => CharacterMigration.Run(season, new CharacterMigration.Request(save, stash, DryRun: false)));

        Assert.Equal(1_166_036 + 125_338, report.GoldMoved);
        Assert.Equal(0, report.GoldLeft);
        Assert.Equal(0, report.Count(SeasonMigration.Outcome.Failed));
        Assert.Equal(1_266_036u + 1_166_036 + 125_338, SharedStash.Read(stash).Gold);

        var (written, result) = WithStamps(() => SaveVerifier.Load(save));
        Assert.True(result.RoundTripped);
        Assert.DoesNotContain("gold", written.Attributes.Stats.Keys);
        Assert.DoesNotContain("goldbank", written.Attributes.Stats.Keys);

        // Legacy: every item at the stamp and with the lines S13 gave it (no migration).
        var items = ItemsOf(written);
        Assert.Equal(before.Select(item => item.Version), items.Select(item => item.Version));
        Assert.Contains(items, item => item.Version == "103");
        Assert.Equal(before.Select(Lines), items.Select(Lines));

        // Only what the game's first load does: the runeword takes this season's number (3261 in S13).
        var ferocity = items.Single(item => Runewords.On(item)?.Key == "Ferocity");
        Assert.Equal((uint)Runewords.IdOf(Runewords.On(ferocity)!)!.Value, ferocity.RunewordId);
    }

    [CorpusFact]
    public void Two_characters_at_once_add_their_gold_to_one_stash()
    {
        if (!Ready)
            return;

        // The Settings panel's every card in one run (RunAll): two of ShockSacri, the stash between them.
        var (save, stash) = Copies();
        string second = Path.Combine(Path.GetDirectoryName(save)!, "Second.d2s");
        File.Copy(save, second);

        using var season = Season.Open(Game.Root, announce: false);
        var done = WithStamps(() => CharacterMigration.RunAll(season,
            [new CharacterMigration.Request(save, stash, DryRun: false), new CharacterMigration.Request(second, stash, DryRun: false)]));

        Assert.All(done, each => Assert.Equal(1_166_036 + 125_338, each.Report!.GoldMoved));
        Assert.Equal(1_266_036u + 2u * (1_166_036 + 125_338), SharedStash.Read(stash).Gold);
    }

    /// <summary>
    /// SkeleMerc as S13 left it: a Necromancer, whose mana per level S14 raised from 2 to 3, with an Iron Golem
    /// made of a Pride - four runes socketed in it.
    /// </summary>
    private static (string Save, string Stash) SkeleMerc()
    {
        var (save, stash) = Copies();
        string skele = Path.Combine(Path.GetDirectoryName(save)!, "SkeleMerc.d2s");
        File.Copy(Path.Combine(Specimens, "before", "SkeleMerc.d2s"), skele);
        return (skele, stash);
    }

    [CorpusFact]
    public void Its_base_mana_moves_once_however_often_it_is_migrated()
    {
        if (!Ready || !File.Exists(Path.Combine(Specimens, "before", "SkeleMerc.d2s")))
            return;

        var (skele, stash) = SkeleMerc();
        using var season = Season.Open(Game.Root, announce: false);
        var first = WithStamps(() => CharacterMigration.Run(season, new CharacterMigration.Request(skele, stash, DryRun: false)));
        var again = WithStamps(() => CharacterMigration.Run(season, new CharacterMigration.Request(skele, stash, DryRun: false)));

        // 89 levels at one more mana each; moved again, it stays - not 381.
        Assert.Equal(first.Was.Mana + 89, first.Now.Mana);
        Assert.Equal(first.Now, again.Now);
        Assert.Equal(again.Was, again.Now);
    }

    [CorpusFact]
    public void Its_golems_item_stays_as_it_is_and_once_migrated_it_waits_no_more()
    {
        if (!Ready || !File.Exists(Path.Combine(Specimens, "before", "SkeleMerc.d2s")))
            return;

        var (skele, stash) = SkeleMerc();
        using var season = Season.Open(Game.Root, announce: false);
        Assert.True(WithStamps(() => CharacterMigration.Waits(SaveVerifier.Load(skele).Save, season.Game)));
        WithStamps(() => CharacterMigration.Run(season, new CharacterMigration.Request(skele, stash, DryRun: false)));

        var (save, _) = WithStamps(() => SaveVerifier.Load(skele));
        var golem = save.Golem.Item;
        Assert.NotEmpty(golem.SocketedItems);
        Assert.All(golem.SocketedItems.Prepend(golem), item => Assert.Equal("103", item.Version));

        // Its items still S13's, its gold moved and its base this season's: nothing left to do.
        Assert.False(WithStamps(() => CharacterMigration.Waits(save, season.Game)));
    }

    [CorpusFact]
    public void A_character_of_this_season_with_gold_waits_for_nothing()
    {
        // NewS, made in S14 - its own items at 105 beside an S13 Bonehew brought in - given gold: nothing for a migration
        // to do. Gold alone kept one offered for good (CorpseExp, made again in S14 under an S13 name,
        // carrying 99,288). ShockSacri as S13 left it, gold and all, still waits.
        if (!Ready)
            return;

        // The copy that holds the Bonehew: a character of this season with a legacy item is not one of the season before.
        var save = Directory.EnumerateFiles(Specimens, "NewS.d2s", SearchOption.AllDirectories).Order()
            .Select(file => WithStamps(() => SaveVerifier.Load(file)).Save)
            .FirstOrDefault(each => ItemsOf(each).Any(item => item.Version == "103")
                                    && ItemsOf(each).Any(item => item.Version == "105"));
        if (save is null)
            return;

        using var season = Season.Open(Game.Root, announce: false);
        save.Attributes.Stats[AttributeEditor.Carried] = 99_288;
        Assert.False(WithStamps(() => CharacterMigration.Waits(save, season.Game)));

        var (shock, _) = Copies();
        Assert.True(WithStamps(() => CharacterMigration.Waits(SaveVerifier.Load(shock).Save, season.Game)));
    }

    /// <summary>Two of ShockSacri and the stash: the requests of a run of the two.</summary>
    private static (string First, string Second, string Stash, CharacterMigration.Request[] Asked) Two()
    {
        var (save, stash) = Copies();
        string second = Path.Combine(Path.GetDirectoryName(save)!, "Second.d2s");
        File.Copy(save, second);
        return (save, second, stash, [new CharacterMigration.Request(save, stash, DryRun: false), new CharacterMigration.Request(second, stash, DryRun: false)]);
    }

    private const long ShockGold = 1_166_036 + 125_338, StashGold = 1_266_036;

    [CorpusFact]
    public void A_character_that_will_not_write_is_one_refusal_and_keeps_its_gold_and_the_others_keep_theirs()
    {
        if (!Ready)
            return;

        // B's file refuses. A is written and reported; B is as it was, its gold in its own file
        // and taken back out of the stash; nothing in neither place and nothing in both.
        var (first, second, stash, asked) = Two();
        byte[] secondBefore = File.ReadAllBytes(second);
        File.SetAttributes(second, FileAttributes.ReadOnly);
        try
        {
            using var season = Season.Open(Game.Root, announce: false);
            var done = WithStamps(() => CharacterMigration.RunAll(season, asked));

            Assert.Equal(ShockGold, done[0].Report!.GoldMoved);
            Assert.Null(done[1].Report);
            Assert.NotNull(done[1].Refused);
            Assert.Equal(secondBefore, File.ReadAllBytes(second));
            Assert.Equal((uint)(StashGold + ShockGold), SharedStash.Read(stash).Gold);

            var (written, _) = WithStamps(() => SaveVerifier.Load(first));
            Assert.DoesNotContain("gold", written.Attributes.Stats.Keys);
        }
        finally
        {
            File.SetAttributes(second, FileAttributes.Normal);
        }
    }

    [CorpusFact]
    public void A_stash_that_will_not_write_refuses_the_characters_whose_gold_it_was_to_take_and_changes_none()
    {
        if (!Ready)
            return;

        var (first, second, stash, asked) = Two();
        byte[] firstBefore = File.ReadAllBytes(first), secondBefore = File.ReadAllBytes(second), stashBefore = File.ReadAllBytes(stash);
        File.SetAttributes(stash, FileAttributes.ReadOnly);
        try
        {
            using var season = Season.Open(Game.Root, announce: false);
            var done = WithStamps(() => CharacterMigration.RunAll(season, asked));

            Assert.All(done, each =>
            {
                Assert.Null(each.Report);
                Assert.StartsWith("Shared stash not written: ", each.Refused);
            });
            Assert.Equal(firstBefore, File.ReadAllBytes(first));
            Assert.Equal(secondBefore, File.ReadAllBytes(second));
            Assert.Equal(stashBefore, File.ReadAllBytes(stash));
        }
        finally
        {
            File.SetAttributes(stash, FileAttributes.Normal);
        }
    }

    [CorpusFact]
    public void A_character_refused_before_writing_adds_nothing_to_the_stash()
    {
        if (!Ready)
            return;

        // Refused by the read-back, its file keeps the gold, and the stash does not take it too.
        var (_, second, stash, asked) = Two();
        byte[] secondBefore = File.ReadAllBytes(second);
        Environment.SetEnvironmentVariable(PatchGuard.FakeUnreadable, "Second.d2s");
        try
        {
            using var season = Season.Open(Game.Root, announce: false);
            var done = WithStamps(() => CharacterMigration.RunAll(season, asked));

            Assert.Equal(ShockGold, done[0].Report!.GoldMoved);
            Assert.Null(done[1].Report);
            Assert.Equal(secondBefore, File.ReadAllBytes(second));
            Assert.Equal((uint)(StashGold + ShockGold), SharedStash.Read(stash).Gold);
        }
        finally
        {
            Environment.SetEnvironmentVariable(PatchGuard.FakeUnreadable, null);
        }
    }

    /// <summary>The S13 snapshot out of the run's data folder for a while: a player who starts at this season.</summary>
    private sealed class NoSnapshot : IDisposable
    {
        private readonly string _folder = SeasonSnapshot.FolderFor("S13");

        public NoSnapshot()
        {
            if (Directory.Exists(_folder + ".aside"))
                Directory.Delete(_folder + ".aside", recursive: true);
            Directory.Move(_folder, _folder + ".aside");
        }

        public void Dispose() => Directory.Move(_folder + ".aside", _folder);
    }

    [CorpusFact]
    public void With_no_snapshot_the_base_moves_by_the_shipped_values_and_it_waits_no_more()
    {
        if (!Ready || !File.Exists(Path.Combine(Specimens, "before", "SkeleMerc.d2s")))
            return;

        // Deliberately, the base moves without a snapshot.
        var (skele, stash) = SkeleMerc();
        using var hidden = new NoSnapshot();
        using var season = Season.Open(Game.Root, announce: false);

        Assert.True(WithStamps(() => CharacterMigration.Waits(SaveVerifier.Load(skele).Save, season.Game)));
        var done = WithStamps(() => CharacterMigration.Run(season, new CharacterMigration.Request(skele, stash, DryRun: false)));
        Assert.Equal(done.Was.Mana + 89, done.Now.Mana);

        var (save, _) = WithStamps(() => SaveVerifier.Load(skele));
        Assert.False(WithStamps(() => CharacterMigration.Waits(save, season.Game)));
    }

    [CorpusFact]
    public void The_shipped_values_move_every_real_character_as_the_snapshot_does()
    {
        if (!Ready)
            return;

        // Every character of the S14 launch's specimens: a dry run by the snapshot and one by the shipped values.
        _ = Copies();
        var files = Directory.EnumerateFiles(Specimens, "*.d2s", SearchOption.AllDirectories).Order().ToList();
        Assert.NotEmpty(files);

        using var season = Season.Open(Game.Root, announce: false);
        CharacterMigration.Request[] asked = [.. files.Select(file => new CharacterMigration.Request(file, null, DryRun: true))];

        var bySnapshot = WithStamps(() => CharacterMigration.RunAll(season, asked));
        using var hidden = new NoSnapshot();
        var byShipped = WithStamps(() => CharacterMigration.RunAll(season, asked));

        int moved = 0;
        foreach (var (snapshot, shipped) in bySnapshot.Zip(byShipped))
        {
            Assert.Equal(snapshot.Report?.Now, shipped.Report?.Now);
            moved += snapshot.Report is { } report && report.Now != report.Was ? 1 : 0;
        }

        Assert.True(moved > 0, "no specimen's base moved: the comparison asked nothing");
    }

    [CorpusFact]
    public void The_shipped_values_are_what_the_seasons_table_gives_every_class()
    {
        if (!Ready)
            return;

        _ = Copies();
        var stamps = (SeasonReading.CurrentStamp, SeasonReading.PreviousStamp);
        using var season = Season.Open(Game.Root, announce: false);
        try
        {
            using var then = Season.Open(Game.Root, Snapshot13, announce: false);
            var shipped = SeasonSnapshot.KnownClassValues["S13"];
            Assert.Equal(CharacterClasses.Names.Count, shipped.Count);
            for (byte id = 0; id < shipped.Count; id++)
            {
                foreach (var (level, vitality, energy) in new[] { (1, 40, 40), (99, 300, 150), (80, 25, 35) })
                    Assert.Equal(NewCharacter.Vitals(then.Game, id, level, vitality, energy, 0), NewCharacter.Vitals(shipped[id], level, vitality, energy, 0));
            }
        }
        finally
        {
            season.Reinstall();
            SeasonReading.SetStamps(stamps.Item1, stamps.Item2);
        }
    }

    [CorpusFact]
    public void A_dry_run_writes_nothing_and_says_what_it_would_do()
    {
        if (!Ready)
            return;

        var (save, stash) = Copies();
        byte[] character = File.ReadAllBytes(save), shared = File.ReadAllBytes(stash);
        using var season = Season.Open(Game.Root, announce: false);
        var report = WithStamps(() => CharacterMigration.Run(season, new CharacterMigration.Request(save, stash, DryRun: true)));

        Assert.True(report.DryRun);
        Assert.Equal(1_166_036 + 125_338, report.GoldMoved);
        Assert.Empty(report.Backups);
        Assert.Equal(character, File.ReadAllBytes(save));
        Assert.Equal(shared, File.ReadAllBytes(stash));
    }
}
