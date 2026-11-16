using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// What a forged item's base brings (ItemForge.DressBase): a quantity where the base stacks, and durability
/// where the base has any. Forged without a quantity, a library javelin was a stack of nought and was gone
/// with its first throw. Real ladder items: all 268 throwing weapons store
/// both a durability and a quantity (50 or 100 of the table's 25-100), all 785 quivers a quantity and no
/// durability, all 806 bows a durability.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class ForgedBaseTests(Game game)
{
    private const string Stamp = "103";

    [GameFact]
    public void Every_forged_item_has_its_stack_and_its_durability()
    {
        if (!game.Available)
            return;

        var wrong = new List<string>();
        int stacked = 0;

        void Look(string name, D2SLib.Model.Save.Item item)
        {
            string code = item.Code.Trim();
            if (Stacks.MaximumFor(code) is { } most)
            {
                stacked++;
                if (item.Quantity != most)
                    wrong.Add($"{name} [{code}]: quantity {item.Quantity}, the base stacks to {most}");
            }

            var row = D2SLib.Core.TXT.ItemsTXT.GetByCode(code);
            bool hasDurability = row is not null
                && int.TryParse(row.Get("durability")?.Value?.Trim(), out int durability)
                && durability > 0;
            if (hasDurability && item.MaxDurability == 0 && !ItemStats.Every(item).Any(stat => stat.Stat == "item_indesctructible"))
                wrong.Add($"{name} [{code}]: no durability");
        }

        foreach (int row in ItemForge.EnabledUniques())
        {
            var forged = ItemForge.UniqueAt(row, Stamp, RollAs.Maximum).Item;
            Look(ItemPresenter.Describe(forged).Name, forged);
        }

        foreach (int row in ItemForge.SetItems())
        {
            var forged = ItemForge.SetItemAt(row, Stamp, RollAs.Maximum).Item;
            Look(ItemPresenter.Describe(forged).Name, forged);
        }

        foreach (string code in new[] { "jav", "7s7", "ama", "7ta", "7bk", "aqv", "cqv" })
            Look($"plain {code}", ItemForge.PlainBase(code, 0, Stamp).Item);

        Assert.True(stacked > 10, $"only {stacked} stackable items forged: the tables were not read");
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [GameFact]
    public void One_roll_moves_every_stat_its_property_writes()
    {
        if (!game.Available)
            return;

        // Titan's Revenge: Enhanced Damage is one roll on two stats.
        var titans = ItemForge.Unique("Titan's Revenge", Stamp, RollAs.Maximum).Item;
        Assert.Contains(AffixRanges.RolledTogether(titans),
            group => group.SetEquals(["item_maxdamage_percent", "item_mindamage_percent"]));

        // Veil of Steel: All Resistances is one number on four.
        var veil = ItemForge.Unique("Veil of Steel", Stamp, RollAs.Maximum).Item;
        Assert.Contains(AffixRanges.RolledTogether(veil),
            group => group.SetEquals(["fireresist", "lightresist", "coldresist", "poisonresist"]));

        // "Adds 1-45 lightning damage" is a minimum and a maximum, not one roll: in no group.
        Assert.DoesNotContain(AffixRanges.RolledTogether(veil).Concat(AffixRanges.RolledTogether(titans)),
            group => group.Contains("lightmindam") || group.Contains("firemindam") || group.Contains("coldmindam"));
    }

    [GameFact]
    public void Some_stats_are_never_written_apart()
    {
        if (!game.Available)
            return;

        var groups = AffixRanges.NeverApart();

        // Enhanced Damage: only dmg% writes either, and it writes both.
        Assert.Contains(groups, group => group.SetEquals(["item_maxdamage_percent", "item_mindamage_percent"]));

        // A flat damage's one-hand, two-hand and thrown numbers: dmg-throw could write the thrown one
        // alone, and nothing grants it.
        Assert.Contains(groups, group => group.SetEquals(["mindamage", "secondary_mindamage", "item_throw_mindamage"]));
        Assert.Contains(groups, group => group.SetEquals(["maxdamage", "secondary_maxdamage", "item_throw_maxdamage"]));

        // A map's paired lines.
        Assert.Contains(groups, group => group.Contains("map_mon_fasterattackrate") && group.Contains("map_mon_fastercastrate"));

        // Not a resistance, an attribute, an elemental damage's two ends, nor the per-level damage: each
        // has a property that writes it alone, or writes its partner a different number.
        foreach (string alone in new[] { "fireresist", "coldresist", "strength", "vitality", "firemindam", "firemaxdam",
                                          "coldmindam", "coldlength", "item_maxdamage_percent_perlevel" })
            Assert.DoesNotContain(groups, group => group.Contains(alone));
    }

    /// <summary>
    /// What goes on an item, and comes off it, with a stat (the property picker adds
    /// them together, the bin takes them together): its never-apart group, as far as the base holds it.
    /// </summary>
    [GameFact]
    public void A_stat_brings_the_ones_written_with_it_as_far_as_the_base_holds_them()
    {
        if (!game.Available)
            return;

        var helm = ItemForge.PlainBase("cap", 0, "103").Item;
        var pike = ItemForge.PlainBase("pik", 0, "103").Item;      // a Pike: two-handed
        var sword = ItemForge.PlainBase("ssd", 0, "103").Item;     // a Short Sword: one-handed
        var javelin = ItemForge.PlainBase("7s7", 0, "103").Item;   // a Balrog Spear: one-handed and thrown

        Assert.Equal(["item_mindamage_percent"], AffixRanges.WrittenWith(helm, "item_maxdamage_percent"));
        Assert.Equal(["item_maxdamage_percent"], AffixRanges.WrittenWith(pike, "item_mindamage_percent"));

        // No weapon: all three copies of a flat damage.
        Assert.Equal(["maxdamage", "secondary_maxdamage"], AffixRanges.WrittenWith(helm, "item_throw_maxdamage"));
        Assert.Equal(["secondary_mindamage", "item_throw_mindamage"], AffixRanges.WrittenWith(helm, "mindamage"));

        // A weapon: the kinds it is and no other.
        Assert.Empty(AffixRanges.WrittenWith(pike, "secondary_maxdamage"));
        Assert.Equal(["secondary_maxdamage"], AffixRanges.WrittenWith(pike, "maxdamage"));
        Assert.Empty(AffixRanges.WrittenWith(sword, "maxdamage"));
        Assert.Equal(["item_throw_maxdamage"], AffixRanges.WrittenWith(javelin, "maxdamage"));

        // Written alone: nothing with it.
        Assert.Empty(AffixRanges.WrittenWith(helm, "strength"));
    }

    /// <summary>
    /// A flat damage on armour is its three copies, as the game writes it on anything that is no weapon
    /// (measured on real circlets: `pd2spc damagekinds`). The forge builds the
    /// uniques that carry one so, and the trade import keeps the two-handed and thrown copies.
    /// </summary>
    [GameTheory]
    [InlineData("Wartraveler")]
    [InlineData("Bloodfist")]
    [InlineData("Razortail")]
    [InlineData("Duskdeep")]
    [InlineData("Spike Thorn")]
    [InlineData("Umbral Disk")]
    public void A_forged_armour_holds_every_copy_of_its_flat_damage(string unique)
    {
        if (!game.Available)
            return;

        var item = ItemForge.Unique(unique, "103").Item;
        var held = item.StatLists[0].Stats.Select(stat => stat.Stat ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase);

        int ends = 0;
        foreach (string[] copies in (string[][])
                 [
                     ["mindamage", "secondary_mindamage", "item_throw_mindamage"],
                     ["maxdamage", "secondary_maxdamage", "item_throw_maxdamage"],
                 ])
        {
            if (!copies.Any(held.Contains))
                continue;

            ends++;
            Assert.All(copies, copy => Assert.Contains(copy, held));
            Assert.Single(copies.Select(copy => item.StatLists[0].Stats.First(stat => copy.Equals(stat.Stat, StringComparison.OrdinalIgnoreCase)).Value).Distinct());
        }

        Assert.True(ends > 0, $"{unique} carries a flat damage");
    }

    /// <summary>
    /// Settling a flat damage (DamageKinds.Settle): what an item an older editor built is repaired with,
    /// and what every builder ends on. Armour with the one-handed copy alone gains the other two, a
    /// two-handed weapon holding all three keeps its own, a runeword's list keeps every copy - and the
    /// legitimacy check says so before, and nothing after.
    /// </summary>
    [GameFact]
    public void Settling_gives_an_item_the_copies_its_base_holds()
    {
        if (!game.Available)
            return;

        static ItemStat Stat(string name, int value) => new() { Id = StatCatalogue.IdOf(name), Stat = name, Value = value };
        static string Held(Item item, int list = 0) => string.Join(",", item.StatLists[list].Stats
            .Where(stat => DamageKinds.Ends.Any(copies => copies.Contains(stat.Stat ?? "")))
            .Select(stat => $"{stat.Stat}={stat.Value}"));

        // Boots as the trade import built them: War Traveler's 15-25, the one-handed copies alone.
        var boots = ItemForge.PlainBase("xtb", 0, "103").Item;
        ItemStats.Home(boots).Stats.AddRange([Stat("mindamage", 15), Stat("maxdamage", 25)]);
        Assert.Equal(2, DamageKinds.Apart(boots).Count);
        Assert.Contains(Legitimacy.Judge(boots), found => found.Said.Contains("Written for one-handed, where the game writes it"));
        Assert.Equal(4, DamageKinds.Settle(boots));
        Assert.Equal("mindamage=15,secondary_mindamage=15,item_throw_mindamage=15,maxdamage=25,secondary_maxdamage=25,item_throw_maxdamage=25",
                     string.Join(",", DamageKinds.Ends.SelectMany(copies => copies).Select(copy => $"{copy}={boots.StatLists[0].Stats.First(stat => stat.Stat == copy).Value}")));
        Assert.Empty(DamageKinds.Apart(boots));
        Assert.DoesNotContain(Legitimacy.Judge(boots), found => found.Said.Contains("Written for"));
        Assert.Equal(0, DamageKinds.Settle(boots));

        // A Pike holding all three: its two-handed copy stays, the others go.
        var pike = ItemForge.PlainBase("pik", 0, "103").Item;
        ItemStats.Home(pike).Stats.AddRange([Stat("maxdamage", 9), Stat("secondary_maxdamage", 9), Stat("item_throw_maxdamage", 9)]);
        Assert.Equal(2, DamageKinds.Settle(pike));
        Assert.Equal("secondary_maxdamage=9", Held(pike));

        // A runeword's own list keeps every copy, whatever the base; the item's own list is narrowed.
        var sword = ItemForge.PlainBase("ssd", 2, "103").Item;
        ItemStats.Home(sword).Stats.AddRange([Stat("maxdamage", 1), Stat("secondary_maxdamage", 1)]);
        sword.IsRuneword = true;
        sword.StatLists.Add(new ItemStatList { Stats = [Stat("mindamage", 3), Stat("secondary_mindamage", 3), Stat("item_throw_mindamage", 3)] });
        Assert.Equal(1, DamageKinds.Settle(sword));
        Assert.Equal("maxdamage=1", Held(sword));
        Assert.Equal("mindamage=3,secondary_mindamage=3,item_throw_mindamage=3", Held(sword, 1));

        // A thrown copy is held at what its field holds.
        var ring = ItemForge.PlainBase("rin", 0, "103").Item;
        ItemStats.Home(ring).Stats.Add(Stat("mindamage", 280));
        DamageKinds.Settle(ring);
        Assert.Equal(StatFields.RangeOf("item_throw_mindamage")!.Value.Max, ring.StatLists[0].Stats.First(stat => stat.Stat == "item_throw_mindamage").Value);
    }
}
