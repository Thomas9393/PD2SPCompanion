using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// Every affix the game offers, put on and taken off, swapped for another: the item ends as it would have been without
/// it - nothing it wrote left behind, nothing of the item's own lost ("of Ages" left its
/// Indestructible on a staff, so every other stat is checked for not
/// clearing properly). One base of each kind, at item level 99, where every tier is offered.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class AffixSweepTests(Game game)
{
    /// <summary>One base of each kind the affix tables tell apart: jewellery, charms, jewel, armour by slot, weapons by class, the class items.</summary>
    internal static readonly string[] Bases =
    [
        "rin", "amu", "jew", "cm1", "cm2", "cm3", "ci0",
        "cap", "qui", "buc", "lgl", "lbt", "lbl",
        "ssd", "hax", "mac", "spr", "pax", "sbw", "lxb", "jav", "tkf", "scp", "wnd", "6bs", "ob1",
        "am1", "dr1", "ba1", "pa1", "ne1", "ktr",
    ];

    /// <summary>
    /// The base as the Forge makes it, with nothing rolled on it - made normal, then called magic with its affix slots
    /// empty: made magic, its rolled affixes' lines would stay with their ids cleared, lines no source writes.
    /// </summary>
    internal static Item Fresh(string code)
    {
        var item = ItemForge.BaseAs(code, ItemQuality.Normal, "103", new Random(7)).Item;
        item.ItemLevel = 99;
        item.Quality = ItemQuality.Magic;
        item.MagicPrefixIds = new ushort[3];
        item.MagicSuffixIds = new ushort[3];
        return item;
    }

    /// <summary>What the item holds, line by line, as its stats are keyed: the thing an affix must leave as it was.</summary>
    internal static List<string> Lines(Item item) =>
        [.. item.StatLists.SelectMany(list => list.Stats).Select(stat => $"{AffixRanges.KeyOf(stat)}={stat.Value}").Order(StringComparer.Ordinal)];

    /// <summary>The same, without the numbers - a swap rolls the new affix again.</summary>
    internal static List<string> Keys(Item item) =>
        [.. item.StatLists.SelectMany(list => list.Stats).Select(AffixRanges.KeyOf).Order(StringComparer.Ordinal)];

    private static void Put(Item item, bool prefix, MagicAffixes.AffixOption? option) =>
        MagicAffixes.Apply(item,
            new Dictionary<(int, bool), MagicAffixes.AffixPick>
            {
                [(0, prefix)] = option is null
                    ? new(0, new Dictionary<string, int>())
                    : new(option.Id, MagicAffixes.Roll(option, new Random(option.Id))),
            },
            new Random(1));

    [GameFact]
    [Trait("Speed", "Slow")]
    public void Every_affix_taken_off_or_swapped_leaves_the_item_as_it_would_be_without_it()
    {
        if (!game.Available)
            return;

        var failed = new List<string>();
        int tried = 0;

        foreach (string code in Bases)
        {
            foreach (bool prefix in new[] { true, false })
            {
                var offered = MagicAffixes.Available(Fresh(code), prefix, 0).Where(option => !option.NeedsParam).ToList();
                for (int n = 0; n < offered.Count; n++)
                {
                    var option = offered[n];
                    string side = prefix ? "prefix" : "suffix";
                    tried++;

                    // On and off: the base as it was.
                    var item = Fresh(code);
                    var before = Lines(item);
                    Put(item, prefix, option);
                    Put(item, prefix, null);
                    var after = Lines(item);
                    if (!after.SequenceEqual(before))
                    {
                        failed.Add($"{code} {side} {option.Id} {option.Name}: off left [{string.Join(", ", after.Except(before))}]"
                                   + $" lost [{string.Join(", ", before.Except(after))}]");
                    }

                    // Swapped for the next one offered: as a fresh item with that one.
                    var other = offered[(n + 1) % offered.Count];
                    if (other.Id == option.Id)
                        continue;

                    var swapped = Fresh(code);
                    Put(swapped, prefix, option);
                    Put(swapped, prefix, other);
                    var straight = Fresh(code);
                    Put(straight, prefix, other);
                    if (!Keys(swapped).SequenceEqual(Keys(straight)))
                    {
                        failed.Add($"{code} {side} {option.Id} {option.Name} -> {other.Id} {other.Name}: kept"
                                   + $" [{string.Join(", ", Keys(swapped).Except(Keys(straight)))}] missing [{string.Join(", ", Keys(straight).Except(Keys(swapped)))}]");
                    }
                }
            }
        }

        Assert.True(tried > 1000, $"only {tried} affixes tried");
        Assert.True(failed.Count == 0, $"{failed.Count} of {tried}:{Environment.NewLine}{string.Join(Environment.NewLine, failed.Take(60))}");
    }

    /// <summary>
    /// The automod's rows and the superior bonus's, swapped one for another and taken off, the same way: as a fresh base
    /// with the one put on, or with none. Every base of the sweep, every row it can have.
    /// </summary>
    [GameFact]
    [Trait("Speed", "Slow")]
    public void Every_automod_and_superior_row_swapped_or_taken_off_leaves_the_item_as_it_would_be_without_it()
    {
        if (!game.Available)
            return;

        var failed = new List<string>();
        int tried = 0;

        foreach (string code in Bases)
        {
            // The automod, where the base has a group of rows to choose from.
            var rows = AffixRanges.AutomagicRowsFor(Fresh(code));
            for (int n = 0; n < rows.Count && rows.Count > 1; n++)
            {
                var (row, next) = (rows[n], rows[(n + 1) % rows.Count]);
                tried++;

                var swapped = Fresh(code);
                AffixRanges.PutAutomagic(swapped, row.Id);
                AffixRanges.PutAutomagic(swapped, next.Id);
                var straight = Fresh(code);
                AffixRanges.PutAutomagic(straight, next.Id);
                if (!Keys(swapped).SequenceEqual(Keys(straight)))
                    failed.Add($"{code} automod {row.Id} -> {next.Id}: kept [{string.Join(", ", Keys(swapped).Except(Keys(straight)))}] missing [{string.Join(", ", Keys(straight).Except(Keys(swapped)))}]");

                var off = Fresh(code);
                AffixRanges.PutAutomagic(off, 0);
                var bare = Keys(off);
                AffixRanges.PutAutomagic(off, row.Id);
                AffixRanges.PutAutomagic(off, 0);
                if (!Keys(off).SequenceEqual(bare))
                    failed.Add($"{code} automod {row.Id} off: left [{string.Join(", ", Keys(off).Except(bare))}]");
            }

            // The superior bonus, on the base made superior.
            Item Superior()
            {
                var item = Fresh(code);
                item.Quality = ItemQuality.Superior;
                return item;
            }

            var bonuses = QualityItems.RowsFor(Superior());
            for (int n = 0; n < bonuses.Count; n++)
            {
                var (bonus, next) = (bonuses[n], bonuses[(n + 1) % bonuses.Count]);
                tried++;

                var none = Superior();
                var before = Keys(none);
                QualityItems.Put(none, bonus);
                QualityItems.Put(none, null);
                if (!Keys(none).SequenceEqual(before))
                    failed.Add($"{code} superior {bonus.Row} off: left [{string.Join(", ", Keys(none).Except(before))}] lost [{string.Join(", ", before.Except(Keys(none)))}]");

                if (next.Row == bonus.Row)
                    continue;

                var swapped = Superior();
                QualityItems.Put(swapped, bonus);
                QualityItems.Put(swapped, next);
                var straight = Superior();
                QualityItems.Put(straight, next);
                if (!Keys(swapped).SequenceEqual(Keys(straight)))
                    failed.Add($"{code} superior {bonus.Row} -> {next.Row}: kept [{string.Join(", ", Keys(swapped).Except(Keys(straight)))}] missing [{string.Join(", ", Keys(straight).Except(Keys(swapped)))}]");
            }
        }

        Assert.True(tried > 50, $"only {tried} rows tried");
        Assert.True(failed.Count == 0, $"{failed.Count} of {tried}:{Environment.NewLine}{string.Join(Environment.NewLine, failed.Take(60))}");
    }
}
