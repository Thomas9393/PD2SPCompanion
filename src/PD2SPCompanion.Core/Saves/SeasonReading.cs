using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Saves;

/// <summary>
/// Which season's encoding a file is in, when it reads cleanly both ways.
/// <para>
/// PD2's tables carry the previous season's encoding beside the current one, so a file can be read as
/// this season's or as last season's. Most files only round-trip one way. But a stat whose field
/// kept its width and only moved its offset reads cleanly both ways, with different values: a Grand
/// Charm of Vita's +40 life reads as -28 under the wrong encoding (measured: 158 of 1395
/// corpus items, written in last season's encoding, also round-trip under this season's). A file of
/// only such items - a stash page of charms - would be taken as this season's and shown wrong.
/// </para>
/// <para>
/// So where both readings round-trip and differ, the file's own evidence decides, in order:
/// <list type="number">
/// <item><b>Its items' version stamp</b>, against the stamps the two seasons write
/// (<see cref="SetStamps"/>, kept by the season snapshots).</item>
/// <item><b>Plausibility</b>: the reading the legitimacy check finds less wrong with. A misread puts a
/// value outside every range that could roll it - the charm's -28 life where it rolls 36-40 - and so
/// draws a finding; measured on the 158 corpus items that read both ways, every misread drew one and
/// no true reading did, with no ties.</item>
/// </list>
/// Where neither decides, the file is refused rather than guessed - the fail-safe rule.
/// </para>
/// </summary>
public static class SeasonReading
{
    /// <summary>The item version stamp the installed season writes, once known.</summary>
    public static string? CurrentStamp { get; private set; }

    /// <summary>The stamp the season before it wrote, once known.</summary>
    public static string? PreviousStamp { get; private set; }

    /// <summary>Set by whoever keeps the seasons' records - the season snapshots.</summary>
    public static void SetStamps(string? current, string? previous)
    {
        CurrentStamp = current;
        PreviousStamp = previous;

        // The parser reads each item by its own stamp once both are known (Globals.ItemEra).
        D2SLib.Globals.current_season_stamp = current;
        D2SLib.Globals.previous_season_stamp = previous;
    }

    /// <summary>Whether the installed tables carry a previous season's encoding to read with at all.</summary>
    public static bool HasPreviousSeason => D2SLib.Model.TXT.StatEncoding.PreviousSeason.Length > 0;

    /// <summary>
    /// Whether a file whose items read cleanly both ways is last season's. Same values both ways: this
    /// season's, since nothing differs. Refused, with the reason, where its evidence does not decide.
    /// </summary>
    public static bool IsLastSeason(string name, IReadOnlyList<Item> asCurrent, IReadOnlyList<Item> asLast)
    {
        if (Values(asCurrent) == Values(asLast))
            return false;

        var stamps = asCurrent.Select(item => item.Version).Where(stamp => stamp is { Length: > 0 }).Distinct().ToList();
        if (stamps.Count == 1 && CurrentStamp is { } current && PreviousStamp is { } previous && current != previous)
        {
            if (stamps[0] == previous)
                return true;
            if (stamps[0] == current)
                return false;
        }

        int wrongNow = Findings(asCurrent), wrongLast = Findings(asLast);
        if (wrongNow != wrongLast)
            return wrongLast < wrongNow;

        throw new InvalidDataException(
            L($"'{name}' left unread: reads as this season and last, with different values."));
    }

    /// <summary>Every item's every property, with what is socketed in it: what the two readings are compared by.</summary>
    private static string Values(IReadOnlyList<Item> items) =>
        string.Join("|", items.Where(item => !item.IsCompact).WithSockets()
            .Select(item => string.Join(";", Items.ItemStats.Every(item).Select(stat => $"{stat.Stat}={stat.Value}/{stat.Param}"))));

    /// <summary>
    /// What the legitimacy rules find wrong with the items, of any certainty, under the installed
    /// tables. Not only "impossible": a misread value is mostly "unrolled" - outside its ranges.
    /// The rules themselves (<see cref="Items.Legitimacy.Judge"/>), not the check: read as last
    /// season's, every item is a legacy one, which the check leaves be - and that reading would win.
    /// </summary>
    private static int Findings(IReadOnlyList<Item> items) =>
        items.Where(item => !item.IsCompact).Sum(item => Items.Legitimacy.Judge(item).Count);
}
