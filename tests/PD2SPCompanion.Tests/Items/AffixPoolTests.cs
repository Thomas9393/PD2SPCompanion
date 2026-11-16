using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// Which affixes an item is offered, against the tables themselves - some affixes are
/// magic only, and not to be picked for a rare or a craft. <c>magicprefix.txt</c> and
/// <c>magicsuffix.txt</c> mark each row <c>rare</c> 1 or not: a rare item rolls only the marked ones, and a
/// crafted item's affixes are a rare's. Read here from the raw rows, not through the editor's own loader, so
/// the two are compared and not one with itself.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class AffixPoolTests(Game game)
{
    private static Item Blank(string code, ItemQuality quality) => new()
    {
        Code = code.PadRight(4, ' '),
        Quality = quality,
        ItemLevel = 99,
        MagicPrefixIds = new ushort[3],
        MagicSuffixIds = new ushort[3],
    };

    /// <summary>The names of the spawnable rows of one table, split by its rare mark.</summary>
    private static (HashSet<string> ForRare, HashSet<string> MagicOnly) Marked(GameData data, string table)
    {
        var rows = data.Excel(table);
        int name = rows.IndexOfColumn("Name"), rare = rows.IndexOfColumn("rare"),
            spawnable = rows.IndexOfColumn("spawnable"), frequency = rows.IndexOfColumn("frequency");
        var marked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var magicOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.Rows)
        {
            string Cell(int at) => at >= 0 && at < row.Length ? row[at].Trim() : "";
            if (Cell(name).Length == 0 || Cell(spawnable) != "1" || !int.TryParse(Cell(frequency), out int weight) || weight <= 0)
                continue;
            (Cell(rare) == "1" ? marked : magicOnly).Add(Cell(name));
        }

        // A name with one tier marked and another not is neither here: only the clear cases are compared.
        var both = marked.Intersect(magicOnly, StringComparer.OrdinalIgnoreCase).ToList();
        marked.ExceptWith(both);
        magicOnly.ExceptWith(both);
        return (marked, magicOnly);
    }

    [Trait("Season", "S13")]
    [GameTheory]
    [InlineData("rin")]
    [InlineData("amu")]
    [InlineData("uap")]   // Shako: a helm
    [InlineData("7s7")]   // Balrog Spear
    [InlineData("jew")]
    public void A_rare_or_crafted_item_is_offered_no_magic_only_affix(string code)
    {
        if (!game.Pinned("S13"))
            return;

        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        foreach (var (prefix, table) in new[] { (true, "magicprefix"), (false, "magicsuffix") })
        {
            var (_, magicOnly) = Marked(data, table);
            Assert.NotEmpty(magicOnly);

            // Keyed by the table's own name for the row (the key before the string table words it).
            var magic = MagicAffixes.Available(Blank(code, ItemQuality.Magic), prefix);
            var rare = MagicAffixes.Available(Blank(code, ItemQuality.Rare), prefix);
            var craft = MagicAffixes.Available(Blank(code, ItemQuality.Craft), prefix);

            Assert.All(rare, row => Assert.True(row.ForRare, $"{code}: a rare is offered {row.Name} ({row.Id}), which {table} does not mark rare"));
            Assert.All(craft, row => Assert.True(row.ForRare, $"{code}: a crafted item is offered {row.Name} ({row.Id}), which {table} does not mark rare"));

            // And the mark is the table's: what a magic item is offered beyond a rare is exactly the rows not marked.
            var beyond = magic.Where(row => !rare.Any(each => each.Id == row.Id)).ToList();
            Assert.All(beyond, row => Assert.False(row.ForRare, $"{code}: {row.Name} ({row.Id}) is marked rare and a rare is not offered it"));
            Assert.Equal(magic.Count(row => row.ForRare), rare.Count);
        }
    }

    [GameFact]
    public void The_rare_mark_is_read_as_the_table_has_it()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        foreach (var (prefix, table) in new[] { (true, "magicprefix"), (false, "magicsuffix") })
        {
            var rows = data.Excel(table);
            int rare = rows.IndexOfColumn("rare"), spawnable = rows.IndexOfColumn("spawnable"), frequency = rows.IndexOfColumn("frequency");
            int marked = rows.Rows.Count(row => row.Length > rare && row[rare].Trim() == "1"
                                                && row[spawnable].Trim() == "1" && int.TryParse(row[frequency].Trim(), out int weight) && weight > 0);

            // Every affix the editor holds, asked with no type filter at all.
            var all = MagicAffixes.Available(Blank("rin", ItemQuality.Magic), prefix, narrowTo: "", outgrown: true);
            Assert.Equal(marked, all.Count(row => row.ForRare));
            Assert.True(all.Count(row => !row.ForRare) > 0, $"{table}: no magic-only affix was read");
        }
    }
}
