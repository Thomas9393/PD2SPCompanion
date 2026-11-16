namespace PD2SPCompanion.Core.Saves;

/// <summary>A container's size, in inventory cells.</summary>
public sealed record GridSize(int Columns, int Rows);

/// <summary>
/// How big each container is, read from the installed <c>inventory.txt</c>.
/// <para>
/// These are not vanilla's numbers. PD2 doubles the character inventory to 10x8, widens the
/// cube to 4x4 and sets the expansion stash to 10x15, so anything hardcoded from a vanilla
/// reference silently mis-sizes every grid. The table ships in <c>pd2data.mpq</c> and is read
/// at runtime like the rest of the game data.
/// </para>
/// </summary>
public sealed class InventoryLayout
{
    /// <summary>Used when the table cannot be read. These are vanilla's sizes, which are
    /// wrong for PD2 - a visibly wrong grid beats refusing to draw one.</summary>
    public static InventoryLayout Fallback { get; } =
        new(new GridSize(10, 4), new GridSize(6, 8), new GridSize(3, 4), ClassicStashPage, null);

    /// <summary>The page an item names when it is in the character's inventory.</summary>
    public const byte InventoryPage = 1;

    /// <summary>
    /// The cube. Vanilla's number, and <b>confirmed against PD2 in game</b>:
    /// an item put in the cube by the game itself reads back on page 4. Worth having
    /// checked - the stash is 6 in PD2 where vanilla says 5, and a wrong page hides items
    /// in silence rather than failing.
    /// </summary>
    public const byte CubePage = 4;

    /// <summary>The small classic stash. Inferred, not confirmed.</summary>
    public const byte ClassicStashPage = 5;

    /// <summary>
    /// The expansion stash, confirmed in game.
    /// <para>
    /// Vanilla numbers the stash 5, and writing 5 does work - the game reads it and shows
    /// the item in the stash. But when the game next saves it rewrites that item as page
    /// 6, and an editor still looking at 5 then shows an empty stash while the item sits
    /// somewhere it does not draw. PD2 inserts containers of its own, and the expansion
    /// stash is a different row from the classic one, so it is a different page too.
    /// </para>
    /// </summary>
    public const byte ExpansionStashPage = 6;

    public GridSize Inventory { get; }
    public GridSize Stash { get; }
    public GridSize Cube { get; }

    /// <summary>Which page this character's stash items name. Size and page come from the
    /// same choice, so they cannot drift apart.</summary>
    public byte StashPage { get; }

    /// <summary>Which archive the table came from, or null when the fallback is in use.</summary>
    public string? Source { get; }

    private InventoryLayout(
        GridSize inventory, GridSize stash, GridSize cube, byte stashPage, string? source)
    {
        Inventory = inventory;
        Stash = stash;
        Cube = cube;
        StashPage = stashPage;
        Source = source;
    }

    public static InventoryLayout Load(GameData game, byte classId, bool expansion)
    {
        try
        {
            var (table, archive) = game.KeptExcel("inventory");

            string className = CharacterClasses.NameOf(classId) ?? CharacterClasses.Names[0];

            // Expansion characters use the large stash; the small one is classic-only.
            var stashNames = expansion
                ? new[] { "Big Bank Page 1", "Big Bank Page", "Bank Page 1" }
                : new[] { "Bank Page 1", "Bank Page" };

            return new InventoryLayout(
                Find(table, [className]) ?? Fallback.Inventory,
                Find(table, stashNames) ?? Fallback.Stash,
                Find(table, ["Transmogrify Box Page 1", "Transmogrify Box"]) ?? Fallback.Cube,
                expansion ? ExpansionStashPage : ClassicStashPage,
                archive);
        }
        catch (Exception)
        {
            // The grids are cosmetic; a missing or malformed table must not stop a character
            // from being opened and edited.
            return Fallback;
        }
    }

    /// <summary>The first named row that exists, as a grid size.</summary>
    private static GridSize? Find(TxtTable table, IReadOnlyList<string> rowNames)
    {
        int nameColumn = table.IndexOfColumn("class");
        int x = table.IndexOfColumn("gridX");
        int y = table.IndexOfColumn("gridY");
        if (nameColumn < 0 || x < 0 || y < 0)
            return null;

        foreach (string wanted in rowNames)
        {
            for (int row = 0; row < table.Rows.Count; row++)
            {
                var fields = table.Rows[row];
                if (nameColumn >= fields.Length
                    || !fields[nameColumn].Equals(wanted, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Rows the game does not use carry -1, and PD2's own LocalStashPagesSave
                // marker carries 127. Neither is a drawable grid.
                if (int.TryParse(Cell(fields, x), out int columns)
                    && int.TryParse(Cell(fields, y), out int rows)
                    && columns is > 0 and <= 64 && rows is > 0 and <= 64)
                {
                    return new GridSize(columns, rows);
                }
            }
        }
        return null;
    }

    private static string Cell(string[] fields, int index) =>
        index < fields.Length ? fields[index] : "";
}
