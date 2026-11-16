using D2SLib.Model.Save;

using static PD2SPCompanion.Tests.Items.AffixSweepTests;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// Every other way the Edit page and the Forge put lines on an item and take them off, swept as the affixes are
/// (<see cref="AffixSweepTests"/>):
/// put on and taken off, the item is as it was; swapped, it
/// is as a fresh one with the new choice. The corruption, the desecration, a craft's family, a runeword, the staff
/// mods, a map's recipes, a rare's six slots, the Perfect Skull's whole new set, the Horadric Cube's upgrade.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class EditSweepTests(Game game)
{
    /// <summary>The lines, and what the header says beside them: the socket count, what is socketed, the word.</summary>
    private static List<string> State(Item item) =>
        [.. Lines(item), $"#sockets={item.TotalNumberOfSockets}", $"#socketed={item.SocketedItems.Count}", $"#runeword={item.IsRuneword}"];

    private static string Diff(List<string> now, List<string> wanted) =>
        $"extra [{string.Join(", ", now.Except(wanted))}] missing [{string.Join(", ", wanted.Except(now))}]";

    private static Item As(string code, ItemQuality quality)
    {
        var item = Fresh(code);
        item.Quality = quality;
        return item;
    }

    private static void Report(List<string> failed, int tried, int least)
    {
        Assert.True(tried >= least, $"only {tried} tried");
        Assert.True(failed.Count == 0, $"{failed.Count} of {tried}:{Environment.NewLine}{string.Join(Environment.NewLine, failed.Take(60))}");
    }

    [GameFact]
    [Trait("Speed", "Slow")]
    public void Every_corruption_cleaned_or_changed_leaves_the_item_as_it_would_be_without_it()
    {
        if (!game.Available)
            return;

        var failed = new List<string>();
        int tried = 0;
        foreach (string code in Bases)
        {
            foreach (var quality in new[] { ItemQuality.Magic, ItemQuality.Rare, ItemQuality.Normal })
            {
                var options = Corruptions.Available(As(code, quality));
                for (int n = 0; n < options.Count; n++)
                {
                    var (option, next) = (options[n], options[(n + 1) % options.Count]);
                    tried++;

                    var item = As(code, quality);
                    var before = State(item);
                    Corruptions.Apply(item, option, new Random(option.Id));
                    Corruptions.Clean(item, new Random(1));
                    if (!State(item).SequenceEqual(before))
                        failed.Add($"{code} {quality} corruption {option.Id} cleaned: {Diff(State(item), before)}");

                    var changed = As(code, quality);
                    Corruptions.Apply(changed, option, new Random(option.Id));
                    Corruptions.Clean(changed, new Random(1));
                    Corruptions.Apply(changed, next, new Random(next.Id));
                    var straight = As(code, quality);
                    Corruptions.Apply(straight, next, new Random(next.Id));
                    if (!State(changed).SequenceEqual(State(straight)))
                        failed.Add($"{code} {quality} corruption {option.Id} -> {next.Id}: {Diff(State(changed), State(straight))}");
                }
            }
        }

        Report(failed, tried, 200);
    }

    [GameFact]
    [Trait("Speed", "Slow")]
    public void Every_desecration_cleaned_or_changed_leaves_the_corrupted_item_as_it_was()
    {
        if (!game.Available)
            return;

        // Amulets alone are desecrated, corrupted ones (Desecrations.CanTake): every desecration over every corruption
        // an amulet can carry, since the two may write one line.
        var failed = new List<string>();
        int tried = 0;
        foreach (var corruption in Corruptions.Available(As("amu", ItemQuality.Rare)).Where(option => option.Grants.Count > 0))
        {
            Item Corrupted()
            {
                var item = As("amu", ItemQuality.Rare);
                Corruptions.Apply(item, corruption, new Random(2));
                return item;
            }

            var pool = Desecrations.Pool(Corrupted());
            for (int n = 0; n < pool.Count; n++)
            {
                var (mod, next) = (pool[n], pool[(n + 1) % pool.Count]);
                tried++;

                var item = Corrupted();
                var before = State(item);
                Desecrations.Apply(item, Corruptions.Draft(item, mod, new Random(3)));
                // Where every line the desecration wrote is the corruption's too, its share cannot be told from the
                // corruption's: the lines come back as the corruption could have rolled them - the same lines, an item
                // the game makes, one roll's lines alike. Everywhere else, exactly as before.
                bool allShared = mod.Grants.SelectMany(grant => AffixRanges.StatsWritten(grant.Code))
                    .All(stat => corruption.Grants.SelectMany(grant => AffixRanges.StatsWritten(grant.Code)).Contains(stat, StringComparer.OrdinalIgnoreCase));
                if (!Desecrations.Clean(item, mod, new Random(1), out _))
                    failed.Add($"corruption {corruption.Id}, desecration {mod.Id}: not cleaned");
                else if (allShared && (!Keys(item).SequenceEqual(Keys(Corrupted())) || Legitimacy.Judge(item).Count > 0))
                    failed.Add($"corruption {corruption.Id}, desecration {mod.Id} cleaned, all shared: {Diff(State(item), before)}; {string.Join("; ", Legitimacy.Judge(item).Select(finding => finding.ToString()))}");
                else if (!allShared && !State(item).SequenceEqual(before))
                    failed.Add($"corruption {corruption.Id}, desecration {mod.Id} cleaned: {Diff(State(item), before)}");

                if (next.Id == mod.Id)
                    continue;

                var changed = Corrupted();
                Desecrations.Apply(changed, Corruptions.Draft(changed, mod, new Random(3)));
                Desecrations.Apply(changed, Corruptions.Draft(changed, next, new Random(4)), mod, new Random(1));
                var straight = Corrupted();
                Desecrations.Apply(straight, Corruptions.Draft(straight, next, new Random(4)));
                if (!Keys(changed).SequenceEqual(Keys(straight)))
                    failed.Add($"corruption {corruption.Id}, desecration {mod.Id} -> {next.Id}: {Diff(Keys(changed), Keys(straight))}");
            }
        }

        Report(failed, tried, 40);
    }

    [GameFact]
    [Trait("Speed", "Slow")]
    public void Every_craft_family_changed_leaves_the_item_as_a_fresh_craft_of_the_new_one_its_affix_kept()
    {
        if (!game.Available)
            return;

        var failed = new List<string>();
        int tried = 0;
        foreach (string code in Bases)
        {
            var families = AffixRanges.CraftFamilies(As(code, ItemQuality.Craft));
            for (int n = 0; n < families.Count && families.Count > 1; n++)
            {
                var (family, next) = (families[n], families[(n + 1) % families.Count]);
                if (next.Family == family.Family)
                    continue;
                tried++;

                // With an affix the craft rolled beside it, which the change must leave alone.
                Item Crafted(AffixRanges.CraftRecipe recipe)
                {
                    var item = As(code, ItemQuality.Craft);
                    Crafts.Reforge(item, Crafts.Draft(recipe, new Random(5)), new Random(5));
                    if (MagicAffixes.Available(item, prefix: true, 0).FirstOrDefault(option => !option.NeedsParam) is { } affix)
                    {
                        MagicAffixes.Apply(item,
                            new Dictionary<(int, bool), MagicAffixes.AffixPick> { [(0, true)] = new(affix.Id, MagicAffixes.Roll(affix, new Random(affix.Id))) },
                            new Random(1));
                    }
                    return item;
                }

                var changed = Crafted(family);
                Crafts.Reforge(changed, Crafts.Draft(next, new Random(5)), new Random(5));
                var straight = Crafted(next);
                if (!Keys(changed).SequenceEqual(Keys(straight)))
                    failed.Add($"{code} craft {family.Family} -> {next.Family}: {Diff(Keys(changed), Keys(straight))}");
            }
        }

        Report(failed, tried, 10);
    }

    [GameFact]
    [Trait("Speed", "Slow")]
    public void Every_runeword_undone_or_changed_leaves_the_base_as_it_was()
    {
        if (!game.Available)
            return;

        var failed = new List<string>();
        int tried = 0;
        foreach (string code in Bases)
        {
            for (int sockets = 2; sockets <= 6; sockets++)
            {
                Item Base()
                {
                    var item = As(code, ItemQuality.Normal);
                    item.TotalNumberOfSockets = (byte)sockets;
                    item.IsSocketed = true;
                    return item;
                }

                var words = RunewordBuilder.WordsFor(Base());
                for (int n = 0; n < words.Count; n++)
                {
                    var (word, next) = (words[n], words[(n + 1) % words.Count]);
                    tried++;

                    var item = Base();
                    var before = State(item);
                    ItemForge.MakeRuneword(item, RunewordBuilder.Roll(word, new Random(6)));
                    RunewordBuilder.Clean(item);
                    if (!State(item).SequenceEqual(before))
                        failed.Add($"{code} [{sockets}] {Runewords.DisplayName(word)} undone: {Diff(State(item), before)}");

                    if (ReferenceEquals(next, word))
                        continue;

                    var changed = Base();
                    ItemForge.MakeRuneword(changed, RunewordBuilder.Roll(word, new Random(6)));
                    ItemForge.MakeRuneword(changed, RunewordBuilder.Roll(next, new Random(7)));
                    var straight = Base();
                    ItemForge.MakeRuneword(straight, RunewordBuilder.Roll(next, new Random(7)));
                    if (!State(changed).SequenceEqual(State(straight)))
                        failed.Add($"{code} [{sockets}] {Runewords.DisplayName(word)} -> {Runewords.DisplayName(next)}: {Diff(State(changed), State(straight))}");
                }
            }
        }

        Report(failed, tried, 50);
    }

    [GameFact]
    [Trait("Speed", "Slow")]
    public void Staff_mods_changed_or_taken_off_leave_the_item_as_it_would_be_with_the_new_set_or_none()
    {
        if (!game.Available)
            return;

        var failed = new List<string>();
        int tried = 0;
        foreach (string code in Bases)
        {
            if (!StaffMods.Rolls(As(code, ItemQuality.Magic)))
                continue;

            for (int seed = 0; seed < 40; seed++)
            {
                tried++;
                var first = StaffMods.Roll(As(code, ItemQuality.Magic), new Random(seed));
                var second = StaffMods.Roll(As(code, ItemQuality.Magic), new Random(seed + 1000));

                var item = As(code, ItemQuality.Magic);
                var before = State(item);
                StaffMods.Put(item, first);
                StaffMods.Put(item, []);
                if (!State(item).SequenceEqual(before))
                    failed.Add($"{code} staff mods {seed} off: {Diff(State(item), before)}");

                var changed = As(code, ItemQuality.Magic);
                StaffMods.Put(changed, first);
                StaffMods.Put(changed, second);
                var straight = As(code, ItemQuality.Magic);
                StaffMods.Put(straight, second);
                if (!State(changed).SequenceEqual(State(straight)))
                    failed.Add($"{code} staff mods {seed} changed: {Diff(State(changed), State(straight))}");
            }
        }

        Report(failed, tried, 100);
    }

    [GameFact]
    [Trait("Speed", "Slow")]
    public void Every_map_recipe_taken_off_leaves_the_map_as_it_was()
    {
        if (!game.Available)
            return;

        var failed = new List<string>();
        int tried = 0;
        foreach (string code in new[] { "t11", "t21", "t31", "t42", "t43" })
        {
            foreach (var quality in new[] { ItemQuality.Normal, ItemQuality.Magic, ItemQuality.Rare })
            {
                Item Map()
                {
                    var map = ItemForge.BaseAs(code, quality, "103", new Random(5)).Item;
                    Array.Clear(map.MagicPrefixIds!);
                    Array.Clear(map.MagicSuffixIds!);
                    ItemStats.Home(map).Stats.Clear();
                    MapRecipes.Dress(map);
                    return map;
                }

                foreach (var recipe in MapRecipes.Available(Map()))
                {
                    tried++;
                    var map = Map();
                    var before = State(map);
                    if (!MapRecipes.Apply(map, recipe))
                        continue;
                    MapRecipes.TakeOff(map, recipe);
                    if (!State(map).SequenceEqual(before))
                        failed.Add($"{code} {quality} recipe {recipe.Ingredient} off: {Diff(State(map), before)}");
                }
            }
        }

        Report(failed, tried, 10);
    }

    [GameFact]
    [Trait("Speed", "Slow")]
    public void A_rares_six_slots_emptied_leave_the_base_and_a_whole_new_set_leaves_nothing_of_the_old()
    {
        if (!game.Available)
            return;

        var failed = new List<string>();
        int tried = 0;
        foreach (string code in Bases)
        {
            for (int seed = 0; seed < 25; seed++)
            {
                tried++;
                var item = As(code, ItemQuality.Rare);
                var before = State(item);

                // Six, one slot at a time, each the first the pool offers it.
                var random = new Random(seed);
                foreach (bool prefix in new[] { true, false })
                {
                    for (int slot = 0; slot < 3; slot++)
                    {
                        var pool = MagicAffixes.Available(item, prefix, slot).Where(option => !option.NeedsParam).ToList();
                        if (pool.Count == 0)
                            continue;
                        var option = pool[random.Next(pool.Count)];
                        MagicAffixes.Apply(item,
                            new Dictionary<(int, bool), MagicAffixes.AffixPick> { [(slot, prefix)] = new(option.Id, MagicAffixes.Roll(option, random)) },
                            random);
                    }
                }

                var empty = new Dictionary<(int, bool), MagicAffixes.AffixPick>();
                foreach (bool prefix in new[] { true, false })
                    for (int slot = 0; slot < 3; slot++)
                        empty[(slot, prefix)] = new(0, new Dictionary<string, int>());
                MagicAffixes.Apply(item, empty, new Random(1));
                if (!State(item).SequenceEqual(before))
                    failed.Add($"{code} rare {seed} emptied: {Diff(State(item), before)}");

                // The Perfect Skull twice, against once.
                var twice = As(code, ItemQuality.Rare);
                MagicAffixes.ApplyFresh(twice, MagicAffixes.RollFresh(twice, new Random(seed)), new Random(1));
                var picks = MagicAffixes.RollFresh(As(code, ItemQuality.Rare), new Random(seed + 500));
                MagicAffixes.ApplyFresh(twice, picks, new Random(1));
                var once = As(code, ItemQuality.Rare);
                MagicAffixes.ApplyFresh(once, picks, new Random(1));
                if (!Keys(twice).SequenceEqual(Keys(once)))
                    failed.Add($"{code} rare {seed} new set over old: {Diff(Keys(twice), Keys(once))}");
            }
        }

        Report(failed, tried, 200);
    }

    [GameFact]
    [Trait("Speed", "Slow")]
    public void A_base_upgraded_keeps_its_lines()
    {
        if (!game.Available)
            return;

        var failed = new List<string>();
        int tried = 0;
        foreach (string code in Bases)
        {
            foreach (var quality in new[] { ItemQuality.Magic, ItemQuality.Rare })
            {
                var item = As(code, quality);
                var random = new Random(8);
                if (MagicAffixes.Available(item, prefix: true, 0).FirstOrDefault(option => !option.NeedsParam) is { } affix)
                {
                    MagicAffixes.Apply(item,
                        new Dictionary<(int, bool), MagicAffixes.AffixPick> { [(0, true)] = new(affix.Id, MagicAffixes.Roll(affix, random)) },
                        random);
                }

                if (!BaseUpgrades.CanUpgrade(item, anyItem: true))
                    continue;

                tried++;
                var before = Keys(item);
                BaseUpgrades.Upgrade(item, new Random(9));
                if (!Keys(item).SequenceEqual(before))
                    failed.Add($"{code} {quality} upgraded to {item.Code.Trim()}: {Diff(Keys(item), before)}");
            }
        }

        Report(failed, tried, 10);
    }
}
