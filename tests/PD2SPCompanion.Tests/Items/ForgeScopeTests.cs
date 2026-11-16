using D2SLib.Model.Save;

using PD2SPCompanion.Core.Collection;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// What the Forge makes and in which rarities (ForgeScope, ItemForge.QualitiesOf)
/// against the installed tables - Season 13's, which is where every list here was read.
/// The rarity rules are very important, and are enforced on the new items available.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class ForgeScopeTests(Game game)
{
    /// <summary>
    /// Cyclopean Roar listed twice on a Jawbone Visor, and others as well.
    /// The game has two of it, and of three other names - each row drops, and differs by a line. No
    /// two of the Forge's uniques, set pieces or runewords read alike, and each of a name's says what it alone
    /// has.
    /// </summary>
    [CorpusFact]
    public void No_two_entries_read_alike_and_the_several_of_one_name_say_what_each_alone_has()
    {
        if (!game.Available)
            return;

        var search = new ForgeSearch();
        search.Load();

        foreach (int rarity in new[] { ForgeSearch.Unique, ForgeSearch.Set, ForgeSearch.Runeword })
        {
            search.SetRarity(search.Rarities.First(each => each.From == rarity));
            var alike = search.Names.GroupBy(entry => (entry.Name, entry.Variant, entry.Detail)).Where(group => group.Count() > 1)
                .Select(group => $"{group.Count()} x {group.Key.Name} | {group.Key.Variant} | {group.Key.Detail}").ToList();
            Assert.True(alike.Count == 0, string.Join("; ", alike));
        }

        search.SetRarity(search.Rarities.First(each => each.From == ForgeSearch.Unique));
        List<string> Of(string name) => [.. search.Names.Where(entry => entry.Name == name).Select(entry => entry.Variant).Order()];

        // The skill and when; what the rows do not share; a facet as players call it. The base still said.
        Assert.Equal(["Battle Cry on casting", "Battle Cry on striking"], Of("Cyclopean Roar"));
        Assert.Equal(["Blessed Hammer on casting", "Blessed Hammer on striking"], Of("Akarat's Devotion"));
        Assert.Equal(["Skeleton Archers", "Skeleton Warriors"], Of("Arm of King Leoric"));
        Assert.Equal(
            [
                "Cold die facet", "Cold level-up facet", "Fire die facet", "Fire level-up facet",
                "Lightning die facet", "Lightning level-up facet", "Poison die facet", "Poison level-up facet",
            ],
            Of("Rainbow Facet"));
        Assert.All(search.Names.Where(entry => entry.Name == "Cyclopean Roar"), entry => Assert.Equal("Jawbone Visor", entry.Detail));

        // A name with one row says no variant.
        Assert.All(search.Names.Where(entry => entry.Name == "Goldwrap"), entry => Assert.Equal("", entry.Variant));
    }

    private const string Stamp = "105";

    private static readonly ItemQuality[] Plain = [ItemQuality.Normal];

    [CorpusFact]
    public void Maps_and_loose_materials_are_made_and_the_materials_tabs_are_not()
    {
        if (!game.Available)
            return;

        // PD2's own materials that lie loose: the 24 the tab does not keep.
        string[] loose =
        [
            "ubtm", "dcma", "rtma", "uba", "luca", "rtmf", "t41", "t61", "t62", "irrn", "lmal", "toa",
            "ivea", "ivez", "iveb", "ived", "iven", "ivep", "ives",
            "llmr", "lsvl", "rid", "rtp", "rkey",
        ];

        var found = ItemBases.Codes.Where(ForgeScope.IsLooseMaterial).OrderBy(each => each).ToList();
        Assert.Equal(loose.OrderBy(each => each), found);
        Assert.All(loose, code => Assert.True(ItemForge.Makes(code), code));

        // Maps: the plain ones and the unique ones.
        Assert.All(new[] { "t11", "t25", "t3b", "t42", "t44", "t51", "t58" }, code => Assert.True(ItemForge.Makes(code), code));

        // The tab's own, the old game's consumables and a quest item: never.
        Assert.All(
            new[] { "r01", "r01s", "gcv", "gpv", "pk1", "wss", "lbox", "imrn", "crfb", "std", "hp1", "rvl", "tsc", "tbk", "key", "ear", "gld", "hdm", "box" },
            code => Assert.False(ItemForge.Makes(code), code));
    }

    [CorpusFact]
    public void A_row_the_game_has_no_use_for_is_not_made()
    {
        if (!game.Available)
            return;

        // The old game's six throwing potions under PD2's twelve; a base that neither spawns nor is named
        // or cubed; the Elixir and the Herb.
        string[] leftover = ["opl", "opm", "ops", "gpl", "gpm", "gps", "7cr2", "cm1p", "cm2p", "cm3p", "t99", "t69", "elx", "hrb"];

        var found = ItemBases.Codes
            .Where(code => !ItemBases.IsQuestItem(code) && !ForgeScope.IsLooseMaterial(code)
                           && (ItemTypes.IsGear(ItemTypes.TypeOf(code)) || ItemTypes.CanRollAffixes(ItemTypes.TypeOf(code)))
                           && ForgeScope.IsLeftover(code))
            .OrderBy(each => each).ToList();
        Assert.Equal(leftover.OrderBy(each => each), found);

        // What stands next to them and is real: PD2's potions, the Phase Blade, the charms, Wirt's Leg.
        Assert.All(new[] { "tpfs", "tpfm", "tpgl", "tpcs", "tpll", "7cr", "cm1", "cm2", "cm3", "leg", "uhn", "utc", "amu" },
            code => Assert.True(ItemForge.Makes(code), code));

        // No two bases the Forge lists in one type wear one name.
        var search = new ForgeSearch();
        search.Load();
        foreach (var type in search.Types.Skip(1))
        {
            search.SetType(type);
            var twice = search.Bases.Skip(1).GroupBy(line => line.Label).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
            Assert.True(twice.Count == 0, $"{type.Label}: {string.Join(", ", twice)}");
        }
    }

    [CorpusFact]
    public void A_base_is_made_only_in_the_rarities_the_game_has_it_in()
    {
        if (!game.Available)
            return;

        static ItemQuality[] Of(string code) => [.. ItemForge.QualitiesOf(code)];

        // The rules as read back.
        Assert.DoesNotContain(ItemQuality.Normal, Of("rin"));
        Assert.DoesNotContain(ItemQuality.Normal, Of("amu"));
        Assert.All(new[] { "cm1", "cm2", "cm3" }, code => Assert.Equal([ItemQuality.Magic], Of(code)));
        Assert.Equal([ItemQuality.Magic, ItemQuality.Rare], Of("jew"));
        Assert.All(new[] { "ci0", "ci1", "ci2", "ci3" }, code => Assert.DoesNotContain(ItemQuality.Craft, Of(code)));

        foreach (string quiver in ItemBases.Codes.Where(code => ItemOrder.TypeName(ItemOrder.TypeRankOf(code)) == "Quivers" && ItemForge.Makes(code)))
            Assert.DoesNotContain(ItemQuality.Superior, Of(quiver));

        // A throwing potion is only ever normal: its type says so, whatever the superior table's "thrown"
        // column reaches through its parents.
        foreach (string potion in ItemBases.Codes.Where(code => ItemTypes.TypeOf(code) == "tpot" && ItemForge.Makes(code)))
            Assert.Equal(Plain, Of(potion));

        // A plain map: normal, magic or rare - never superior, never crafted.
        foreach (string map in ItemBases.Codes.Where(code => ItemTypes.TypeOf(code) is "t1m" or "t2m" or "t3m").Where(ItemForge.Makes))
            Assert.Equal([ItemQuality.Normal, ItemQuality.Magic, ItemQuality.Rare], Of(map));

        // A dungeon map is rare alone: a Horadrim Scarab makes it so, and nothing drops or scours one.
        foreach (string dungeon in new[] { "t42", "t43", "t44" })
            Assert.Equal([ItemQuality.Rare], Of(dungeon));

        // A loose material is normal and nothing else.
        foreach (string code in ItemBases.Codes.Where(ForgeScope.IsLooseMaterial))
            Assert.Equal(Plain, Of(code));

        // A base that exists only as its unique has no quality of its own: the eight unique maps, and
        // Rathma's three.
        string[] named = ["t51", "t52", "t53", "t54", "t55", "t56", "t57", "t58", "rar", "rbe", "ram"];
        Assert.Equal(named.OrderBy(each => each), ItemBases.Codes.Where(ForgeScope.OnlyNamed).OrderBy(each => each));
        Assert.All(named, code => Assert.True(ItemForge.Makes(code), code));
        Assert.All(named, code => Assert.Empty(Of(code)));
    }

    [CorpusFact]
    public void The_boxes_offer_a_base_that_exists_only_as_its_unique_as_that_and_nothing_else()
    {
        if (!game.Available)
            return;

        var search = new ForgeSearch();
        search.Load();

        // Rathma's armour: marked in the list, Unique alone, and the one unique chosen by itself.
        search.SetType(search.Types.First(type => type.Label == "Chests"));
        var marked = search.Bases.Single(line => line.Code == "rar");
        Assert.Equal("Boneweave (S)", marked.Label);
        Assert.Contains(search.Bases, line => line.Code == "uhn" && line.Label == "Boneweave");

        search.SetBase(marked);
        Assert.Equal([ForgeSearch.Unique], search.Rarities.Select(rarity => rarity.From ?? -1));
        Assert.Equal(ForgeSearch.Unique, search.Rarity.From);
        Assert.Equal("Cage of the Unsullied", search.Named?.Name);
        Assert.Equal("Boneweave (S)", search.Named?.Detail);

        // No runeword is made in it, six sockets or not: nor is it the best base for one.
        var words = new ForgeSearch();
        words.Load();
        words.SetRarity(words.Rarities.First(rarity => rarity.From == ForgeSearch.Runeword));
        foreach (var word in words.Names.Where(entry => entry.Types!.Contains("tors")))
        {
            Assert.False(words.Takes(word, "rar"), word.Name);
            Assert.NotEqual("rar", ForgeSearch.Make(new ForgeSearch.Order(ForgeSearch.Runeword, word, null), Stamp).Code.Trim());
        }

        // A unique map: its base under Maps, Unique alone.
        var maps = new ForgeSearch();
        maps.Load();
        maps.SetType(maps.Types.First(type => type.Label == "Maps"));
        Assert.Equal("Map (Warlord of Blood)", maps.Bases.Single(line => line.Code == "t52").Label);
        maps.SetBase(maps.Bases.Single(line => line.Code == "t52"));
        Assert.Equal([ForgeSearch.Unique], maps.Rarities.Select(rarity => rarity.From ?? -1));
        Assert.NotNull(maps.Named);
        var made = ForgeSearch.Make(maps.What!, Stamp);
        Assert.Equal(ItemQuality.Unique, made.Quality);
        Assert.Equal("t52", made.Code.Trim());
    }

    [CorpusFact]
    public void Bases_with_no_tier_come_after_the_tiers()
    {
        if (!game.Available)
            return;

        var search = new ForgeSearch();
        search.Load();
        search.SetType(search.Types.First(type => type.Label == "Weapons"));

        var ranks = search.Bases.Skip(1).Select(line => ItemTier.Rank(line.Code) ?? 3).ToList();
        Assert.Contains(3, ranks);
        Assert.Equal(ranks.OrderBy(each => each), ranks);

        // Within a tier, by name.
        foreach (var tier in search.Bases.Skip(1).GroupBy(line => ItemTier.Rank(line.Code) ?? 3))
        {
            var names = tier.Select(line => line.Label).ToList();
            Assert.Equal(names.OrderBy(each => each, StringComparer.OrdinalIgnoreCase), names);
        }
    }

    [CorpusFact]
    public void A_plain_map_is_the_map_the_game_drops()
    {
        if (!game.Available)
            return;

        string path = Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s");
        if (!File.Exists(path))
            return;

        int bits = D2SLib.Globals.stat_id_bits;

        byte[] Blob(Item item)
        {
            var copy = ItemBlob.Restore(Item.Write(item, ItemBlob.Version), bits);
            ItemBlob.Normalise(copy);
            copy.IsNew = false;
            return ItemBlob.TryCapture(copy, bits, out var blob, out _, out string? why) ? blob : throw new InvalidOperationException(why);
        }

        var real = (SaveVerifier.Load(path).Save.PlayerItemList?.Items ?? [])
            .Where(item => ItemOrder.TypeName(ItemOrder.TypeRank(item)) == "Maps" && item.Quality == ItemQuality.Normal)
            .ToList();
        Assert.NotEmpty(real);

        foreach (var theirs in real)
        {
            var ours = ItemForge.BaseAs(theirs.Code.Trim(), ItemQuality.Normal, theirs.Version).Item;
            Assert.Equal(1, (int)ours.Quantity);

            // Where it dropped and how many are in the stack are the item's own; the rest is the base's.
            ours.ItemLevel = theirs.ItemLevel;
            ours.Quantity = theirs.Quantity;
            Assert.True(Blob(ours).AsSpan().SequenceEqual(Blob(ItemBlob.Copy(theirs, bits))), $"{theirs.Code.Trim()} differs from the game's");
        }
    }

    [CorpusFact]
    public void A_superiors_range_is_its_own_rows()
    {
        if (!game.Available)
            return;

        static Item Superior(string code) => ItemForge.BaseAs(code, ItemQuality.Superior, Stamp).Item;
        static string Words(QualityItems.Bonus bonus) => string.Join("+", bonus.Mods.Select(mod => mod.Code));
        static (int Min, int Max) Range(Item item, string code) =>
            QualityItems.For(item).Where(each => each.Code == code).Select(each => (each.Min, each.Max)).Single();

        // No bonus yet: every row it could roll, at its widest.
        var pike = Superior("pik");
        Assert.Equal((5, 20), Range(pike, "dmg%"));
        Assert.Equal((10, 25), Range(pike, "att%"));

        // Enhanced Damage alone is 10-20; with Durability each is five lower, and nothing else is offered.
        QualityItems.Put(pike, QualityItems.RowsFor(pike).First(bonus => Words(bonus) == "dmg%"));
        Assert.Equal([("dmg%", 10, 20)], QualityItems.For(pike));

        QualityItems.Put(pike, QualityItems.RowsFor(pike).First(bonus => Words(bonus) == "dmg%+dur%"));
        Assert.Equal((5, 15), Range(pike, "dmg%"));
        Assert.Equal((10, 20), Range(pike, "dur%"));
        Assert.Equal(2, QualityItems.For(pike).Count);

        var plate = Superior("uar");
        QualityItems.Put(plate, QualityItems.RowsFor(plate).First(bonus => Words(bonus) == "ac%"));
        Assert.Equal([("ac%", 10, 20)], QualityItems.For(plate));

        // A weapon's damage bonus written flat, as the game writes it on a small weapon, is still that
        // bonus: beside Attack Rating it is their pair's row.
        var dagger = Superior("dgr");
        QualityItems.Put(dagger, QualityItems.RowsFor(dagger).First(bonus => Words(bonus) == "att%"));
        Assert.Equal([("att%", 15, 25)], QualityItems.For(dagger));
        dagger.StatLists[0].Stats.Add(new ItemStat { Id = StatCatalogue.IdOf("maxdamage"), Stat = "maxdamage", Value = 1 });
        Assert.Equal((10, 20), Range(dagger, "att%"));
        Assert.Contains(QualityItems.For(dagger), each => each.Code == "dmg%");

        // And held to it: the 25% that Attack Rating alone may be is over the pair's 20.
        Assert.NotEmpty(Legitimacy.Check(dagger));
        dagger.StatLists[0].Stats.First(stat => stat.Stat == "item_tohit_percent").Value = 18;
        Assert.Empty(Legitimacy.Check(dagger));

        // A runeword's base: a lone bonus may be half of a pair whose other half the word's own lines
        // hold (an item built from the armory's page), so it is not held to the row of one alone.
        var word = Superior("uar");
        QualityItems.Put(word, QualityItems.RowsFor(word).First(bonus => Words(bonus) == "dur%"));
        word.IsRuneword = true;
        Assert.Equal((10, 25), Range(word, "dur%"));
        QualityItems.Put(word, QualityItems.RowsFor(word).First(bonus => Words(bonus) == "ac%+dur%"));
        Assert.Equal((5, 15), Range(word, "ac%"));
        Assert.Equal((10, 20), Range(word, "dur%"));

        // The check says so: 20% Enhanced Defense beside 20% Durability is what no row gives.
        QualityItems.Put(plate, QualityItems.RowsFor(plate).First(bonus => Words(bonus) == "ac%+dur%"));
        Assert.Empty(Legitimacy.Check(plate));
        plate.StatLists[0].Stats.First(stat => stat.Stat == "item_armor_percent").Value = 20;
        Assert.NotEmpty(Legitimacy.Check(plate));
    }
}
