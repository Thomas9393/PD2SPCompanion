namespace PD2SPCompanion.Core.GameFiles;

/// <summary>
/// The seven classes, by the id a save stores: the order the engine numbers them and every
/// table lists them in. Fixed by the save's class byte rather than read from a table, so the
/// one list here stands for the copies that used to be kept wherever a class was named.
/// </summary>
public static class CharacterClasses
{
    /// <summary>The names charstats.txt gives its rows: "Amazon" to "Assassin".</summary>
    public static IReadOnlyList<string> Names { get; } =
        ["Amazon", "Sorceress", "Necromancer", "Paladin", "Barbarian", "Druid", "Assassin"];

    /// <summary>The codes skills.txt and itemtypes.txt use: "ama" to "ass".</summary>
    public static IReadOnlyList<string> Codes { get; } = ["ama", "sor", "nec", "pal", "bar", "dru", "ass"];

    /// <summary>The name of a class id, or null for one past the seven.</summary>
    public static string? NameOf(int id) => id >= 0 && id < Names.Count ? Names[id] : null;

    /// <summary>The code of a class id, or null for one past the seven.</summary>
    public static string? CodeOf(int id) => id >= 0 && id < Codes.Count ? Codes[id] : null;

    /// <summary>The name a class code stands for - "Amazon" for <c>ama</c> - or null.</summary>
    public static string? NameOfCode(string? code)
    {
        for (int id = 0; id < Codes.Count; id++)
        {
            if (string.Equals(Codes[id], code, StringComparison.OrdinalIgnoreCase))
                return Names[id];
        }

        return null;
    }
}
