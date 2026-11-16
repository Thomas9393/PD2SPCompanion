namespace PD2SPCompanion.Core.Testbed;

/// <summary>
/// Cube recipes that desecrate an amulet by hand, to see in the game what the stored values do -
/// a test, not a feature. PD2's real desecration runs in its own code
/// after Lucion dies: no cubemain row starts it. But its two stats have properties any recipe can
/// set - <c>desecrate</c> writes <c>desecrated</c> (2 bits in S13) and <c>desecratenum</c> writes
/// <c>desecrator</c> - exactly as <c>corrupt</c> and <c>corruptnum</c> write corruption's pair.
/// <para>
/// The market's 261 desecrated amulets all hold <c>desecrated</c> 2 or 3: 2 on every one whose
/// desecration added enhanced damage, 3 on every other mod. What the listings cannot show is what
/// the game does with the value - whether it alone changes the amulet's appearance, and how the
/// game prints it. These rows set each value on an amulet, adding nothing else, so the picture
/// and the tooltip before and after tell.
/// </para>
/// </summary>
public static class DesecrationTest
{
    /// <summary>The rows: an amulet and something every vendor sells, and the value each sets.</summary>
    public static readonly (string Ingredient, string Name, int Value)[] Rows =
    [
        ("key", "Key", 1),
        ("isc", "Scroll of Identify", 2),
        ("tsc", "Scroll of Town Portal", 3),
    ];

    /// <summary>What each row says it is, so anyone reading the file can tell.</summary>
    private static string Description(int value) => $"amulet + ingredient -> desecrated {value} (PD2SPCompanion test)";

    /// <summary>Appends the rows to a cubemain table - the one already built, so the cow portal stays.</summary>
    public static void AppendTo(TxtTable table)
    {
        int Need(string column) => table.IndexOfColumn(column) is int found && found >= 0
            ? found
            : throw new InvalidDataException($"cubemain.txt has no '{column}' column.");

        int description = Need("description"), enabled = Need("enabled"), inputs = Need("numinputs");
        int version = Need("version"), level = Need("ilvl");
        int mod1Chance = Need("mod 1 chance"), mod2Chance = Need("mod 2 chance");
        int first = Need("input 1"), second = Need("input 2"), output = Need("output");
        int mod1 = Need("mod 1"), mod1Min = Need("mod 1 min"), mod1Max = Need("mod 1 max");
        int mod2 = Need("mod 2"), mod2Min = Need("mod 2 min"), mod2Max = Need("mod 2 max");
        int eol = table.IndexOfColumn("*eol");

        foreach (var (ingredient, _, value) in Rows)
        {
            var row = new string[table.Columns.Count];
            for (int n = 0; n < row.Length; n++)
                row[n] = "";

            row[description] = Description(value);
            row[enabled] = "1";

            // As the game's own amulet corruption rows are: expansion (version 100), the item
            // keeping its level (ilvl 100), and each mod certain (chance 100). Not ladder-only.
            row[version] = "100";
            row[level] = "100";
            row[mod1Chance] = row[mod2Chance] = "100";
            row[inputs] = "2";
            row[first] = "amul";
            row[second] = ingredient;
            row[output] = "useitem";
            row[mod1] = "desecrate";
            row[mod1Min] = row[mod1Max] = $"{value}";
            row[mod2] = "desecratenum";
            row[mod2Min] = row[mod2Max] = "1";
            if (eol >= 0)
                row[eol] = "0";

            table.Append(row);
        }
    }
}
