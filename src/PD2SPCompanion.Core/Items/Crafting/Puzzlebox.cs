using System.Text;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// PD2's Puzzlebox, which bores sockets into an item that has none.
/// <para>
/// Seventeen rows in <c>cubemain.txt</c>, the Puzzlepiece's among them - the two recipes
/// grant the same counts, so they are read as one. How many an item gets depends on what it
/// is: a two-handed weapon or a bow takes 2 to 4, and everything else 1 to 2.
/// </para>
/// <para>
/// The recipe's own input is <c>&lt;type&gt;,nos</c> - <b>no sockets</b> - so an item that
/// already has them is not something the cube will take, however it came by them. That is
/// the whole of the rule here too.
/// </para>
/// </summary>
public static class Puzzlebox
{
    /// <summary>How many sockets each recipe gives, by the item type it names, in the file's order.</summary>
    private static List<(string Type, int Least, int Most)> _rows = [];

    /// <summary>
    /// What this item would be given, or null for one no recipe takes: the <b>first recipe in
    /// the file</b> whose type the item is, as the cube matches them - not the nearest type. A
    /// Matriarchal Spear is <c>aspe</c>, a spear and a weapon by its first type and <c>2han</c> by
    /// its second; the two-hander's row (2-4) comes before the weapon's (1-2), and nearest-first
    /// gave it the weapon's (two real crafted pikes with four sockets read as
    /// forged).
    /// </summary>
    public static (int Least, int Most)? RangeFor(Item item)
    {
        var lineage = new HashSet<string>(ItemTypes.LineageOfBase(item.Code), StringComparer.OrdinalIgnoreCase);

        foreach (var (type, least, most) in _rows)
        {
            if (lineage.Contains(type))
                return (least, most);
        }

        return null;
    }

    /// <summary>
    /// Whether the cube would take this item: one the recipes name, with no sockets in it.
    /// <para>
    /// Asked of the item as it stands, not of anything staged, so a count that has been
    /// rolled and not yet kept can be rolled again as often as you like - which is what
    /// standing in front of the cube with a stack of boxes amounts to.
    /// </para>
    /// </summary>
    public static bool CanTake(Item item) =>
        item.TotalNumberOfSockets == 0 && RangeFor(item) is not null;

    /// <summary>
    /// How many sockets one transmute lands, held to what the base can actually hold: the
    /// recipe offers a bow 2 to 4 and a bow that takes 3 is given 3.
    /// </summary>
    public static int Roll(Item item, Random random)
    {
        if (RangeFor(item) is not { } range)
            return 0;

        int rolled = range.Most <= range.Least
            ? range.Least
            : random.Next(range.Least, range.Most + 1);

        int room = ItemBases.MaximumSockets(item);

        return room > 0 ? Math.Clamp(rolled, 1, room) : rolled;
    }

    /// <summary>
    /// The box's own inventory graphic, named the way every other item's is - so the button
    /// wears the thing it does rather than a drawing of one. Read from the tables at need,
    /// since it is one lookup and it keeps the code out of the loader.
    /// </summary>
    public static string? SpriteName =>
        D2SLib.Core.TXT.ItemsTXT.GetByCode("lbox")?.Get("invfile")?.Value?.Trim()
            is { Length: > 0 } named
            ? named
            : null;

    public static void Load(GameData game)
    {
        var found = new List<(string, int, int)>();

        try
        {
            if (game.Lines("cubemain") is not { } lines)
                return;
            if (lines.Length == 0)
                return;

            string[] header = lines[0].Split('\t');

            int input = Array.IndexOf(header, "input 1");
            int second = Array.IndexOf(header, "input 2");
            int code = Array.IndexOf(header, "mod 1");
            int low = Array.IndexOf(header, "mod 1 min");
            int high = Array.IndexOf(header, "mod 1 max");
            int live = Array.IndexOf(header, "enabled");

            if (input < 0 || second < 0 || code < 0 || low < 0 || high < 0)
                return;

            foreach (string line in lines.Skip(1))
            {
                string[] cells = line.Split('\t');
                if (cells.Length <= Math.Max(high, second))
                    continue;

                // The box and the piece, which are the two items that do this.
                string with = cells[second].Trim().Trim('"');
                if (!with.StartsWith("lbox,", StringComparison.OrdinalIgnoreCase)
                    && !with.StartsWith("lpp,", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!cells[code].Trim().Equals("sock", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (live >= 0 && cells.Length > live && cells[live].Trim() != "1")
                    continue;

                string[] parts = cells[input].Trim().Trim('"').Split(',');
                string type = parts[0].Trim();

                if (type.Length == 0
                    || !int.TryParse(cells[low].Trim(), out int least)
                    || !int.TryParse(cells[high].Trim(), out int most))
                {
                    continue;
                }

                // The box's row and the piece's name the same types with the same counts; the first
                // of each is the one the cube reaches.
                if (!found.Any(row => row.Item1.Equals(type, StringComparison.OrdinalIgnoreCase)))
                    found.Add((type, Math.Min(least, most), Math.Max(least, most)));
            }
        }
        catch (Exception problem)
        {
            // Without the table the button simply has nothing to offer, which is better
            // than offering a number that came from nowhere.
            Log.Caught(problem);
        }
        finally
        {
            // The early returns too: a cubemain missing or with its columns renamed kept the last tables' rows.
            _rows = found;
        }
    }
}
