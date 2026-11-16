namespace PD2SPCompanion.Core.Testbed;

/// <summary>
/// Builds the loose <c>cubemain.txt</c> that opens the cow level from two tomes, without
/// writing anything.
/// <para>
/// The recipe is <b>added</b> rather than substituted: the shipped Wirt's Leg recipes stay
/// exactly as they are, and one extra row is appended that takes two things every vendor
/// keeps in permanent stock. The row is a copy of the game's own cow recipe with one input
/// swapped, so every column it does not name is whatever the game put there.
/// </para>
/// <para>
/// The alternative was to make Wirt's Leg purchasable, which needs <c>spawnable</c> set on
/// its row in <c>weapons.txt</c> - and that puts the leg into the item generator at large,
/// where it turns up as random vendor stock and monster drops in every act. Moving one
/// cubemain row touches no item table at all.
/// </para>
/// <para>
/// Two things this cannot change, both decided by the engine behind <c>op 28</c>: the portal
/// still wants that difficulty finished, and killing the Cow King still bars it afterwards.
/// </para>
/// </summary>
public static class CowPortalOverride
{
    /// <summary>The recipe as a player would read it, for the panel to show.</summary>
    public const string Recipe = "Tome of Identify + Tome of Town Portal";

    /// <summary>Tome of Identify, and Tome of Town Portal - the recipe's two inputs.</summary>
    private const string Identify = "ibk";
    private const string TownPortal = "tbk";

    /// <summary>
    /// The cube operation that hands off to the engine, and the act it opens a portal to.
    /// Every shipped cow recipe carries this pair, and nothing else carries <c>param 2</c>.
    /// </summary>
    private const string Operation = "28";
    private const string CowLevel = "2";

    /// <summary>What the appended row says it is, so anyone reading the file can tell.</summary>
    private const string Description = "tome + tome -> portal to cow lvl (PD2SPCompanion)";

    public static ModdedFile Build(GameData game)
    {
        var (table, _) = game.ReadExcel("cubemain");
        AppendTo(table);
        return new ModdedFile("cubemain", DensityOverride.Serialize(table));
    }

    /// <summary>
    /// The recipe appended to a cubemain table, for a file that carries other rows as well - the
    /// desecration test's (see <see cref="DesecrationTest"/>).
    /// </summary>
    public static void AppendTo(TxtTable table)
    {
        int description = Need(table, "description");
        int enabled = Need(table, "enabled");
        int operation = Need(table, "op");
        int level = Need(table, "param");
        int first = Need(table, "input 1");
        int second = Need(table, "input 2");

        string[]? template = null;

        foreach (string[] row in table.Rows)
        {
            if (Cell(row, operation) != Operation
                || Cell(row, level) != CowLevel
                || Cell(row, enabled) != "1")
            {
                continue;
            }

            // Already there - which is not an error worth writing a second copy of.
            if (Cell(row, first) == Identify && Cell(row, second) == TownPortal)
                throw new InvalidDataException("cubemain.txt already has this recipe.");

            template ??= row;
        }

        if (template is null)
            throw new InvalidDataException("cubemain.txt has no cow portal recipe to copy.");

        // Padded to the header's width: a row the game wrote may stop short of the last
        // column, and a row that stops short of a column we are about to set cannot.
        var added = new string[table.Columns.Count];
        for (int n = 0; n < added.Length; n++)
            added[n] = n < template.Length ? template[n] : "";

        added[description] = Description;
        added[first] = Identify;
        added[second] = TownPortal;

        table.Append(added);
    }

    private static string Cell(string[] cells, int column) =>
        column < cells.Length ? cells[column].Trim() : "";

    private static int Need(TxtTable table, string column) =>
        table.IndexOfColumn(column) is int found && found >= 0
            ? found
            : throw new InvalidDataException($"cubemain.txt has no '{column}' column.");
}
