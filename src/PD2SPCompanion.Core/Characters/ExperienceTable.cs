namespace PD2SPCompanion.Core.Characters;

/// <summary>
/// The experience a level takes, from <c>experience.txt</c>. PD2's: the
/// <c>MaxLvl</c> row says 99 for every class, and row <c>n</c> is the experience that reaches
/// level <c>n + 1</c> - row 1 is 500, where level 2 begins. Row 99 is not a level but the most
/// experience a character can hold, 3,837,739,017 - past a signed 32-bit number, as level 99's
/// own 3,520,485,254 already is.
/// </summary>
public sealed class ExperienceTable
{
    private readonly long[] _rows;

    private ExperienceTable(int maxLevel, long[] rows)
    {
        MaxLevel = maxLevel;
        _rows = rows;
    }

    /// <summary>No table: nothing can be levelled against it.</summary>
    public static ExperienceTable Unknown { get; } = new(0, []);

    /// <summary>The highest level a character reaches.</summary>
    public int MaxLevel { get; }

    public bool IsAvailable => MaxLevel > 0 && _rows.Length > MaxLevel;

    /// <summary>The experience at which <paramref name="level"/> begins; nought for level 1.</summary>
    public long Start(int level) => level <= 1 ? 0 : _rows[Math.Min(level, MaxLevel) - 1];

    /// <summary>The most experience a character can hold.</summary>
    public long Cap => _rows[MaxLevel];

    /// <summary>The level <paramref name="experience"/> falls in.</summary>
    public int LevelOf(long experience)
    {
        int level = 1;
        while (level < MaxLevel && Start(level + 1) <= experience)
            level++;

        return level;
    }

    /// <summary>The class's column: every class has one, and a mod may make them differ.</summary>
    public static ExperienceTable For(GameData game, string className)
    {
        try
        {
            // Kept by the tables, not read again at every open and every undo.
            var table = game.Excel("experience");
            int column = table.IndexOfColumn(className);
            int label = table.IndexOfColumn("Level");
            if (column < 0 || label < 0)
                return Unknown;

            int maxLevel = 0;
            var rows = new List<long>();
            foreach (var fields in table.Rows)
            {
                if (column >= fields.Length || !long.TryParse(fields[column], out long value))
                    continue;

                if (fields[label].Equals("MaxLvl", StringComparison.OrdinalIgnoreCase))
                    maxLevel = (int)value;
                else
                    rows.Add(value);
            }

            var read = new ExperienceTable(maxLevel, [.. rows]);
            return read.IsAvailable ? read : Unknown;
        }
        catch (Exception)
        {
            return Unknown;
        }
    }
}
