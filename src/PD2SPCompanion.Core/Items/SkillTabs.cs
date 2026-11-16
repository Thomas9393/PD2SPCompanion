using System.Text;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// The three skill trees each class has, named as the game names them.
/// <para>
/// A skill-tab stat records a class and which of that class's three trees, 0 to 2. The
/// names come from <c>charstats.txt</c>, whose <c>StrSkillTab1..3</c> columns hold the
/// string key for each - so a class's trees are read from the class's own row rather than
/// from a list here that could drift out of step with the game.
/// </para>
/// <para>
/// Those keys are globally numbered, <c>StrSklTabItem1</c> to <c>21</c>, and the blocks do
/// not follow the order classes are numbered in a save: 1-3 Amazon, 4-6 Paladin, 7-9
/// Necromancer, 10-12 Barbarian, 13-15 Sorceress, 16-18 Druid, 19-21 Assassin. That
/// numbering is not what the stat stores, which is why it is only used to find the name.
/// </para>
/// </summary>
public static class SkillTabs
{
    /// <summary>
    /// Three string keys per class, in the order the class's row lists them. Kept as keys
    /// and resolved when asked: the string tables may not be loaded yet when this is, and
    /// resolving eagerly would quietly bake in empty names.
    /// </summary>
    private static string[][] _byClass = [];

    /// <summary>
    /// The classes in the order the save numbers them, which is the order the table lists
    /// them: Amazon, Sorceress, Necromancer, Paladin, Barbarian, Druid, Assassin.
    /// </summary>
    private static string[] _classes = [];

    public static IReadOnlyList<string> Classes => _classes;

    /// <summary>The class an id names, or null for one the table does not carry.</summary>
    public static string? ClassOf(int id) =>
        id >= 0 && id < _classes.Length ? _classes[id] : null;

    public static void Load(GameData game)
    {
        var classes = new List<string[]>();
        var named = new List<string>();

        try
        {
            if (game.Lines("charstats") is { } lines)
            {
                string[] header = lines[0].Split('\t');

                int name = Array.IndexOf(header, "class");
                int[] columns =
                [
                    Array.IndexOf(header, "StrSkillTab1"),
                    Array.IndexOf(header, "StrSkillTab2"),
                    Array.IndexOf(header, "StrSkillTab3"),
                ];

                if (name >= 0 && columns.All(column => column >= 0))
                {
                    foreach (string line in lines.Skip(1))
                    {
                        string[] cells = line.Split('\t');
                        if (cells.Length <= columns.Max() || cells[name].Trim().Length == 0)
                            continue;

                        // The table ends with rows that are not playable classes.
                        if (cells[name].Trim().StartsWith("Expansion", StringComparison.OrdinalIgnoreCase))
                            continue;

                        classes.Add([.. columns.Select(column => cells[column].Trim())]);
                        named.Add(cells[name].Trim());

                        if (classes.Count == CharacterClasses.Names.Count)
                            break;
                    }
                }
            }
        }
        catch (Exception)
        {
            // Without the table a tab shows its number, which says less but says nothing
            // wrong.
        }

        _byClass = [.. classes];
        _classes = [.. named];
    }

    /// <summary>
    /// The name of one of a class's trees, or null when the tables cannot name it.
    /// </summary>
    public static string? Name(int classId, int tab)
    {
        if (classId < 0 || classId >= _byClass.Length)
            return null;
        if (tab < 0 || tab >= _byClass[classId].Length)
            return null;

        string name = Resolve(_byClass[classId][tab]);
        return name.Length > 0 ? name : null;
    }

    /// <summary>All three of a class's trees, for choosing between them.</summary>
    public static IReadOnlyList<string> For(int classId) =>
        classId >= 0 && classId < _byClass.Length
            ? [.. _byClass[classId].Select(Resolve)]
            : [];

    /// <summary>
    /// The tab string carries the affix wording - "+%d to Combat Skills" - so the prefix
    /// comes off, the same way the skill tree headings do it.
    /// </summary>
    private static string Resolve(string key) =>
        key.Length == 0 ? "" : StringTable.Current?.Tab(key) ?? "";
}
