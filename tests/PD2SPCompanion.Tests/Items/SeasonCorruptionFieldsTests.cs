using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// What Season 14 stores beside a corruption and a desecration, seen on a character:
/// three Atma's Scarabs of one roll, one plain, one corrupted in game, one corrupted and
/// desecrated in game. <c>corruptor</c> holds 1001 plus the cube's roll (1470 = 469, inside the amulet's fire
/// resistance band); <c>desecrated</c> holds the mod's corruption id (44, +10% Faster Run/Walk) and <c>desecrator</c>
/// 1001. The editor writes the same into an S14 item - and Season 13's 1, 2 and 3 into a legacy one, which is read by
/// Season 13's one- and two-bit fields. S14's own tables only.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class SeasonCorruptionFieldsTests(Game game)
{
    private static string Specimen => Path.Combine(Game.Corpus, "specimens", "s14-corrupt-desecrate-2026-10-26", "PhysSpear.d2s");

    private bool Ready => game.Available && Game.Tables is null && D2SLib.Model.TXT.StatEncoding.PreviousSeason == "S13" && File.Exists(Specimen);

    private static T WithStamps<T>(Func<T> run)
    {
        var kept = (SeasonReading.CurrentStamp, SeasonReading.PreviousStamp);
        SeasonReading.SetStamps("105", "103");
        try { return run(); }
        finally { SeasonReading.SetStamps(kept.CurrentStamp, kept.PreviousStamp); }
    }

    [CorpusFact]
    public void A_maps_corruptor_is_its_roll_past_3001_held_at_the_fields_top()
    {
        if (!Ready)
            return;

        // SeqBarb's three, corrupted in game: the magic one 79, 3374 - a roll of 373; the rare ones 85 and 87,
        // their rolls past 1094, 4095.
        var maps = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-31", "SeqBarbMaps.d2s")).Save.PlayerItemList.Items
            .Where(item => item.Code.Trim() == "t11").ToList();
        Assert.Equal([(79, 3374), (85, 4095), (87, 4095)], maps.Select(map => (Held(map, Corruptions.IdStat), Held(map, Corruptions.FlagStat))).Order());

        WithStamps(() =>
        {
            var magic = maps.Single(map => map.Quality == ItemQuality.Magic);
            var rare = maps.First(map => map.Quality == ItemQuality.Rare);
            for (int seed = 0; seed < 50; seed++)
            {
                Assert.InRange(Corruptions.FlagFor(magic, 79, new Random(seed)), 3001 + 271, 3001 + 540);
                Assert.Equal(4095, Corruptions.FlagFor(rare, 85, new Random(seed)));
            }
            return 0;
        });
    }

    private static int Held(Item item, string stat) => ItemStats.Every(item).Single(each => each.Stat == stat).Value;

    /// <summary>The three scarabs, in the order the game left them: plain, corrupted, corrupted and desecrated.</summary>
    private static List<Item> Scarabs()
    {
        string copy = Path.Combine(Path.GetTempPath(), "pd2spc-scarabs-" + Guid.NewGuid().ToString("N") + ".d2s");
        File.Copy(Specimen, copy);
        try
        {
            return [.. SaveVerifier.Load(copy).Save.PlayerItemList.Items.Where(item => ItemPresenter.Describe(item).Name == "Atma's Scarab")];
        }
        finally
        {
            File.Delete(copy);
        }
    }

    [CorpusFact]
    public void The_games_own_corruption_and_desecration_read_as_the_mod_they_are()
    {
        if (!Ready)
            return;

        WithStamps(() =>
        {
            var scarabs = Scarabs();
            Assert.Equal(3, scarabs.Count);
            var (corrupted, desecrated) = (scarabs[1], scarabs[2]);

            // The cube's roll, plus the 1001 its outcome adds, inside the band of the corruption it gave.
            Assert.Equal(36, Corruptions.IdOn(corrupted));
            Assert.Equal(1470, Held(corrupted, Corruptions.FlagStat));
            var fire = Corruptions.Available(corrupted, asIfClean: true).Single(option => option.Id == 36);
            Assert.InRange(1470 - 1001, fire.Band - fire.Width + 1, fire.Band);

            // The desecration names its mod: 44, the amulet's +10% Faster Run/Walk.
            Assert.True(Desecrations.NamesTheMod(desecrated));
            Assert.Equal(44, Desecrations.ValueOn(desecrated));
            Assert.Equal(1001, Held(desecrated, Desecrations.FlagStat));
            var mod = Desecrations.Identify(desecrated).Found;
            Assert.NotNull(mod);
            Assert.Equal(44, mod!.Id);
            Assert.Contains("item_fastermovevelocity", Desecrations.StatsOn(desecrated));

            Assert.Empty(Legitimacy.Check(corrupted));
            Assert.Empty(Legitimacy.Check(desecrated));
            return 0;
        });
    }

    [CorpusFact]
    public void The_editor_writes_what_the_game_does_and_Season_13s_values_on_a_legacy_item()
    {
        if (!Ready)
            return;

        WithStamps(() =>
        {
            var plain = Scarabs()[0];
            var fire = Corruptions.Available(plain).Single(option => option.Id == 36);

            // An S14 amulet: 1001 plus a roll in the band; then desecrated with Faster Run/Walk, 44 and 1001.
            var mine = ItemBlob.Copy(plain, 10);
            Corruptions.Apply(mine, fire, new Random(7));
            Assert.InRange(Held(mine, Corruptions.FlagStat), 1001 + fire.Band - fire.Width + 1, 1001 + fire.Band);
            var run = Desecrations.Pool(mine).Single(option => option.Id == 44);
            Assert.True(Desecrations.Apply(mine, Corruptions.Draft(mine, run, new Random(7))));
            Assert.Equal(44, Desecrations.ValueOn(mine));
            Assert.Equal(1001, Held(mine, Desecrations.FlagStat));
            Assert.Equal(44, Desecrations.Identify(mine).Found?.Id);

            // A legacy one keeps Season 13's: 1, then 3 (not enhanced damage) and 1.
            var legacy = ItemBlob.Copy(plain, 10);
            legacy.Version = "103";
            Corruptions.Apply(legacy, Corruptions.Available(legacy).Single(option => option.Id == 36), new Random(7));
            Assert.Equal(1, Held(legacy, Corruptions.FlagStat));
            Assert.True(Desecrations.Apply(legacy, Corruptions.Draft(legacy, Desecrations.Pool(legacy).Single(option => option.Id == 44), new Random(7))));
            Assert.Equal(Desecrations.ForOthers, Desecrations.ValueOn(legacy));
            Assert.Equal(1, Held(legacy, Desecrations.FlagStat));
            return 0;
        });
    }
}
