namespace PD2SPCompanion.Core.Testbed;

/// <summary>
/// A cube recipe that makes a magic Dagger at item level 99 from a Scroll of Identify - a test, not a feature, and temporary.
/// Which skills a dagger's staffmods draw from
/// is PD2's own code: the wiki says "Poison Strike and most martial arts skills", <c>itemtypes.txt</c> says the
/// Assassin's, and 29 real daggers showed Poison Strike, martial arts, Blade Fury and Blade Shield. A pile of daggers
/// the game rolls itself settles it. Item level 99 puts every skill in reach (required level + 7).
/// </summary>
public static class DaggerTest
{
    /// <summary>What the player puts in the cube, and what comes out - for the panel and the command to say.</summary>
    public const string Recipe = "Scroll of Identify -> magic Dagger, item level 99";

    private const string Description = "scroll of identify -> magic dagger ilvl 99 (PD2SPCompanion test)";

    /// <summary>Appends the row to a cubemain table - the one already built, so the cow portal stays.</summary>
    public static void AppendTo(TxtTable table)
    {
        int Need(string column) => table.IndexOfColumn(column) is int found && found >= 0
            ? found
            : throw new InvalidDataException($"cubemain.txt has no '{column}' column.");

        var row = new string[table.Columns.Count];
        for (int n = 0; n < row.Length; n++)
            row[n] = "";

        row[Need("description")] = Description;
        row[Need("enabled")] = "1";
        // Expansion, not ladder-only, as the game's own rows are; the output's item level fixed at 99.
        row[Need("version")] = "100";
        row[Need("lvl")] = "99";
        row[Need("numinputs")] = "1";
        row[Need("input 1")] = "isc";
        row[Need("output")] = "dgr,mag";
        if (table.IndexOfColumn("*eol") is int eol && eol >= 0)
            row[eol] = "0";

        table.Append(row);
    }
}
