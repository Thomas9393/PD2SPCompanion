using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// What the socket die rolls for an item: the range of whatever gave, or would give, the item its sockets
/// (the die and the Puzzlebox button merged).
/// <list type="number">
/// <item><b>Its own</b>: a unique's or set piece's row, a socket affix (<see cref="AffixRanges.SocketSpan"/>) -
/// Blade of Ali Baba 2-3, Mechanist's 1-2, and a count named outright, which rolls to itself (Artificer's 3).</item>
/// <item><b>Its corruption</b>, where the corruption's outcome was sockets: the fewest to the most its outcomes
/// bore - 1-3 on armour, 2-4 on a one-hander, 3-6 on a two-hander or bow.</item>
/// <item><b>The Puzzlebox</b> on everything else: 2-4 on a two-hander or bow, 1-2 on the rest
/// (<see cref="Puzzlebox.RangeFor"/>).</item>
/// </list>
/// Every range is held to what the base takes at its item level (<see cref="ItemBases.MaximumSockets"/>), and
/// never under one. <see cref="Whole"/> is the other roll, one to the most the base takes: what the cube's socket
/// recipes roll on a normal or superior base (<c>cubemain.txt</c>: helm 1-3, armour and shield 1-4, weapon 1-6).
/// <para>
/// The game rolls each of these once and never again - its Puzzlebox takes only an item with no sockets, its
/// Demonic Cube leaves the count (fifty rerolls). Rolling again is the editor's own.
/// </para>
/// </summary>
public static class SocketRolls
{
    /// <summary>Where a range comes from.</summary>
    public enum Source
    {
        Own,
        Corruption,
        Puzzlebox,
    }

    /// <summary>A range the die rolls in, both ends counted.</summary>
    public sealed record Range(int Least, int Most, Source From);

    /// <summary>The die's range for this item, or null for one that takes no sockets or that no source names.</summary>
    public static Range? For(Item item)
    {
        int room = ItemBases.MaximumSockets(item);
        if (room <= 0)
            return null;

        Range Held(int least, int most, Source from) =>
            new(Math.Clamp(Math.Min(least, most), 1, room), Math.Clamp(Math.Max(least, most), 1, room), from);

        if (AffixRanges.SocketSpan(item) is { } own)
            return Held(own.Least, own.Most, Source.Own);

        if (Corruptions.IdOn(item) == Corruptions.Sockets)
        {
            var holes = Corruptions.Available(item, asIfClean: true)
                .Where(option => option.GivesSockets && option.Holes.Most > 0)
                .Select(option => option.Holes)
                .ToList();

            if (holes.Count > 0)
                return Held(holes.Min(each => Math.Min(each.Least, each.Most)), holes.Max(each => each.Most), Source.Corruption);
        }

        return Puzzlebox.RangeFor(item) is { } box ? Held(box.Least, box.Most, Source.Puzzlebox) : null;
    }

    /// <summary>
    /// One roll inside <paramref name="range"/>, by the game's own odds. A unique's range, an affix's and the Puzzlebox's are an even roll in the game. A corruption's is
    /// not: each count is an outcome with its own band - armor one, two or three sockets at 12, 7 and 6 in a hundred -
    /// so the outcome is drawn by its width first, then its count inside it (a plain base's row gives one to six,
    /// evenly), and held to the range.
    /// </summary>
    public static int Roll(Item item, Range range, Random random)
    {
        if (range.From == Source.Corruption)
        {
            var outcomes = Corruptions.Available(item, asIfClean: true)
                .Where(option => option.GivesSockets && option.Holes.Most > 0)
                .ToList();

            int space = outcomes.Sum(option => Math.Max(1, option.Width));
            int at = space > 0 ? random.Next(space) : -1;
            foreach (var option in outcomes)
            {
                at -= Math.Max(1, option.Width);
                if (at >= 0)
                    continue;

                int least = Math.Min(option.Holes.Least, option.Holes.Most), most = Math.Max(option.Holes.Least, option.Holes.Most);
                return Math.Clamp(random.Next(least, most + 1), range.Least, range.Most);
            }
        }

        return random.Next(range.Least, range.Most + 1);
    }

    /// <summary>One to the most the base takes at its item level, or null for a base that takes none.</summary>
    public static (int Least, int Most)? Whole(Item item) =>
        ItemBases.MaximumSockets(item) is > 0 and var room ? (1, room) : null;
}
