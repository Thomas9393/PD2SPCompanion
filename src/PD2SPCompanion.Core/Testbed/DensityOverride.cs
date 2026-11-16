using System.Text;

namespace PD2SPCompanion.Core.Testbed;

/// <summary>What scaling a table did, so the panel can say it rather than imply it.</summary>
public sealed record DensityReport(int Scaled, int Clamped, int Skipped)
{
    public bool AnyClamped => Clamped > 0;

    public override string ToString() =>
        $"{Scaled} density values scaled ({Clamped} hit the cap, {Skipped} preset or town "
        + "values left alone)";
}

/// <summary>
/// Which areas a density change reaches: everything, or the farming
/// areas only.
/// </summary>
public enum DensityScope
{
    /// <summary>Every level that spawns at all.</summary>
    All,

    /// <summary>The game's own areas of level 85 or more in hell, and the cow level.</summary>
    Level85Cows,

    /// <summary>Those, and every area PD2 added: its maps and endgame areas.</summary>
    Level85CowsMaps,
}

/// <summary>One loose table to be written, and what it should contain.</summary>
public sealed record ModdedFile(string Table, byte[] Bytes);

/// <summary>
/// Builds the loose <c>levels.txt</c> that makes monsters denser, without writing anything.
/// <para>
/// Generation is kept free of file I/O on purpose: the arithmetic and the skip rules are the
/// part worth testing, and they can be checked against the reference harness without an
/// install anywhere near it. <see cref="DensityInstaller"/> owns everything that touches
/// disk.
/// </para>
/// </summary>
public static class DensityOverride
{
    /// <summary>
    /// The ceiling a density is held to: the engine's own. Populating a room rolls each 3x3
    /// cell against <c>MonDen</c> out of 100000, and clamps <c>MonDen</c> to 10000 first - so
    /// at most one cell in ten ever gets a pack, and a higher number does nothing (D2MOO,
    /// <c>D2GAME_PopulateRoom_6FC67190</c>; seen in game as 10x and 20x looking
    /// like 5x). It was d2modgen's 9900 before the reason was found.
    /// </summary>
    public const int Ceiling = 10000;

    /// <summary>
    /// The density "multiple" that means every chosen area at <see cref="Ceiling"/>: large enough
    /// that any spawning level's density times it passes the ceiling (the lowest PD2 ships is 50).
    /// Said as MAX. More than 5x where an area is quiet - 500 would need 20x - and the same as 4x
    /// on PD2's busiest maps.
    /// </summary>
    public const double MaxDensity = 1000;

    /// <summary>
    /// The largest pack multiple offered: pack size is the one table lever past the density
    /// ceiling. 10x was tried in game and crammed the screen so full that loot could hardly be
    /// picked up, so 5x is the top. Pack size and density were one
    /// setting before; 4x packs at 5x density was the "MAX" seen in game as about 10x.
    /// </summary>
    public const int MostPacks = 5;

    /// <summary>The most one pack holds: <c>MinGrp</c>/<c>MaxGrp</c> are a byte, and d2modgen stops at 99.</summary>
    public const int MostInPack = 99;

    /// <summary>
    /// The floor the game wants on a level that spawns at all. A level that spawns nothing
    /// is left alone rather than raised to it; see <see cref="Scale"/>.
    /// </summary>
    public const int Floor = 100;

    private static readonly string[] DensityColumns = ["MonDen", "MonDen(N)", "MonDen(H)"];

    /// <summary>
    /// Multiplies every real monster density in <c>levels.txt</c>, and reports what that
    /// came to.
    /// <para>
    /// A density of 0 or blank means the level has no random spawn pass at all - towns and
    /// preset levels - so it is skipped rather than scaled. Letting it through would drop it
    /// on the floor of 100 and invent spawns where the game means there to be none.
    /// </para>
    /// </summary>
    public static (ModdedFile File, DensityReport Report) Build(GameData game, double multiplier,
                                                                DensityScope scope = DensityScope.All)
    {
        var (table, _) = game.ReadExcel("levels");

        int scaled = 0, clamped = 0, skipped = 0;
        var inScope = ScopeTest(table, scope);

        int[] columns = [.. DensityColumns
            .Select(table.IndexOfColumn)
            .Where(column => column >= 0)];

        if (columns.Length == 0)
            throw new InvalidDataException("levels.txt has no MonDen columns.");

        foreach (string[] cells in table.Rows)
        {
            if (!inScope(cells))
                continue;

            foreach (int column in columns)
            {
                if (column >= cells.Length)
                    continue;

                if (!int.TryParse(cells[column].Trim(), out int density) || density == 0)
                {
                    skipped++;
                    continue;
                }

                int wanted = (int)Math.Round(density * multiplier, MidpointRounding.AwayFromZero);

                if (wanted > Ceiling)
                {
                    wanted = Ceiling;
                    clamped++;
                }

                cells[column] = Math.Max(wanted, Floor).ToString();
                scaled++;
            }
        }

        // Proven against the source before it is trusted: if the round-trip of an unmodified
        // table is not exact, nothing built from it should be fed to the game.
        return (
            new ModdedFile("levels", Serialize(table)),
            new DensityReport(scaled, clamped, skipped));
    }

    /// <summary>
    /// Makes every monster's packs bigger in <c>monstats.txt</c> - more monsters per spawn,
    /// where density can add no more spawns. Returns the file and how many monsters it touched.
    /// <para>
    /// A monster whose pack is exactly one (<c>MinGrp</c> = <c>MaxGrp</c> = 1) is meant to be
    /// alone and stays so, as d2modgen leaves it. Fallen and scarabs are forced to packs of one by
    /// the engine whatever this says. A pack is placed around its leader, so a crowded room
    /// fails to place some - the multiple is an upper bound, not a count.
    /// </para>
    /// </summary>
    public static (ModdedFile File, int Scaled) BuildPacks(GameData game, double factor,
                                                           DensityScope scope = DensityScope.All)
    {
        var (table, _) = game.ReadExcel("monstats");

        // Pack size is the monster's, not the area's, so it cannot be kept to the chosen areas.
        // The nearest is to grow only the monsters those areas name; they still come bigger
        // wherever else they spawn.
        var named = scope == DensityScope.All ? null : MonstersIn(game, scope);
        int monsterId = table.IndexOfColumn("Id");

        int min = table.IndexOfColumn("MinGrp"), max = table.IndexOfColumn("MaxGrp");
        if (min < 0 || max < 0)
            throw new InvalidDataException("monstats.txt has no MinGrp/MaxGrp columns.");

        int scaled = 0;
        foreach (string[] cells in table.Rows)
        {
            if (Math.Max(min, max) >= cells.Length
                || !int.TryParse(cells[min].Trim(), out int least)
                || !int.TryParse(cells[max].Trim(), out int most)
                || least <= 0 || most <= 0 || (least == 1 && most == 1))
                continue;

            if (named is not null && (monsterId < 0 || !named.Contains(cells[monsterId].Trim())))
                continue;

            cells[min] = Math.Min(MostInPack, (int)Math.Round(least * factor, MidpointRounding.AwayFromZero)).ToString();
            cells[max] = Math.Min(MostInPack, (int)Math.Round(most * factor, MidpointRounding.AwayFromZero)).ToString();
            scaled++;
        }

        return (new ModdedFile("monstats", Serialize(table)), scaled);
    }

    /// <summary>The game's own last level. Every row after it is PD2's: its maps and endgame areas.</summary>
    public const int LastOriginalLevel = 136;

    /// <summary>The cow level's row. Its hell level (81) is under 85, but it is farmed like one.</summary>
    public const int CowLevel = 39;

    /// <summary>Whether a levels.txt row is in the scope, by its id and its hell area level.</summary>
    private static Func<string[], bool> ScopeTest(TxtTable table, DensityScope scope)
    {
        if (scope == DensityScope.All)
            return _ => true;

        int id = table.IndexOfColumn("Id"), hellLevel = table.IndexOfColumn("MonLvl3Ex");
        if (id < 0 || hellLevel < 0)
            throw new InvalidDataException("levels.txt has no Id or MonLvl3Ex column.");

        return cells =>
        {
            if (Math.Max(id, hellLevel) >= cells.Length || !int.TryParse(cells[id].Trim(), out int level))
                return false;

            if (level == CowLevel)
                return true;

            if (level > LastOriginalLevel)
                return scope == DensityScope.Level85CowsMaps;

            return int.TryParse(cells[hellLevel].Trim(), out int areaLevel) && areaLevel >= 85;
        };
    }

    /// <summary>Every monster the scope's levels name in their spawn lists (mon, nmon, umon).</summary>
    private static HashSet<string> MonstersIn(GameData game, DensityScope scope)
    {
        var (levels, _) = game.ReadExcel("levels");
        var inScope = ScopeTest(levels, scope);

        int[] lists = [.. levels.Columns
            .Select((name, at) => (name, at))
            .Where(each => System.Text.RegularExpressions.Regex.IsMatch(each.name, "^[nu]?mon[0-9]+$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            .Select(each => each.at)];

        var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string[] cells in levels.Rows.Where(inScope))
        {
            foreach (int column in lists)
            {
                if (column < cells.Length && cells[column].Trim() is { Length: > 0 } monster)
                    named.Add(monster);
            }
        }

        return named;
    }

    /// <summary>
    /// Turns a parsed table back into the bytes the game reads.
    /// <para>
    /// <see cref="TxtTable.Parse"/> is lossy in two ways that matter here: it splits on
    /// <c>\n</c> and trims the <c>\r</c>, and it drops the trailing blank line. Both have to
    /// be put back - the tables are CRLF and end with a blank line - or the file is not the
    /// shape the game was given.
    /// </para>
    /// </summary>
    public static byte[] Serialize(TxtTable table)
    {
        var text = new StringBuilder();

        text.Append(string.Join('\t', table.Columns)).Append("\r\n");

        foreach (string[] cells in table.Rows)
            text.Append(string.Join('\t', cells)).Append("\r\n");

        return Encoding.ASCII.GetBytes(text.ToString());
    }

    /// <summary>
    /// Whether this table survives a parse and a serialize unchanged. The one claim the
    /// generated file rests on, so it is asked rather than assumed.
    /// </summary>
    public static bool RoundTrips(GameData game, string table)
    {
        var source = game.Read(GameData.ExcelPath(table))
            ?? throw new FileNotFoundException($"'{table}.txt' is not in any archive.");

        return Serialize(TxtTable.Parse(table, source.Data)).AsSpan()
            .SequenceEqual(source.Data);
    }
}
