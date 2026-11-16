using D2SLib.Model.Save;

using PD2SPCompanion.Core.Collection;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// The Forge's four boxes - type, base, rarity, name (ForgeSearch) - chosen in
/// every order and at random against the installed tables - all kinds of combination and
/// scenario, to find bugs. After every
/// choice the boxes must hold only what their lists offer, agree with each other, and name either nothing
/// or an item that is made as named.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class ForgeSearchTests(Game game)
{
    private const string Stamp = "103";

    private static ForgeSearch Loaded()
    {
        var search = new ForgeSearch();
        search.Load();
        return search;
    }

    /// <summary>What must hold after any choice, whatever came before it. Says what does not.</summary>
    private static string? Wrong(ForgeSearch search, Dictionary<string, string?> made)
    {
        if (!search.Types.Contains(search.Type)) return "the type is not among the types";
        if (!search.Bases.Contains(search.Base)) return "the base is not among the bases";
        if (!search.Rarities.Contains(search.Rarity)) return "the rarity is not among the rarities";
        if (search.Named is not null && !search.Names.Contains(search.Named)) return "the name is not among the names";

        bool named = search.Rarity.From <= ForgeSearch.Runeword;
        if (search.CanPickName != named) return "a name is offered for a rarity that has none, or not for one that has";
        if (!named && (search.Names.Count > 0 || search.Named is not null)) return "names are listed for a rarity that has none";
        if (named && search.Named is null && search.Names.Count == 1) return "one name left and not chosen";
        if (search.CanPickBase != search.Bases.Count > 1) return "the base box is open with nothing in it, or shut with something";
        if (search.Bases[0] != ForgeSearch.Any) return "the bases do not begin with Any";

        bool word = search.Rarity.From == ForgeSearch.Runeword && search.Named is not null;
        if (search.Type.IsAny && !word && search.Bases.Count > 1) return "bases are offered with no type and no runeword";
        if (word && search.Bases.Skip(1).Any(line => !search.Takes(search.Named!, line.Code!))) return "a base is offered that the runeword chosen does not take";

        if (search.Base.Code is { } code)
        {
            if (search.Named is { } name && !search.Takes(name, code)) return $"{name.Name} is not made on the base chosen";
            if (!search.Type.IsAny)
            {
                int rank = ItemOrder.TypeRankOf(code);
                if (rank < search.Type.From || rank > search.Type.To) return "the base is outside the type";
            }
        }

        var what = search.What;
        if (named != (what?.Named is not null || (named && search.Named is null))) return "an order with a name for a rarity without, or the other way";
        if (named && (what is null) != (search.Named is null)) return "a name chosen and nothing to make, or the other way";
        if (!named && (what is null) != search.Base.IsAny) return "a base chosen and nothing to make, or the other way";

        if (what is null)
            return null;

        // Each order made once: the same order is the same item.
        string key = $"{what.Rarity}/{what.Named?.Name}/{what.Named?.Detail}/{what.Named?.Row}/{what.Code}";
        if (!made.TryGetValue(key, out string? fault))
        {
            fault = null;
            try
            {
                var item = ForgeSearch.Make(what, Stamp);
                string on = item.Code.Trim();

                if (what.Code is { } asked && !string.Equals(on, asked, StringComparison.OrdinalIgnoreCase))
                    fault = $"asked on {asked}, made on {on}";
                else if (what.Rarity == ForgeSearch.Runeword && !item.IsRuneword)
                    fault = "asked a runeword, made none";
                else if (what.Rarity == ForgeSearch.Runeword && !search.Takes(what.Named!, on))
                    fault = $"the runeword was made in {on}, which does not take it";
                else if (what.Rarity > ForgeSearch.Runeword)
                {
                    var wanted = what.Rarity switch
                    {
                        ForgeSearch.Crafted => ItemQuality.Craft,
                        ForgeSearch.Rare => ItemQuality.Rare,
                        ForgeSearch.Magic => ItemQuality.Magic,
                        ForgeSearch.Superior => ItemQuality.Superior,
                        _ => ItemQuality.Normal,
                    };
                    if (item.Quality != wanted)
                        fault = $"asked {wanted}, made {item.Quality}";
                }

                if (fault is null)
                {
                    byte[] bytes = Item.Write(item, 0x60);
                    if (!bytes.AsSpan().SequenceEqual(Item.Write(Item.Read(bytes, 0x60), 0x60)))
                        fault = "read back another item";
                }
            }
            catch (Exception problem)
            {
                fault = $"not made: {problem.Message}";
            }

            made[key] = fault;
        }

        return fault is null ? null : $"{key}: {fault}";
    }

    private static string Says(ForgeSearch search) =>
        $"[{search.Type.Label} / {search.Base.Label} / {search.Rarity.Label} / {search.Named?.Name ?? "-"} ({search.Named?.Detail})]";

    [GameFact]
    [Trait("Speed", "Slow")]
    public void Any_choice_at_random_leaves_the_boxes_agreeing_and_the_item_made()
    {
        if (!game.Available)
            return;

        var search = Loaded();
        var made = new Dictionary<string, string?>();
        var random = new Random(20261004);
        var faults = new List<string>();

        string[] typed = ["war", "pike", "insight", "spirit", "ring", "tal", "armor", "sword", "charm", "enigma", "bow", "of", "grief", "shield", "amulet", "jewel", "boots", "griffon", "stone", "a"];

        for (int step = 0; step < 4000 && faults.Count < 12; step++)
        {
            string did;
            switch (random.Next(6))
            {
                case 0:
                    var type = search.Types[random.Next(search.Types.Count)];
                    search.SetType(type);
                    did = $"type {type.Label}";
                    break;
                case 1:
                    var @base = search.Bases[random.Next(search.Bases.Count)];
                    search.SetBase(@base);
                    did = $"base {@base.Label}";
                    break;
                case 2:
                    var rarity = search.Rarities[random.Next(search.Rarities.Count)];
                    search.SetRarity(rarity);
                    did = $"rarity {rarity.Label}";
                    break;
                case 3 when search.Names.Count > 0:
                    var name = search.Names[random.Next(search.Names.Count)];
                    search.SetNamed(name);
                    did = $"name {name.Name}";
                    break;
                case 4:
                    var finds = search.Find(typed[random.Next(typed.Length)], 40, out _);
                    if (finds.Count == 0)
                        continue;
                    var find = finds[random.Next(finds.Count)];
                    search.Pick(find);
                    did = $"find {find.Name} ({find.Detail})";
                    if (find.Rarity <= ForgeSearch.Runeword && !ReferenceEquals(search.Named, find))
                        faults.Add($"step {step}, {did}: the find is not the name chosen {Says(search)}");
                    if (find.Code is { } on && !string.Equals(search.Base.Code, on, StringComparison.OrdinalIgnoreCase))
                        faults.Add($"step {step}, {did}: the find's base is not the base chosen {Says(search)}");
                    break;
                default:
                    // A choice offered and already made changes nothing.
                    search.SetRarity(search.Rarity);
                    did = "the same rarity again";
                    break;
            }

            if (Wrong(search, made) is { } wrong)
                faults.Add($"step {step}, after {did}: {wrong} {Says(search)}");
        }

        Assert.True(made.Count > 300, $"only {made.Count} different items asked for: the walk went nowhere");
        Assert.True(faults.Count == 0, string.Join(Environment.NewLine, faults));
    }

    /// <summary>The four boxes set towards one item, in an order; a box is set when its list offers what is wanted.</summary>
    private static bool Reach(ForgeSearch search, int[] order, string? type, string? code, int rarity, Func<ForgeSearch.Entry, bool>? name)
    {
        for (int pass = 0; pass < 6; pass++)
        {
            foreach (int box in order)
            {
                switch (box)
                {
                    case 0 when search.Types.FirstOrDefault(line => type is null ? line.IsAny : line.Label == type) is { } wanted:
                        search.SetType(wanted);
                        break;
                    case 1 when code is not null && search.Bases.FirstOrDefault(line => line.Code == code) is { } on:
                        search.SetBase(on);
                        break;
                    case 2 when search.Rarities.FirstOrDefault(line => line.From == rarity) is { } kind:
                        search.SetRarity(kind);
                        break;
                    case 3 when name is not null && search.Names.FirstOrDefault(name) is { } which:
                        search.SetNamed(which);
                        break;
                }
            }

            bool there = search.Rarity.From == rarity
                         && (code is null || search.Base.Code == code)
                         && (name is null || (search.Named is not null && name(search.Named)));
            if (there)
                return true;
        }

        return false;
    }

    private static IEnumerable<int[]> Orders()
    {
        int[] boxes = [0, 1, 2, 3];
        foreach (int a in boxes)
            foreach (int b in boxes.Where(x => x != a))
                foreach (int c in boxes.Where(x => x != a && x != b))
                    yield return [a, b, c, boxes.First(x => x != a && x != b && x != c)];
    }

    [GameFact]
    [Trait("Speed", "Slow")]
    public void An_item_is_reached_whatever_order_its_boxes_are_set_in()
    {
        if (!game.Available)
            return;

        // What is wanted: the type's label, the base, the rarity, and which name.
        (string What, string? Type, string? Code, int Rarity, Func<ForgeSearch.Entry, bool>? Name)[] wanted =
        [
            ("War Traveler", "Boots", "xtb", ForgeSearch.Unique, entry => entry.Name == "War Traveler"),
            ("a unique with no type chosen", null, null, ForgeSearch.Unique, entry => entry.Name == "War Traveler"),
            ("Tal Rasha's Guardianship", "Chests", "uth", ForgeSearch.Set, entry => entry.Name.StartsWith("Tal Rasha")),
            ("Insight in a Pike", "Weapons: Spears", "pik", ForgeSearch.Runeword, entry => entry.Name == "Insight"),
            ("Insight in a Pike, no type chosen", null, "pik", ForgeSearch.Runeword, entry => entry.Name == "Insight"),
            ("Spirit in a Monarch", "Shields", "uit", ForgeSearch.Runeword, entry => entry.Name == "Spirit"),
            ("a rare Ring", "Rings", "rin", ForgeSearch.Rare, null),
            ("a magic Grand Charm", "Charms", "cm3", ForgeSearch.Magic, null),
            ("a superior Sacred Armor", "Chests", "uar", ForgeSearch.Superior, null),
            ("a crafted Pike", "Weapons: Spears", "pik", ForgeSearch.Crafted, null),
            ("a normal Monarch", "Shields", "uit", ForgeSearch.Normal, null),
        ];

        var faults = new List<string>();
        var made = new Dictionary<string, string?>();

        foreach (var want in wanted)
        {
            foreach (int[] order in Orders())
            {
                // From nothing, and from a page left on something else entirely.
                foreach (bool dirty in new[] { false, true })
                {
                    var search = Loaded();
                    if (dirty)
                        search.Pick(search.Find("griffon", 5, out _)[0]);

                    string how = $"{want.What}, boxes in order {string.Join("", order)}{(dirty ? ", from another item" : "")}";
                    if (!Reach(search, order, want.Type, want.Code, want.Rarity, want.Name))
                        faults.Add($"{how}: not reached {Says(search)}");
                    else if (Wrong(search, made) is { } wrong)
                        faults.Add($"{how}: {wrong} {Says(search)}");
                    else if (search.What is null)
                        faults.Add($"{how}: nothing to make {Says(search)}");
                }
            }
        }

        Assert.True(faults.Count == 0, string.Join(Environment.NewLine, faults.Take(30)));
    }

    [GameFact]
    public void A_runeword_chosen_first_offers_its_bases_with_no_type()
    {
        if (!game.Available)
            return;

        var search = Loaded();
        Assert.False(search.CanPickBase);

        search.SetRarity(search.Rarities.First(line => line.From == ForgeSearch.Runeword));
        Assert.False(search.CanPickBase);

        search.SetNamed(search.Names.First(entry => entry.Name == "Insight"));
        Assert.True(search.CanPickBase, "a runeword chosen: its base is the next thing to choose");
        Assert.Contains(search.Bases, line => line.Code == "pik");
        Assert.DoesNotContain(search.Bases, line => line.Code == "rin");

        // Made in the best base of its kinds until one is chosen; then in that one, the word kept.
        var best = ForgeSearch.Make(search.What!, Stamp);
        Assert.True(best.IsRuneword);

        search.SetBase(search.Bases.First(line => line.Code == "pik"));
        Assert.Equal("Insight", search.Named?.Name);
        Assert.Equal("pik", search.What?.Code);
        Assert.Equal("pik", ForgeSearch.Make(search.What!, Stamp).Code.Trim());

        // Another word of the same base: the base stays. One the base does not take is not offered.
        Assert.DoesNotContain(search.Names, entry => entry.Name == "Enigma");
        search.SetBase(ForgeSearch.Any);
        Assert.Contains(search.Names, entry => entry.Name == "Enigma");

        // Away from runewords, the bases are the type's again: none, with no type.
        search.SetRarity(search.Rarities.First(line => line.From == ForgeSearch.Unique));
        Assert.False(search.CanPickBase);
    }

    [GameFact]
    public void Nothing_is_listed_that_the_forge_does_not_make()
    {
        if (!game.Available)
            return;

        // A unique map is listed, as the Forge makes maps; else it would be the one thing listed on a
        // base the Forge left alone.
        var search = Loaded();
        Assert.Contains(search.Find("warlord of blood", 40, out _), entry => entry.Rarity == ForgeSearch.Unique);
        Assert.DoesNotContain(search.Names, entry => entry.Code is { } code && !ItemForge.Makes(code));
        Assert.DoesNotContain(search.Find("potion", 400, out _), entry => entry.Code is { } code && !ItemForge.Makes(code));
    }

    [GameFact]
    public void A_base_says_its_tier_and_the_list_goes_by_it()
    {
        if (!game.Available)
            return;

        var search = Loaded();
        search.SetType(search.Types.First(line => line.Label == "Chests"));
        Assert.Equal("Elite", search.Bases.First(line => line.Code == "uar").Tier);
        Assert.Equal("Normal", search.Bases.First(line => line.Code == "qui").Tier);

        // Normal, then exceptional, then elite; by name within each.
        var tiers = search.Bases.Skip(1).Select(line => ItemTier.Rank(line.Code) ?? 0).ToList();
        Assert.Equal(tiers.OrderBy(rank => rank), tiers);
        foreach (var tier in search.Bases.Skip(1).GroupBy(line => line.Tier))
            Assert.Equal(tier.Select(line => line.Label).OrderBy(name => name, StringComparer.OrdinalIgnoreCase), tier.Select(line => line.Label));

        // The Circlet's row names a tier above and no elite: Normal all the same, among its kind.
        search.SetType(search.Types.First(line => line.Label == "Helms"));
        Assert.Equal("Normal", search.Bases.First(line => line.Code == "ci0").Tier);
        Assert.Equal("Normal", search.Bases.First(line => line.Code == "ci1").Tier);
        Assert.Equal("Exceptional", search.Bases.First(line => line.Code == "ci2").Tier);
        Assert.Equal("Elite", search.Bases.First(line => line.Code == "ci3").Tier);
        Assert.DoesNotContain(search.Bases.Skip(1), line => line.Tier.Length == 0);


        // A ring has no tiers.
        search.SetType(search.Types.First(line => line.Label == "Rings"));
        Assert.Equal("", search.Bases.First(line => line.Code == "rin").Tier);
    }
}
