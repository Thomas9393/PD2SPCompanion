using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// A clean on a set piece: its mods sit on its own list, its green lists are the set's. So a clean answers its own list
/// from its own sources, and leaves no line nothing grants.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class SetPieceCleanTests(Game game)
{
    private const string Stamp = "105";

    private static IEnumerable<ItemStat> Green(Item piece) => piece.StatLists.Skip(1).SelectMany(list => list.Stats);

    /// <summary>The set pieces with a one-line corruption on a stat of their green lists, as <paramref name="ownToo"/> asks.</summary>
    private static IEnumerable<(Item Piece, Corruptions.CorruptionOption Option, string Stat)> Meeting(bool ownToo)
    {
        foreach (int row in ItemForge.SetItems())
        {
            var piece = ItemForge.SetItemAt(row, Stamp, RollAs.Maximum).Item;
            var green = Green(piece).Select(stat => stat.Stat).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var own = piece.StatLists[0].Stats.Select(stat => stat.Stat).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var option = Corruptions.Available(piece).FirstOrDefault(each => !each.GivesSockets && each.Grants.Count == 1
                && each.Grants[0].Param == 0 && AffixRanges.StatsWritten(each.Grants[0].Code) is { Count: 1 } written
                && green.Contains(written[0]) && own.Contains(written[0]) == ownToo);
            if (option is not null)
                yield return (piece, option, AffixRanges.StatsWritten(option.Grants[0].Code)[0]);
        }
    }

    [GameFact]
    public void A_corruptions_line_on_a_set_piece_goes_with_the_clean_and_the_green_line_stays()
    {
        if (!game.Available)
            return;

        int looked = 0;
        foreach (var (piece, option, stat) in Meeting(ownToo: false))
        {
            int green = Green(piece).Where(each => each.Stat == stat).Sum(each => each.Value);

            Corruptions.Apply(piece, option, new Random(3));
            Assert.Contains(piece.StatLists[0].Stats, each => each.Stat == stat);

            Corruptions.Clean(piece, new Random(3));

            Assert.DoesNotContain(piece.StatLists[0].Stats, each => each.Stat == stat);
            Assert.Equal(green, Green(piece).Where(each => each.Stat == stat).Sum(each => each.Value));
            looked++;
        }

        Assert.True(looked > 0, "no set piece with a corruption that meets only its green list");
    }

    [GameFact]
    public void A_set_pieces_own_line_comes_back_inside_its_own_range_and_the_green_line_stays()
    {
        if (!game.Available)
            return;

        int looked = 0;
        foreach (var (piece, option, stat) in Meeting(ownToo: true))
        {
            int green = Green(piece).Where(each => each.Stat == stat).Sum(each => each.Value);
            var range = AffixRanges.OwnOf(piece)[AffixRanges.KeyOf(piece.StatLists[0].Stats.First(each => each.Stat == stat))];

            Corruptions.Apply(piece, option, new Random(3));
            Corruptions.Clean(piece, new Random(3));

            int own = piece.StatLists[0].Stats.Where(each => each.Stat == stat).Sum(each => each.Value);
            Assert.InRange(own, range.Min, range.Max);
            Assert.Equal(green, Green(piece).Where(each => each.Stat == stat).Sum(each => each.Value));
            looked++;
        }

        Assert.True(looked > 0, "no set piece with a corruption that meets both its own list and its green list");
    }
}
