using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// Smart sort's rules written as PD2's loot filter writes its conditions, so that
/// no one need learn a different system from the game's: ItemDisplay[...]: Folder,
/// the filter's keywords, codes and operators, the first rule that fits winning; every line wrong said by its number -
/// what was let through in silence before (a type that does not exist) too; the old file converted to rules that sort
/// as it did.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class SmartSortFilterTests(Game game)
{
    private static Item Unique(string code) =>
        ItemForge.UniqueAt(ItemForge.EnabledUniques().First(row => ItemForge.UniqueAt(row, "103").Item.Code.Trim() == code), "103").Item;

    private static string Sorted(Item item, string rules) => string.Join("/", SmartSort.Classify(item, SmartSort.Parse(rules)));

    [GameFact]
    public void Every_line_wrong_is_said_by_its_number_and_the_rest_still_sort()
    {
        if (!game.Available)
            return;

        var rules = SmartSort.Parse("""
            // A comment, and the lines after it each wrong in its own way.
            rin UNI -> Rings
            ItemDisplay[amulett]: Amulets
            ItemDisplay[GROUND rin]: Rings
            ItemDisplay[(rin OR amu]: Jewelry
            ItemDisplay[SOCK]: Socketed
            ItemDisplay[ILVL~80]: High
            ItemDisplay[FOO>1]: Foo
            ItemDisplay[CHARM]: Charms/%COLOUR%
            ItemDisplay[rin]: Rings
            """);

        Assert.Equal(
        [
            "line 2: a rule is written ItemDisplay[conditions]: Folder/Subfolder.",
            "line 3: no condition or base code is called amulett.",
            "line 4: GROUND means nothing in the vault.",
            "line 5: a ( with no ).",
            "line 6: SOCK is a number: compare it, as SOCK>0.",
            "line 7: ILVL~80: a range is written ILVL~low-high.",
            "line 8: no value is called FOO.",
            "line 9: no folder token is called %COLOUR%.",
        ], rules.Problems);

        Assert.Single(rules.All);
        Assert.Equal(["Rings"], SmartSort.Classify(Unique("rin"), rules));
    }

    [GameFact]
    public void Conditions_read_as_the_filter_reads_them()
    {
        if (!game.Available)
            return;

        var ring = Unique("rin");
        var magic = ItemForge.BaseAs("rin", ItemQuality.Magic, "103", new Random(1)).Item;

        // Side by side all must fit; OR either; ! not; brackets group.
        Assert.Equal("A", Sorted(ring, "ItemDisplay[rin UNI]: A\nItemDisplay[]: B"));
        Assert.Equal("B", Sorted(ring, "ItemDisplay[rin SET]: A\nItemDisplay[]: B"));
        Assert.Equal("A", Sorted(ring, "ItemDisplay[(SET OR UNI) JEWELRY !ETH]: A\nItemDisplay[]: B"));
        Assert.Equal("A", Sorted(ring, "ItemDisplay[rin AND UNI]: A\nItemDisplay[]: B"));
        Assert.Equal("B", Sorted(ring, "ItemDisplay[!(rin UNI)]: A\nItemDisplay[]: B"));

        // Numbers: compared, ranged, summed - and the first rule that fits wins.
        Assert.Equal("A", Sorted(ring, "ItemDisplay[ILVL~1-99 SOCK=0]: A\nItemDisplay[]: B"));
        Assert.Equal("A", Sorted(ring, "ItemDisplay[ILVL>0]: A\nItemDisplay[ILVL>0]: C"));
        Assert.Equal("B", Sorted(ring, "ItemDisplay[FRES+CRES+LRES+PRES>400]: A\nItemDisplay[]: B"));

        // Tokens fill the folder.
        Assert.Equal("Rings/Unique", Sorted(ring, "ItemDisplay[]: %SLOT%/%QUALITY%"));

        // CRAFTBASE is how the item is being stored: Shift + Store asks it true.
        var rules = SmartSort.Parse("ItemDisplay[CRAFTBASE]: Crafting Bases/%SLOT%\nItemDisplay[MAG]: Magic/%SLOT%");
        Assert.Equal(["Magic", "Rings"], SmartSort.Classify(magic, rules));
        Assert.Equal(["Crafting Bases", "Rings"], SmartSort.ClassifyAsCraftingBase(magic, rules));
    }

    /// <summary>
    /// A skiller under its class and then its skill tree,
    /// by the tree's name as the game prints it; and a rules file that holds the default before that,
    /// untouched, takes the new one - an edited one does not.
    /// </summary>
    [GameFact]
    public void A_skiller_goes_under_its_skill_tree_and_an_untouched_earlier_default_follows_the_new_one()
    {
        if (!game.Available)
            return;

        var charm = ItemForge.BaseAs("cm3", ItemQuality.Magic, "103", new Random(1)).Item;
        charm.StatLists[0].Stats.RemoveAll(stat => stat.Stat == "item_addskill_tab");
        charm.StatLists[0].Stats.Add(new ItemStat { Id = 188, Stat = "item_addskill_tab", CharacterClass = 0, SkillTab = 1, Value = 1 });

        string tree = SkillTabs.Name(0, 1)!;
        Assert.False(string.IsNullOrEmpty(tree));
        Assert.Equal(["Charms", "Skillers", "Amazon", tree], SmartSort.Classify(charm, SmartSort.Parse(SmartSort.DefaultText)));

        const string was = "ItemDisplay[CHARM STAT188>0]: Charms/Skillers/%CLASS%";
        const string now = "ItemDisplay[CHARM STAT188>0]: Charms/Skillers/%CLASS%/%SKILLTAB%";
        string earlier = SmartSort.DefaultText.Replace(now, was);
        Assert.NotEqual(SmartSort.DefaultText, earlier);
        Assert.True(SmartSort.IsEarlierDefault(earlier));
        Assert.True(SmartSort.IsEarlierDefault("// comments of its own" + "\n" + earlier.Replace("        ", " ")));
        Assert.False(SmartSort.IsEarlierDefault(SmartSort.DefaultText));
        Assert.False(SmartSort.IsEarlierDefault(earlier.Replace("Runewords/Helms", "Runewords/Hats")));
    }

    /// <summary>
    /// SK and OS asked of the skill the stat holds as its param (encode 1): asked of SkillId, which those two never
    /// carry, no rule fit.
    /// </summary>
    [GameFact]
    public void A_single_skill_and_an_oskill_fit_by_their_skill()
    {
        if (!game.Available)
            return;

        var ring = Unique("rin");
        ring.StatLists[0].Stats.Add(new ItemStat { Id = 107, Stat = "item_singleskill", Param = 44, Value = 2 });
        ring.StatLists[0].Stats.Add(new ItemStat { Id = 97, Stat = "item_nonclassskill", Param = 221, Value = 3 });

        Assert.Equal("A", Sorted(ring, "ItemDisplay[SK44>1]: A\nItemDisplay[]: B"));
        Assert.Equal("B", Sorted(ring, "ItemDisplay[SK45>0]: A\nItemDisplay[]: B"));
        Assert.Equal("A", Sorted(ring, "ItemDisplay[OS221=3]: A\nItemDisplay[]: B"));
    }

    /// <summary>
    /// PREFIX and SUFFIX asked of every affix an item holds, as the filter asks them: a rare's
    /// second or third suffix fits SUFFIX= as its first does, and two such conditions side by side may both fit. Only
    /// the first was asked - Kryszard's "High ED Amp Proc Combo" missed 49 of 56 ladder weapons for it.
    /// </summary>
    [GameFact]
    public void Prefix_and_suffix_are_asked_of_every_affix_the_item_holds()
    {
        if (!game.Available)
            return;

        var rare = ItemForge.BaseAs("rin", ItemQuality.Rare, "103", new Random(1)).Item;
        rare.MagicSuffixIds = [100, 898, 0];
        rare.MagicPrefixIds = [0, 0, 617];

        Assert.Equal("A", Sorted(rare, "ItemDisplay[SUFFIX=898]: A\nItemDisplay[]: B"));
        Assert.Equal("A", Sorted(rare, "ItemDisplay[SUFFIX=100 SUFFIX=898]: A\nItemDisplay[]: B"));
        Assert.Equal("A", Sorted(rare, "ItemDisplay[SUFFIX~890-899]: A\nItemDisplay[]: B"));
        Assert.Equal("B", Sorted(rare, "ItemDisplay[SUFFIX=0]: A\nItemDisplay[]: B"));
        Assert.Equal("A", Sorted(rare, "ItemDisplay[PREFIX=617]: A\nItemDisplay[]: B"));
        Assert.Equal("B", Sorted(rare, "ItemDisplay[!(SUFFIX=899 OR PREFIX=617)]: A\nItemDisplay[]: B"));

        // None held: one 0, as before.
        rare.MagicPrefixIds = [0, 0, 0];
        Assert.Equal("A", Sorted(rare, "ItemDisplay[PREFIX=0]: A\nItemDisplay[]: B"));
    }

    [GameFact]
    public void The_old_default_converted_sorts_as_the_shipped_rules_do_and_what_has_no_filter_word_is_left_a_comment()
    {
        if (!game.Available)
            return;

        // The old default's rules, as a player's old file held them.
        const string old = """
            # an old comment
            [set groups]
            Common = Arctic Gear, Sigon's Complete Steel
            [sorted]
            set                    -> Sets/{set group}/{set}
            ring unique            -> Rings/Unique
            amulet unique          -> Amulets/Unique
            type:circ              -> Circlets
            unique weapon          -> Unique Weapons/{weapon}
            unique                 -> Other Uniques/{slot}
            *                      -> Unsorted
            [crafting bases]
            *                      -> Crafting Bases/{slot}
            """;

        string converted = SmartSort.Convert(old);
        var rules = SmartSort.Parse(converted);
        Assert.Empty(rules.Problems);
        Assert.Contains("// an old comment", converted);
        Assert.Contains("SetGroup[Common]: Arctic Gear, Sigon's Complete Steel", converted);
        Assert.Contains("ItemDisplay[CRAFTBASE]: Crafting Bases/%SLOT%", converted);
        Assert.Contains("// not converted, the filter has no word for type:circ: type:circ              -> Circlets", converted);

        var shipped = SmartSort.Parse(SmartSort.DefaultText);
        foreach (string code in new[] { "rin", "amu" })
            Assert.Equal(SmartSort.Classify(Unique(code), shipped), SmartSort.Classify(Unique(code), rules));
    }
}
