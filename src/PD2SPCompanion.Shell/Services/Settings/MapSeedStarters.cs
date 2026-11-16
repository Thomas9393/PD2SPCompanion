using System.Collections.Generic;

namespace PD2SPCompanion.Shell.Services.Settings;

/// <summary>
/// The seeds the list starts with, so it is useful before anyone has kept one of their own.
/// <para>
/// These are long-shared community seeds, collected from a Chinese Diablo II forum thread and
/// translated. They are only a starting point - every one can be renamed or forgotten, and the
/// list is the person's from then on.
/// </para>
/// <para>
/// The shorthand was translated by a player in that community: <c>都虫</c> is
/// <b>Duriel</b> and <c>劳模</c> is <b>Mephisto</b>. Anyone who knows better can still correct a
/// remark in the panel, and the change is kept.
/// </para>
/// </summary>
public static class MapSeedStarters
{
    /// <summary>
    /// Raise this when seeds are added, and they reach people who have already run the editor.
    /// Their own list and their own remarks are untouched - only seeds they do not have go in.
    /// </summary>
    public const int Version = 1;

    public static IReadOnlyList<MapSeedNote> All =>
    [
        Note(9999, L("Good Crypt map")),
        Note(212696605, L("Countess, second route")),
        Note(252685412, L("Good map for Wirt's Leg")),
        Note(295230333, L("Diamond Bow - Lower Kurast, bottom left")),
        Note(353030734, L("Duriel and Mephisto")),
        Note(397461307, L("Great Axe - Mausoleum")),
        Note(411320971, L("Ward shield - Mausoleum")),
        Note(446413409, L("Ancient Tunnels, Sewers 2, Duriel and Mephisto")),
        Note(603656925, L("Good Cow Level")),
        Note(879797555, L("Mausoleum - armor rack first")),
        Note(967655068, L("Ancient Tunnels")),
        Note(1264278392, L("Baal run")),
        Note(1283519501, L("Crypt map")),
        Note(1365135098, L("Lower Kurast - chest run")),
        Note(1384278513, L("Mausoleum - armor rack first, hidden route")),
        Note(1456128388, L("Cow Level - tight packs")),
        Note(1518730302, L("Countess - short walk")),
        Note(1518983575, L("Ancient Tunnels - dense monsters")),
        Note(1528696831, L("Wormhole layout")),
        Note(1596075862, L("Cow Level, route 1")),
        Note(1622310954, L("Archon Plate - Mausoleum")),
        Note(1873949524, L("Crypt")),
        Note(1969200037, L("Good Tombs map")),
        Note(2013609290, L("Short path, Baal close")),
        Note(2146305511, L("Kraken Shell - Mausoleum")),
    ];

    private static MapSeedNote Note(uint seed, string remark) =>
        new() { Seed = seed, Remark = remark };
}
