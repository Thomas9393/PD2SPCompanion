using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// The runeword builder: which words a base can hold, a word's properties rolled, and the base
/// made into it. The item panel's Zod button.
/// </summary>
public static class RunewordBuilder
{
    /// <summary>A word chosen and its properties rolled, waiting on the tick - as a craft's draft waits.</summary>
    public sealed record Draft(Runewords.Word Word, IReadOnlyList<(Runewords.RunewordMod Mod, IReadOnlyList<AffixRanges.Wrote> Values)> Rolled)
    {
        public string Name => NameOf(Word);

        public static string NameOf(Runewords.Word word) => Runewords.DisplayName(word);

        /// <summary>What it grants, in the game's own words.</summary>
        public IReadOnlyList<string> Lines => Crafts.Describe(Rolled.SelectMany(each => each.Values));
    }

    /// <summary>
    /// Whether a base can take a runeword at all: normal or superior, with sockets - every one of
    /// them empty, or holding a runeword's runes, which the builder cleans off to make another
    /// (the item cleaned back to white, or just swapped to another runeword).
    /// Which words fit is <see cref="WordsFor"/>.
    /// </summary>
    public static bool CanHold(Item item) =>
        !item.IsCompact
        && item.Quality is ItemQuality.Normal or ItemQuality.Superior
        && item.TotalNumberOfSockets > 0
        && (item.IsRuneword || item.SocketedItems.Count == 0);

    /// <summary>
    /// Makes a runeword its white base again: the runes out of its sockets and gone, the word's
    /// number and its stat list with them. What the base had stays - its own list with a
    /// superior's bonuses, the sockets, ethereal, item level.
    /// </summary>
    public static void Clean(Item item)
    {
        item.IsRuneword = false;
        item.RunewordId = 0;
        item.SocketedItems.Clear();
        item.NumberOfSocketedItems = 0;

        while (item.StatLists.Count > 1)
            item.StatLists.RemoveAt(1);
    }

    /// <summary>
    /// The words this base can be made into, in the table's order: as many runes as it has
    /// sockets, a type the word takes and none it excludes.
    /// </summary>
    public static IReadOnlyList<Runewords.Word> WordsFor(Item item)
    {
        if (!CanHold(item))
            return [];

        var lineage = ItemTypes.LineageOfBase(item.Code);

        return [.. Runewords.All.Where(word =>
            word.Runes.Length == item.TotalNumberOfSockets
            && word.Types.Any(lineage.Contains)
            && !word.ExcludedTypes.Any(lineage.Contains))];
    }

    /// <summary>
    /// Rolls every property of the word inside its range. A packed property - a chance to cast,
    /// charges - is its two numbers as the row writes them, not a range, so it is not rolled.
    /// </summary>
    public static Draft Roll(Runewords.Word word, Random random)
    {
        var rolled = new List<(Runewords.RunewordMod, IReadOnlyList<AffixRanges.Wrote>)>();

        foreach (var mod in word.Mods)
        {
            var (min, max) = AffixRanges.Ordered(mod.Code, mod.Min, mod.Max);
            int value = AffixRanges.IsPackedCode(mod.Code) || max <= min ? max : random.Next(min, max + 1);
            rolled.Add((mod, AffixRanges.Written(mod.Code, mod.Min, mod.Max, value, mod.Param)));
        }

        return new Draft(word, rolled);
    }
}
