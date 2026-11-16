using System.Text.Json;

using D2SLib.Model.Save;

using PD2SPCompanion.Core.Trade;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// What the cube does to a map, against the installed tables - Season 13's: the
/// corruption's own roll of three thousand, the recipes that write fixed properties (Standard of Heroes, Orb
/// of Fortification, Catalyst Shard, the Corrupted Ears), and the five a dungeon is made with. And the one
/// real item that showed the gap: a heroic Imperial Palace on the market, flagged without them.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class MapRecipeTests(Game game)
{
    private const string Stamp = "103";

    private static Item Map(string code, ItemQuality quality) => ItemForge.BaseAs(code, quality, Stamp, new Random(5)).Item;

    /// <summary>
    /// A map as <see cref="Map"/> makes it, its rolled affixes taken off - what a recipe or a bracket is tested on. The
    /// Forge rolls a magic or rare map as a drop; a dungeon keeps the scarab's five.
    /// </summary>
    private static Item Blank(string code, ItemQuality quality)
    {
        var map = Map(code, quality);
        Array.Clear(map.MagicPrefixIds!);
        Array.Clear(map.MagicSuffixIds!);
        ItemStats.Home(map).Stats.Clear();
        MapRecipes.Dress(map);
        return map;
    }

    private static Item UniqueMap(string code) =>
        ItemForge.UniqueAt(ItemForge.EnabledUniques().First(row => ItemForge.UniqueAt(row, Stamp).Item.Code.Trim() == code), Stamp).Item;

    private static int? Stat(Item item, string name) =>
        item.StatLists.SelectMany(list => list.Stats).FirstOrDefault(stat => stat.Stat == name)?.Value;

    private static string[] Ingredients(Item item) => [.. MapRecipes.Available(item).Select(recipe => recipe.Ingredient).OrderBy(each => each)];

    private static readonly string[] Ears = ["ivea", "iveb", "ived", "iven", "ivep", "ives", "ivez"];

    [Trait("Season", "S13")]
    [CorpusFact]
    public void A_maps_corruption_is_eleven_outcomes_of_a_roll_of_three_thousand()
    {
        if (!game.Pinned("S13"))
            return;

        if (!game.Available)
            return;

        foreach (string code in new[] { "t11", "t12", "t13" })
        {
            foreach (var quality in new[] { ItemQuality.Magic, ItemQuality.Rare })
            {
                var open = Corruptions.Available(Map(code, quality));
                Assert.Equal(Enumerable.Range(78, 11), open.Select(option => option.Id));
                Assert.All(open, option => Assert.Equal("9%", option.Chance));
                Assert.All(open, option => Assert.Equal(270, option.Width));
            }

            // A white map is turned away: the game has a recipe that says so.
            Assert.Empty(Corruptions.Available(Map(code, ItemQuality.Normal)));
            Assert.Null(Corruptions.ShardFor(Map(code, ItemQuality.Normal)));
        }

        // A dungeon: ten, in bands of a hundred of the same three thousand - as the table has it.
        var dungeon = Corruptions.Available(Map("t42", ItemQuality.Rare));
        Assert.Equal(Enumerable.Range(78, 10), dungeon.Select(option => option.Id));
        Assert.All(dungeon, option => Assert.Equal("3.3%", option.Chance));

        // A unique map takes none.
        Assert.Empty(Corruptions.Available(UniqueMap("t52")));

        // Gear is rolled out of a thousand, as it was.
        var ring = Corruptions.Available(ItemForge.BaseAs("rin", ItemQuality.Rare, Stamp, new Random(5)).Item);
        Assert.NotEmpty(ring);
        Assert.All(ring, option => Assert.Equal(1000, option.Height));
        Assert.True(ring.Sum(option => option.Width) <= 1000);
    }

    [Trait("Season", "S13")]
    [CorpusFact]
    public void A_corruption_writes_what_its_row_says_and_the_check_is_silent()
    {
        if (!game.Pinned("S13"))
            return;

        if (!game.Available)
            return;

        foreach (string code in new[] { "t11", "t13", "t42" })
        {
            var blank = Map(code, ItemQuality.Rare);
            foreach (var option in Corruptions.Available(blank))
            {
                var map = Map(code, ItemQuality.Rare);
                Corruptions.Apply(map, option, new Random(option.Id));
                Assert.Equal(option.Id, Corruptions.IdOn(map));
                Assert.True(Legitimacy.Check(map).Count == 0,
                    $"{code} corruption {option.Id}: {string.Join(" | ", Legitimacy.Check(map).Select(each => each.ToString()))}");
            }
        }
    }

    [CorpusFact]
    public void Each_map_is_offered_the_recipes_the_cube_has_for_it()
    {
        if (!game.Available)
            return;

        string[] all = [.. Ears.Append("fort").Append("iwss").Append("std").OrderBy(each => each)];

        // Magic and rare, tiers 1 to 3: all ten.
        foreach (string code in new[] { "t11", "t25", "t3b" })
        {
            Assert.Equal(all, Ingredients(Map(code, ItemQuality.Magic)));
            Assert.Equal(all, Ingredients(Map(code, ItemQuality.Rare)));

            // A white map: none of them.
            Assert.Empty(Ingredients(Map(code, ItemQuality.Normal)));
        }

        // A dungeon: heroic and the ears - nothing fortifies it or forces its event.
        Assert.Equal(Ears.Append("std").OrderBy(each => each), Ingredients(Map("t43", ItemQuality.Rare)));

        // A unique map: heroic and fortified, no ear.
        Assert.Equal(["fort", "std"], Ingredients(UniqueMap("t54")));

        // And nothing that is no map.
        Assert.Empty(MapRecipes.Available(ItemForge.BaseAs("rin", ItemQuality.Rare, Stamp).Item));
        Assert.Empty(MapRecipes.Grants(ItemForge.BaseAs("uar", ItemQuality.Normal, Stamp).Item));
    }

    [Trait("Season", "S13")]
    [CorpusFact]
    public void A_recipe_goes_on_once_is_credited_and_comes_off_whole()
    {
        if (!game.Pinned("S13"))
            return;

        if (!game.Available)
            return;

        var map = Blank("t12", ItemQuality.Rare);
        string Shape() => string.Join(";", map.StatLists.SelectMany(list => list.Stats).Select(stat => $"{stat.Stat}={stat.Value}"));
        string before = Shape();

        // Heroic: its flag, and +20 magic find, +20 gold find, +20 density, +10 experience.
        var heroic = MapRecipes.Available(map).Single(recipe => recipe.Ingredient == "std");
        Assert.Equal("Standard of Heroes", heroic.Name);
        Assert.True(MapRecipes.Apply(map, heroic));
        Assert.Equal(1, Stat(map, "heroic"));
        Assert.Equal(20, Stat(map, "map_play_magicbonus"));
        Assert.Equal(20, Stat(map, "map_play_goldbonus"));
        Assert.Equal(20, Stat(map, "map_glob_density"));
        Assert.Equal(10, Stat(map, "map_play_addexperience"));
        Assert.Contains(heroic, MapRecipes.On(map));
        Assert.Empty(Legitimacy.Check(map));

        // Not twice.
        Assert.False(MapRecipes.Apply(map, heroic));
        Assert.Equal(20, Stat(map, "map_glob_density"));

        // Fortified and an event forced on top: the density is still the Standard's alone.
        var fortified = MapRecipes.Available(map).Single(recipe => recipe.Ingredient == "fort");
        var forced = MapRecipes.Available(map).Single(recipe => recipe.Ingredient == "iwss");
        Assert.True(MapRecipes.Apply(map, fortified));
        Assert.True(MapRecipes.Apply(map, forced));
        Assert.Equal(50, Stat(map, "map_glob_skirmish_mode"));
        Assert.Equal(30, Stat(map, "map_play_velocitypercent"));
        Assert.Equal(63, Stat(map, "map_force_event"));
        Assert.Equal(3, MapRecipes.On(map).Count);
        Assert.Empty(Legitimacy.Check(map));

        // One ear, and no second: the Sorceress's, which says so by its number.
        var ear = MapRecipes.Available(map).Single(recipe => recipe.Ingredient == "ives");
        Assert.True(MapRecipes.Apply(map, ear));
        Assert.Equal(2, Stat(map, "treacherous"));
        Assert.Equal(-25, Stat(map, "map_play_fireresist"));
        Assert.Equal(25, Stat(map, "map_glob_monsterrarity"));
        Assert.False(MapRecipes.Apply(map, MapRecipes.Available(map).Single(recipe => recipe.Ingredient == "ivez")));
        Assert.Empty(Legitimacy.Check(map));

        // Written and read back as it stands.
        byte[] wrote = Item.Write(map, 0x60);
        Assert.True(wrote.AsSpan().SequenceEqual(Item.Write(Item.Read(wrote, 0x60), 0x60)));

        // And off again, each in turn, to the map it was.
        foreach (var recipe in MapRecipes.On(map).ToList())
            Assert.True(MapRecipes.TakeOff(map, recipe));
        Assert.Empty(MapRecipes.On(map));
        Assert.Equal(before, Shape());

        // A unique map is fortified without the players' speed.
        var palace = UniqueMap("t54");
        Assert.True(MapRecipes.Apply(palace, MapRecipes.Available(palace).Single(recipe => recipe.Ingredient == "fort")));
        Assert.Equal(50, Stat(palace, "map_glob_skirmish_mode"));
        Assert.Null(Stat(palace, "map_play_velocitypercent"));
        Assert.Empty(Legitimacy.Check(palace));
    }

    [CorpusFact]
    public void A_dungeon_is_made_with_the_scarabs_five()
    {
        if (!game.Available)
            return;

        foreach (string code in new[] { "t42", "t43", "t44" })
        {
            Assert.Equal(5, MapRecipes.MadeWith(code).Count);

            // Rolled as the game makes one: six affixes on top of the five, and nothing said against it.
            var rolled = Map(code, ItemQuality.Rare);
            Assert.Equal(6, rolled.MagicPrefixIds!.Count(id => id > 0) + rolled.MagicSuffixIds!.Count(id => id > 0));
            Assert.Empty(Legitimacy.Check(rolled));

            var dungeon = Blank(code, ItemQuality.Rare);
            Assert.Equal(30, Stat(dungeon, "map_mon_velocitypercent"));
            Assert.Equal(100, Stat(dungeon, "map_mon_tohit"));
            Assert.Equal(100, Stat(dungeon, "map_mon_pierce"));
            Assert.Equal(1, Stat(dungeon, "map_mon_cannotbefrozen"));
            Assert.Equal(20, Stat(dungeon, "map_mon_maxhp_percent"));
            Assert.Equal(50, Stat(dungeon, "map_play_addexperience"));
            Assert.NotNull(Stat(dungeon, "map_mon_splash"));
            Assert.Empty(Legitimacy.Check(dungeon));

            // "Monsters have Cannot Be Frozen" is one bit and reads bare in game (Storm Casque); the
            // life the same row writes keeps its bracket.
            var shown = AffixRanges.DisplayedFor(dungeon);
            Assert.False(shown.ContainsKey(AffixRanges.KeyOf("map_mon_cannotbefrozen", 0)));
            // As a staff's "Repairs 1 durability in 33 seconds" (Warp Spell), and not the enhanced damage pair.
            Assert.False(StatText.PrintsItsValue("item_replenish_durability"));
            Assert.True(StatText.PrintsItsValue("item_maxdamage_percent"));
            Assert.True(StatText.PrintsItsValue("map_glob_density"));
            Assert.True(shown[AffixRanges.KeyOf("map_mon_maxhp_percent", 0)].Max > shown[AffixRanges.KeyOf("map_mon_maxhp_percent", 0)].Min);

            byte[] wrote = Item.Write(dungeon, 0x60);
            Assert.True(wrote.AsSpan().SequenceEqual(Item.Write(Item.Read(wrote, 0x60), 0x60)));
        }

        // No other map is made with anything.
        Assert.Empty(MapRecipes.MadeWith("t11"));
        Assert.Empty(MapRecipes.MadeWith("t52"));
        Assert.Empty(Blank("t13", ItemQuality.Rare).StatLists.SelectMany(list => list.Stats));
    }

    [Trait("Season", "S13")]
    [CorpusFact]
    public void Monster_rarity_is_what_its_field_holds()
    {
        if (!game.Pinned("S13"))
            return;

        if (!game.Available)
            return;

        // Five bits: 31. Champion's asks 30-40 on a tier-1 map, and the Imperial Palace's row 50.
        var map = Map("t11", ItemQuality.Rare);
        var champions = MagicAffixes.Available(map, prefix: true).First(option => option.Name == "Champion's");
        Assert.Equal("Monster Rarity: +30-31%", champions.Readable);
        Assert.Equal(31, MagicAffixes.Roll(champions, new Random(1)).Values.Max());
        Assert.InRange(MagicAffixes.Roll(champions, null)["map_glob_monsterrarity"], 30, 31);

        var palace = UniqueMap("t54");
        Assert.Equal(31, Stat(palace, "map_glob_monsterrarity"));
        Assert.Empty(Legitimacy.Check(palace));

        // A corruption's 30-40 the same, and an ear's 25 on top of it stays 31.
        var rarity = Corruptions.Available(map).Single(option => option.Id == 86);
        Corruptions.Apply(map, rarity, new Random(9));
        Assert.InRange(Stat(map, "map_glob_monsterrarity") ?? 0, 30, 31);
        Assert.True(MapRecipes.Apply(map, MapRecipes.Available(map).Single(recipe => recipe.Ingredient == "ivez")));
        Assert.Equal(31, Stat(map, "map_glob_monsterrarity"));
        Assert.Empty(Legitimacy.Check(map));

        byte[] wrote = Item.Write(map, 0x60);
        Assert.Equal(31, Stat(Item.Read(wrote, 0x60), "map_glob_monsterrarity"));
    }

    [CorpusFact]
    public void A_bracket_wholly_below_zero_is_drawn_up_to_nought()
    {
        if (!game.Available)
            return;

        // A Demon Road map: "Players have -19% Chance to Block [-40 - 0]" in game.
        var map = Blank("t27", ItemQuality.Rare);
        var fumbling = MagicAffixes.Available(map, prefix: false).First(option => option.Name == "of Fumbling");
        MagicAffixes.Apply(map, new Dictionary<(int, bool), MagicAffixes.AffixPick> { [(0, false)] = new(fumbling.Id, MagicAffixes.Roll(fumbling, null)) }, new Random(1));

        var shown = AffixRanges.DisplayedFor(map);
        var block = shown[AffixRanges.KeyOf("map_play_toblock", 0)];
        Assert.Equal((-40, 0), (block.Min, block.Max));

        // And "+N% of Physical Damage as Extra Poison Damage over 5 Seconds" has no bracket at all in
        // game - nor, by the same shape, the cold one - while the fire one beside them has.
        foreach (var (affix, stat, bracketed) in new[]
                 {
                     ("Poisonous", "map_mon_phys_as_extra_pois", false),
                     ("Chilling", "map_mon_phys_as_extra_cold", false),
                     ("Smoking", "map_mon_phys_as_extra_fire", true),
                 })
        {
            var other = Blank("t27", ItemQuality.Rare);
            var option = MagicAffixes.Available(other, prefix: true).First(each => each.Name == affix);
            MagicAffixes.Apply(other, new Dictionary<(int, bool), MagicAffixes.AffixPick> { [(0, true)] = new(option.Id, MagicAffixes.Roll(option, null)) }, new Random(1));
            Assert.NotNull(Stat(other, stat));
            Assert.Equal(bracketed, AffixRanges.DisplayedFor(other).ContainsKey(AffixRanges.KeyOf(stat, 0)));
            Assert.Empty(Legitimacy.Check(other));
        }

        // A tier-3 map's other lines as the game drew them: the low rows are in its union.
        Assert.Equal(3, MapRecipes.TierOf("t27"));
        Assert.Null(MapRecipes.TierOf("t42"));
        Assert.Null(MapRecipes.TierOf("rin"));
    }

    [CorpusFact]
    public void A_map_is_written_as_the_game_writes_it()
    {
        if (!game.Available)
            return;

        // Every recipe on a rare map, put on in the order the buttons are pressed: the lists come out in
        // id order all the same, as the game saves them.
        var map = Map("t27", ItemQuality.Rare);
        MagicAffixes.Apply(map, MagicAffixes.RollFresh(map, new Random(3)), new Random(3));
        foreach (string ingredient in new[] { "iwss", "ivez", "fort", "std" })
            Assert.True(MapRecipes.Apply(map, MapRecipes.Available(map).First(recipe => recipe.Ingredient == ingredient)), ingredient);

        Assert.All(map.StatLists, list =>
            Assert.Equal(list.Stats.Select(stat => stat.Id).OrderBy(id => id), list.Stats.Select(stat => stat.Id)));
        Assert.Empty(Legitimacy.Check(map));

        // The control: a Dread Circlet given a Catalyst Shard in the game's own cube,
        // as the game saved it. Taken back off and put on here, it is the game's item bit for bit.
        string path = Path.Combine(Game.Corpus, "maps", "catalysed-in-game-cube-and-two-editor-made-s13.d2s");
        if (!File.Exists(path))
            return;

        var games = (SaveVerifier.Load(path).Save.PlayerItemList?.Items ?? []).Single(item => item.Id == 25593046);
        Assert.Equal(63, Stat(games, "map_force_event"));
        byte[] asSaved = Item.Write(games, 0x60);

        var mine = Item.Read(asSaved, 0x60);
        var catalyst = Assert.Single(MapRecipes.On(mine));
        Assert.Equal("iwss", catalyst.Ingredient);
        Assert.True(MapRecipes.TakeOff(mine, catalyst));
        Assert.Null(Stat(mine, "map_force_event"));
        Assert.True(MapRecipes.Apply(mine, MapRecipes.Available(mine).First(recipe => recipe.Ingredient == "iwss")));
        Assert.Equal(asSaved, Item.Write(mine, 0x60));
    }

    [CorpusFact]
    public void A_map_is_told_from_everything_else()
    {
        if (!game.Available)
            return;

        // What the item panel's Add property asks before it lists a map's 104 properties.
        foreach (string code in new[] { "t11", "t27", "t42", "t43", "t51", "t58" })
            Assert.True(ItemOrder.IsMap(code), code);

        foreach (string code in new[] { "7gd", "uap", "rin", "cm1", "jew", "wss", "std", "ivea", "r33" })
            Assert.False(ItemOrder.IsMap(code), code);

        // A map is offered a map's properties and nothing else; nothing else is offered a map's. The
        // recipes' marks are a map's though their names do not say so; the corruption's two are everyone's.
        foreach (string stat in new[] { "map_glob_density", "map_play_toblock", "map_mon_splash", "heroic", "treacherous" })
        {
            Assert.True(StatCatalogue.IsMapOnly(stat), stat);
            Assert.True(StatCatalogue.OfferedOn(stat, map: true), stat);
            Assert.False(StatCatalogue.OfferedOn(stat, map: false), stat);
        }

        foreach (string stat in new[] { "strength", "item_allskills", "item_fastercastrate", "fireresist", "maxhp" })
        {
            Assert.False(StatCatalogue.IsMapOnly(stat), stat);
            Assert.False(StatCatalogue.OfferedOn(stat, map: true), stat);
            Assert.True(StatCatalogue.OfferedOn(stat, map: false), stat);
        }

        foreach (string stat in new[] { Corruptions.IdStat, Corruptions.FlagStat })
        {
            Assert.True(StatCatalogue.OfferedOn(stat, map: true), stat);
            Assert.True(StatCatalogue.OfferedOn(stat, map: false), stat);
        }

        // Every property the game's own 150 maps hold is one a map is offered.
        string path = Path.Combine(Game.Corpus, "maps", "hundred-maps-fifty-dungeons-rolled-in-game-s13.d2s");
        if (!File.Exists(path))
            return;

        var held = (SaveVerifier.Load(path).Save.PlayerItemList?.Items ?? [])
            .Where(item => ItemOrder.IsMap(item.Code))
            .SelectMany(item => item.StatLists.SelectMany(list => list.Stats))
            .Select(stat => stat.Stat ?? "").Distinct().ToList();
        Assert.True(held.Count > 40);
        Assert.All(held, stat => Assert.True(StatCatalogue.OfferedOn(stat, map: true), stat));
    }

    [CorpusFact]
    public void The_forge_lists_maps_by_tier_and_says_it()
    {
        if (!game.Available)
            return;

        var search = new PD2SPCompanion.Core.Collection.ForgeSearch();
        search.Load();
        search.SetType(search.Types.First(type => type.Label == "Maps"));
        var maps = search.Bases.Skip(1).ToList();

        // Tier one to three, then the dungeons; by name within a tier.
        var ranks = maps.Select(line => MapRecipes.TierRank(line.Code) ?? 9).ToList();
        Assert.Equal(ranks.OrderBy(each => each), ranks);
        Assert.Equal([1, 2, 3, 4], ranks.Distinct());
        foreach (var tier in maps.GroupBy(line => MapRecipes.TierRank(line.Code)))
        {
            var names = tier.Select(line => line.Label).ToList();
            Assert.Equal(names.OrderBy(each => each, StringComparer.OrdinalIgnoreCase), names);
        }

        Assert.All(maps, line => Assert.True(line.HasTier, line.Label));
        Assert.Equal("Tier 1", maps.Single(line => line.Code == "t11").TierShown);
        Assert.Equal("Tier 3", maps.Single(line => line.Code == "t27").TierShown);
        Assert.Equal("Dungeon", maps.Single(line => line.Code == "t42").TierShown);
        Assert.Equal("Tier 2", maps.Single(line => line.Code == "t52").TierShown);

        // And a found base says its tier beside its name, as gear does.
        Assert.Contains(search.Find("demon road", 20, out _), entry => entry.Code == "t27" && entry.Detail == "Tier 3");

        // Gear is as it was.
        search.SetType(search.Types.First(type => type.Label == "Chests"));
        Assert.Equal("Elite", search.Bases.Single(line => line.Code == "uar").TierShown);
        Assert.Null(MapRecipes.TierName("uar"));
    }

    [CorpusFact]
    public void A_rare_map_rolled_afresh_has_six_affixes()
    {
        if (!game.Available)
            return;

        // Fifty of fifty in game: three prefixes and three suffixes, one of a group.
        foreach (string code in new[] { "t11", "t25", "t15", "t42" })
        {
            for (int seed = 0; seed < 25; seed++)
            {
                var map = Map(code, ItemQuality.Rare);
                Assert.Equal(6, MagicAffixes.RollCount(map, new Random(seed)));

                var rolled = MagicAffixes.RollFresh(map, new Random(seed));
                Assert.Equal(3, rolled.Count(each => each.Key.Prefix && each.Value.Id > 0));
                Assert.Equal(3, rolled.Count(each => !each.Key.Prefix && each.Value.Id > 0));
            }
        }

        // A magic map: a prefix and a suffix, every time.
        for (int seed = 0; seed < 25; seed++)
        {
            var map = Map("t31", ItemQuality.Magic);
            var rolled = MagicAffixes.RollFresh(map, new Random(seed));
            Assert.Equal(1, rolled.Count(each => each.Key.Prefix && each.Value.Id > 0));
            Assert.Equal(1, rolled.Count(each => !each.Key.Prefix && each.Value.Id > 0));
        }

        // A magic map's Splashing is written as the game holds it.
        var splashing = Map("t31", ItemQuality.Magic);
        var splash = MagicAffixes.ReachableBy(splashing).First(option => option.Name == "Splashing" && !option.ForRare && option.On.Contains("t3m"));
        Assert.Equal(127, MagicAffixes.Roll(splash, null)["map_mon_splash"]);

        // Gear is rolled three to six below item level 45 (AffixCountTests has the rest).
        var ring = ItemForge.BaseAs("rin", ItemQuality.Rare, Stamp).Item;
        ring.ItemLevel = 30;
        var counts = Enumerable.Range(0, 200).Select(seed => MagicAffixes.RollCount(ring, new Random(seed))).ToHashSet();
        Assert.Equal([3, 4, 5, 6], counts.OrderBy(each => each));
    }

    [Trait("Season", "S13")]
    [CorpusFact]
    public void The_fifty_maps_the_game_rolled_are_read_as_it_rolled_them()
    {
        if (!game.Pinned("S13"))
            return;

        if (!game.Available)
            return;

        // Fifty tier-3 maps made rare in the game's own cube, and fifty made magic,
        // as the save had them.
        string path = Path.Combine(Game.Corpus, "maps", "hundred-maps-fifty-dungeons-rolled-in-game-s13.d2s");
        if (!File.Exists(path))
            return;

        var all = (SaveVerifier.Load(path).Save.PlayerItemList?.Items ?? [])
            .Where(item => ItemTypes.TypeOf(item.Code) == "t3m").ToList();
        var maps = all.Where(item => item.Quality == ItemQuality.Rare).ToList();
        Assert.Equal(50, maps.Count);

        // The magic ones: a prefix and a suffix, every one - and now and then a rare map's weaker row,
        // which a magic map may roll and a rare map's rows never return the favour.
        var magic = all.Where(item => item.Quality == ItemQuality.Magic).ToList();
        Assert.Equal(50, magic.Count);
        int weak = 0;
        foreach (var map in magic)
        {
            var pair = Assert.Single(MagicAffixes.PairsOn(map), each => !each.IsEmpty);
            Assert.NotNull(pair.Prefix);
            Assert.NotNull(pair.Suffix);

            var reach = MagicAffixes.ReachableBy(map).Select(option => (option.Id, option.IsPrefix)).ToHashSet();
            Assert.Contains((pair.Prefix!.Id, true), reach);
            Assert.Contains((pair.Suffix!.Id, false), reach);
            weak += (pair.Prefix.ForRare ? 1 : 0) + (pair.Suffix.ForRare ? 1 : 0);

            // "Splashing" asks a 150% chance and the field holds 127: three of the fifty.
            Assert.True((Stat(map, "map_mon_splash") ?? 0) <= 127);
            Assert.True((Stat(map, "map_glob_monsterrarity") ?? 0) <= 31);
            Assert.True(Legitimacy.Check(map).Count == 0,
                $"{ItemPresenter.Describe(map).Name}: {string.Join(" | ", Legitimacy.Check(map).Select(each => each.ToString()))}");
        }

        Assert.InRange(weak, 10, 40);

        int low = 0, magicOnly = 0;
        foreach (var map in maps)
        {
            var pairs = MagicAffixes.PairsOn(map);
            var prefixes = pairs.Select(pair => pair.Prefix).OfType<MagicAffixes.AffixOption>().ToList();
            var suffixes = pairs.Select(pair => pair.Suffix).OfType<MagicAffixes.AffixOption>().ToList();

            // Six, always: three and three, no group twice on a side.
            Assert.Equal(3, prefixes.Count);
            Assert.Equal(3, suffixes.Count);
            Assert.Equal(3, prefixes.Select(option => option.Group).Distinct().Count());
            Assert.Equal(3, suffixes.Select(option => option.Group).Distinct().Count());

            // Every one of them an affix the editor would offer this map, and none a magic-only row.
            var offered = MagicAffixes.ReachableBy(map).Select(option => (option.Id, option.IsPrefix)).ToHashSet();
            Assert.All(prefixes.Concat(suffixes), option => Assert.Contains((option.Id, option.IsPrefix), offered));
            magicOnly += prefixes.Concat(suffixes).Count(option => !option.ForRare);

            // A tier-3 map does roll the rows written for tiers 1 and 2: about a third of them.
            low += prefixes.Concat(suffixes).Count(option => option.On.SequenceEqual(["t1m", "t2m"]));

            // Monster Rarity is what its field holds.
            Assert.True((Stat(map, "map_glob_monsterrarity") ?? 0) <= 31);
            Assert.Empty(Legitimacy.Check(map));
        }

        Assert.Equal(0, magicOnly);
        Assert.InRange(low, 70, 120);
    }

    [CorpusFact]
    public void Ten_of_those_maps_are_bracketed_as_the_game_bracketed_them()
    {
        if (!game.Available)
            return;

        string path = Path.Combine(Game.Corpus, "maps", "hundred-maps-fifty-dungeons-rolled-in-game-s13.d2s");
        if (!File.Exists(path))
            return;

        // Ten of the fifty rare ones, hovered in game one by one (screenshots): ninety
        // lines, every green bracket the game drew and the five lines it drew none on. Picked for their
        // lines below zero - Drain Life, defense, resistances, Hit Recovery, Chance to Block - and each of
        // the five ends in 0, as the one seen before them did.
        uint[] hovered =
        [
            591661465, 25593046, 233797332, 2870785406, 973856675,
            1493587828, 1636038637, 263251973, 1804020725, 3723678178,
        ];

        var drawn = new Dictionary<string, (int Min, int Max)?>
        {
            ["map_play_magicbonus"] = (5, 60),
            ["map_play_goldbonus"] = (5, 60),
            ["map_glob_density"] = (8, 30),
            ["map_play_addexperience"] = (3, 12),
            ["map_glob_monsterrarity"] = (30, 60),
            ["map_mon_passive_fire_mastery"] = (5, 20),
            ["map_mon_passive_ltng_mastery"] = (5, 20),
            ["map_mon_passive_cold_mastery"] = (5, 20),
            ["map_mon_fasterattackrate"] = (40, 100),
            ["map_mon_fastercastrate"] = (40, 100),
            ["map_mon_tohit"] = (30, 90),
            ["map_mon_pierce"] = (30, 90),
            ["map_mon_ac%"] = (50, 200),
            ["map_mon_absorbcold_percent"] = (4, 10),
            ["map_mon_absorblight_percent"] = (4, 10),
            ["map_mon_absorbfire_percent"] = (4, 10),
            ["map_mon_normal_damage_reduction"] = (100, 600),
            ["map_mon_hpregen"] = (1024, 2560),
            ["map_mon_lifedrainmindam"] = (4, 16),
            ["map_mon_fastergethitrate"] = (40, 100),
            ["map_mon_maxhp_percent"] = (4, 20),
            ["map_mon_openwounds"] = (5, 20),
            ["map_mon_crushingblow"] = (15, 45),
            ["map_mon_ed%"] = (10, 25),
            ["map_mon_phys_as_extra_ltng"] = (60, 100),
            ["map_mon_phys_as_extra_fire"] = (60, 100),
            ["map_mon_phys_as_extra_mag"] = (8, 14),
            ["map_mon_dropweapons"] = (3, 6),
            ["map_mon_droparmor"] = (2, 7),
            ["map_mon_dropcrafting"] = (2, 5),

            // Below zero, up to nought.
            ["map_play_ac%"] = (-50, 0),
            ["map_play_fastergethitrate"] = (-40, 0),
            ["map_play_toblock"] = (-40, 0),
            ["map_play_hpregen"] = (-80, 0),
            ["map_play_fireresist"] = (-25, 0),
            ["map_play_lightresist"] = (-25, 0),
            ["map_play_coldresist"] = (-25, 0),
            ["map_play_poisonresist"] = (-25, 0),

            // Bare in game.
            ["map_mon_phys_as_extra_pois"] = null,
            ["map_mon_splash"] = null,
            ["map_glob_add_mon_horde"] = null,
            ["map_glob_add_mon_souls"] = null,
        };

        var maps = (SaveVerifier.Load(path).Save.PlayerItemList?.Items ?? [])
            .Where(item => hovered.Contains(item.Id)).ToList();
        Assert.Equal(10, maps.Count);

        var seen = new HashSet<string>();
        foreach (var map in maps)
        {
            var shown = AffixRanges.DisplayedFor(map);
            foreach (var stat in map.StatLists.SelectMany(list => list.Stats))
            {
                string name = stat.Stat ?? "";
                Assert.True(drawn.ContainsKey(name), $"{name} was on none of the ten");
                seen.Add(name);

                shown.TryGetValue(AffixRanges.KeyOf(stat), out var bracket);
                // A value that cannot vary is drawn without one, here as in game.
                var mine = bracket is null || bracket.Min == bracket.Max ? ((int, int)?)null : (bracket.Min, bracket.Max);
                Assert.True(drawn[name] == mine, $"{ItemPresenter.Describe(map).Name}, {name}: the game drew {drawn[name]}, the editor {mine}");
            }
        }

        Assert.Equal(drawn.Count, seen.Count);
    }

    [Trait("Season", "S13")]
    [CorpusFact]
    public void The_fifty_dungeons_the_scarab_made_and_what_fifty_shards_did_to_them()
    {
        if (!game.Pinned("S13"))
            return;

        if (!game.Available)
            return;

        // Fifty tier-3 maps turned into dungeons with Horadrim Scarabs in game, then a Worldstone Shard
        // tried on each: real dungeons, as the game made them.
        string path = Path.Combine(Game.Corpus, "maps", "hundred-maps-fifty-dungeons-rolled-in-game-s13.d2s");
        if (!File.Exists(path))
            return;

        var dungeons = (SaveVerifier.Load(path).Save.PlayerItemList?.Items ?? [])
            .Where(item => ItemTypes.TypeOf(item.Code) == "t4m").ToList();
        Assert.Equal(50, dungeons.Count);

        int corrupted = 0;
        foreach (var dungeon in dungeons)
        {
            // Rare and nothing else; six affixes, every one a row written for tiers 3 and 4.
            Assert.Equal(ItemQuality.Rare, dungeon.Quality);
            var affixes = MagicAffixes.PairsOn(dungeon).SelectMany(pair => new[] { pair.Prefix, pair.Suffix }).OfType<MagicAffixes.AffixOption>().ToList();
            Assert.Equal(6, affixes.Count);
            Assert.All(affixes, option => Assert.Equal(["t3m", "t4m"], option.On));

            // The scarab's five, on every one of them.
            Assert.All(new[] { "map_mon_velocitypercent", "map_mon_tohit", "map_mon_pierce", "map_mon_splash", "map_mon_maxhp_percent", "map_play_addexperience" },
                stat => Assert.NotNull(Stat(dungeon, stat)));
            Assert.Equal(1, Stat(dungeon, "map_mon_cannotbefrozen"));

            // The shard: its roll is set on all fifty, and one in three took an outcome - ten of them,
            // ids 78 to 87. The rest hold the spent roll and no corruption, and the game will not try again.
            Assert.Equal(1, Stat(dungeon, Corruptions.FlagStat));
            if (Corruptions.IdOn(dungeon) is { } id)
            {
                corrupted++;
                Assert.InRange(id, 78, 87);
            }

            Assert.Empty(Legitimacy.Check(dungeon));
        }

        // 19 of 50: the table's one in three (ten bands of 100 out of a roll of 3000), not one in one.
        Assert.Equal(19, corrupted);

        // All fifty count as corrupted - the game says so of each - and 31 of them with nothing: spent.
        // A spent one may still be given a corruption here, or have the mark taken off.
        Assert.Equal(50, dungeons.Count(Corruptions.IsCorrupted));
        Assert.Equal(31, dungeons.Count(Corruptions.IsSpent));

        var spent = dungeons.First(Corruptions.IsSpent);
        Corruptions.Apply(spent, Corruptions.Available(spent).First(), new Random(2));
        Assert.True(Corruptions.IsCorrupted(spent));
        Assert.False(Corruptions.IsSpent(spent));
        Assert.Empty(Legitimacy.Check(spent));

        var cleared = dungeons.Last(Corruptions.IsSpent);
        Corruptions.Clean(cleared, new Random(2));
        Assert.False(Corruptions.IsCorrupted(cleared));
        Assert.False(Corruptions.IsSpent(cleared));
        Assert.Null(Stat(cleared, Corruptions.FlagStat));
        Assert.Empty(Legitimacy.Check(cleared));
        Assert.All(Corruptions.Available(Map("t42", ItemQuality.Rare)), option => Assert.Equal("3.3%", option.Chance));
    }

    [CorpusFact]
    public void Only_a_plain_map_stacks()
    {
        if (!game.Available)
            return;

        // A magic map with a quantity of 2: used up whole at one click.
        var plain = Map("t11", ItemQuality.Normal);
        Assert.False(Stacks.IsOneOnly(plain));
        plain.Quantity = 20;
        Assert.Empty(Legitimacy.Check(plain));

        foreach (var quality in new[] { ItemQuality.Magic, ItemQuality.Rare })
        {
            // This season's stamp: an older item is never checked (ItemSeason); on 103 this passed in a full run and failed alone.
            var map = ItemForge.BaseAs("t11", quality, PD2SPCompanion.Core.Saves.ItemSeason.ForNew(Stamp), new Random(5)).Item;
            Assert.True(Stacks.IsOneOnly(map));
            Assert.Equal(1, (int)map.Quantity);
            Assert.Empty(Legitimacy.Check(map));

            map.Quantity = 2;
            var finding = Assert.Single(Legitimacy.Check(map));
            Assert.Equal(Certainty.Unrolled, finding.Certainty);
        }

        // A dungeon, which is rare; a unique map does not stack at all and has no count to raise.
        Assert.True(Stacks.IsOneOnly(Map("t42", ItemQuality.Rare)));
        Assert.False(Stacks.IsStackable("t52"));

        // What stacks and is no map is as it was: a magic quiver, a rare javelin.
        Assert.False(Stacks.IsOneOnly(ItemForge.BaseAs("aqv", ItemQuality.Magic, Stamp).Item));
        Assert.False(Stacks.IsOneOnly(ItemForge.BaseAs("jav", ItemQuality.Rare, Stamp).Item));
    }

    [Trait("Season", "S13")]
    [CorpusFact]
    public void Every_real_map_on_the_market_is_silent()
    {
        if (!game.Pinned("S13"))
            return;

        if (!game.Available)
            return;

        string folder = Path.Combine(Game.Corpus, "trade", "maps");
        if (!Directory.Exists(folder))
            return;

        int maps = 0, heroic = 0;
        var wrong = new List<string>();

        foreach (string file in Directory.EnumerateFiles(folder, "type_*.json"))
        {
            using var page = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var listing in page.RootElement.GetProperty("data").EnumerateArray())
            {
                var item = TradeItems.Build(listing.GetProperty("item")).Item;
                maps++;
                if (Stat(item, "heroic") == 1)
                    heroic++;

                var findings = Legitimacy.Check(item);
                if (findings.Count > 0)
                    wrong.Add($"{ItemPresenter.Describe(item).Name}: {string.Join(" | ", findings.Select(each => each.ToString()))}");
            }
        }

        Assert.True(maps >= 50, $"only {maps} maps read");
        Assert.True(heroic >= 1, "the heroic Imperial Palace is not among them");
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }
}
