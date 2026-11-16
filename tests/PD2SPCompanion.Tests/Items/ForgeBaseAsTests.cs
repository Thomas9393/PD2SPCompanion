using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// A base made in a quality with nothing rolled onto it (ItemForge.BaseAs), for the Forge page:
/// which qualities a base can be, and that every one made is an item the file format
/// writes and reads back as it was made.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class ForgeBaseAsTests(Game game)
{
    private const string Stamp = "103";

    [GameFact]
    public void A_base_is_offered_the_qualities_its_type_allows()
    {
        if (!game.Available)
            return;

        // A pike is anything; a ring is never Normal and never Superior; a charm is magic alone.
        var pike = ItemForge.QualitiesOf("pik");
        Assert.Contains(ItemQuality.Normal, pike);
        Assert.Contains(ItemQuality.Superior, pike);
        Assert.Contains(ItemQuality.Magic, pike);
        Assert.Contains(ItemQuality.Rare, pike);

        var ring = ItemForge.QualitiesOf("rin");
        Assert.DoesNotContain(ItemQuality.Normal, ring);
        Assert.DoesNotContain(ItemQuality.Superior, ring);
        Assert.Contains(ItemQuality.Magic, ring);
        Assert.Contains(ItemQuality.Rare, ring);

        var charm = ItemForge.QualitiesOf("cm1");
        Assert.Equal([ItemQuality.Magic], charm);

        // Not a rune, not a quest item.
        Assert.True(ItemForge.Makes("pik"));
        Assert.True(ItemForge.Makes("rin"));
        Assert.False(ItemForge.Makes("r01"));
        Assert.False(ItemForge.Makes("hdm"));
    }

    [GameFact]
    [Trait("Speed", "Slow")]
    public void Every_base_in_every_quality_it_allows_writes_and_reads_back()
    {
        if (!game.Available)
            return;

        var wrong = new List<string>();
        int made = 0;
        var random = new Random(7);

        foreach (string code in ItemBases.Codes.Where(ItemForge.Makes))
        {
            foreach (var quality in ItemForge.QualitiesOf(code))
            {
                try
                {
                    var item = ItemForge.BaseAs(code, quality, Stamp, random).Item;
                    made++;

                    if (item.Quality != quality)
                        wrong.Add($"{code} asked {quality}, made {item.Quality}");

                    byte[] first = Item.Write(item, 0x60);
                    var back = Item.Read(first, 0x60);
                    if (!first.AsSpan().SequenceEqual(Item.Write(back, 0x60)))
                        wrong.Add($"{code} {quality}: read back another item");

                    if (quality is ItemQuality.Rare or ItemQuality.Craft && (back.RarePrefixId == 0 || back.RareSuffixId == 0))
                        wrong.Add($"{code} {quality}: no name");
                }
                catch (Exception problem)
                {
                    wrong.Add($"{code} {quality}: {problem.Message}");
                }
            }
        }

        Assert.True(made > 500, $"only {made} made: the tables were not read");
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong.Take(40)));
    }
}
