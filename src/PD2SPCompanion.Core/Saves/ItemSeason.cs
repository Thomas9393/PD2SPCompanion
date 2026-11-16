using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Saves;

/// <summary>
/// An item of the season before, kept as that season made it: a <b>legacy</b> item, deliberately not migrated, marked legacy,
/// and no longer checked for legality. The game
/// never rewrites one - it reads each item by its own stamp, the season before's through the previous-season columns, and
/// mixes the two in one file - so the editor does not either: no migration changes it, the legitimacy check leaves it be
/// (its lines came from rows this season may no longer have), and its card and tooltip say which season it is of. An edit
/// makes it this season's (<see cref="MakeCurrent"/>), checked from then on.
/// </summary>
public static class ItemSeason
{
    /// <summary>
    /// Whether the item is an earlier season's: its stamp is the one the installed tables read through their
    /// previous-season columns - or the repair wrote it again out of a stamp the game reads no more, and keeps it as one
    /// (<see cref="LegacyRecord"/>). Any other older stamp is no season the game reads, and is judged as this season's,
    /// as before.
    /// </summary>
    public static bool IsLegacy(Item item) => ByStamp(item) || LegacyRecord.SeasonOf(item) is not null;

    /// <summary>The season a legacy item is of ("S13"), or null for an item that is not one.</summary>
    public static string? SeasonOf(Item item) =>
        ByStamp(item) ? D2SLib.Model.TXT.StatEncoding.PreviousSeason is { Length: > 0 } season ? season : null
        : LegacyRecord.SeasonOf(item);

    private static bool ByStamp(Item item) =>
        item.Version is { Length: > 0 } stamp && Previous() is { } previous && stamp == previous;

    /// <summary>What a legacy item's card and tooltip say of it - "Legacy · S13" - or null for one of this season.</summary>
    public static string? Mark(Item item) =>
        !IsLegacy(item) ? null
        : SeasonOf(item) is { } season ? L($"Legacy · {season}")
        : L("Legacy");

    /// <summary>
    /// Whether items of two stamps may lie in one file: the same stamp, or this season's beside the season before's -
    /// which the game mixes in one file and reads each by its own (ShockSacri: 89 items at 103, 6 at 105).
    /// Anything else is refused as it always was: seven pre-S10 items among eleven of S13 made the game refuse a
    /// character. An unknown stamp on either side asks nothing.
    /// </summary>
    public static bool Mix(string? era, string? arriving) =>
        era is not { Length: > 0 } || arriving is not { Length: > 0 } || era == arriving
        || (Readable(era) && Readable(arriving));

    /// <summary>
    /// The stamp an item the editor makes is given, for a file of <paramref name="era"/>: this season's wherever the
    /// file may hold it beside its own - a new item is never a legacy one - else the file's.
    /// </summary>
    public static string ForNew(string era) =>
        SeasonReading.CurrentStamp is { Length: > 0 } current && Mix(era, current) ? current : era;

    /// <summary>
    /// A legacy item made this season's, in place - what an edit does to one: stamped as this season stamps, and every
    /// property fitted to what this season's fields hold (<see cref="Collection.Migration.Fit"/>). Nothing for an item
    /// that is not legacy. Answers whether it was one.
    /// </summary>
    public static bool MakeCurrent(Item item)
    {
        if (LegacyRecord.SeasonOf(item) is not null)
        {
            LegacyRecord.Forget(item);
            return true;
        }

        if (!ByStamp(item) || SeasonReading.CurrentStamp is not { Length: > 0 } current)
            return false;

        Collection.Migration.Fit(item, stampAs: current);
        return true;
    }

    /// <summary>The season before's stamp, where both seasons' are known and differ.</summary>
    private static string? Previous() =>
        SeasonReading.CurrentStamp is { Length: > 0 } current && SeasonReading.PreviousStamp is { Length: > 0 } previous
        && current != previous ? previous : null;

    private static bool Readable(string stamp) =>
        stamp == SeasonReading.CurrentStamp || stamp == Previous();
}
