using System.Text;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// Which items carry a quantity, and how large it may be.
/// <para>
/// Whether the field exists at all is a property of the base item, not of the one on the
/// floor: the parser writes nine bits of quantity only for a base flagged <c>stackable</c>
/// in <c>misc.txt</c>, <c>weapons.txt</c> or <c>armor.txt</c>. Nothing else has the field.
/// </para>
/// <para>
/// <b>Stackable is not the same as counted.</b> PD2 gave throwing weapons and arrows/bolts
/// unlimited quantity - the wiki's General Changes says so in as many words, and the game
/// draws no number on them, throwing potions included. The bases are still marked stackable
/// and still store a number; it simply means nothing. <c>itemtypes.txt</c> separates them
/// exactly: <c>Throwable</c> covers the knives, axes, javelins and missile potions, and a
/// non-empty <c>Quiver</c> covers arrows and bolts. So this is read from the install rather
/// than held as a list of codes here.
/// </para>
/// </summary>
public static class Stacks
{
    /// <summary>
    /// The widest a quantity can be written: the parser gives it nine bits. Every stackable
    /// base is well inside this - the largest is a 350-arrow quiver - except <c>gld</c>,
    /// whose 5,000 is loose gold on the ground and never an item in a save.
    /// </summary>
    public const int FieldMaximum = 511;

    private static Dictionary<string, int> _maxStack = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, int> _minStack = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, string> _typeOf = new(StringComparer.OrdinalIgnoreCase);
    private static HashSet<string> _uncounted = new(StringComparer.OrdinalIgnoreCase);

    public static void Load(GameData game)
    {
        var maxStack = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var minStack = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var typeOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string table in new[] { "misc", "weapons", "armor" })
            ReadBases(game, table, maxStack, minStack, typeOf);

        _maxStack = maxStack;
        _minStack = minStack;
        _typeOf = typeOf;
        _uncounted = ReadUncountedTypes(game);
    }

    /// <summary>
    /// An item's code as the tables spell it. A save pads the field to four characters, so
    /// the code off an item is "key " where the table says "key" - and every lookup made
    /// with the padded form quietly finds nothing.
    /// </summary>
    private static string? Key(string? code) =>
        code?.Trim() is { Length: > 0 } trimmed ? trimmed : null;

    /// <summary>True when the base item stores a quantity at all.</summary>
    public static bool IsStackable(string? code) =>
        Key(code) is { } id && _maxStack.ContainsKey(id);

    /// <summary>
    /// True when that quantity is worth showing. False for the bases PD2 made unlimited,
    /// whose stored number the game ignores.
    /// </summary>
    public static bool IsCounted(string? code) =>
        Key(code) is { } id
        && _maxStack.ContainsKey(id)
        && !(_typeOf.TryGetValue(id, out string? type) && _uncounted.Contains(type));

    /// <summary>
    /// The count as the game shows it.
    /// <para>
    /// <b>A stack of one is stored as zero.</b> A single Eth Rune in a stack holds
    /// <c>quantity 0</c> in the file while the game's own tooltip says "Quantity: 1". The
    /// file keeps its zero - writing a one where the game wrote a nought is a change
    /// nobody asked for - and only what is shown is raised.
    /// </para>
    /// </summary>
    public static int Shown(int quantity) => Math.Max(1, quantity);

    /// <summary>
    /// Whether this item is one of a kind that stacks only while it is plain: <b>a map</b>. A white map
    /// stacks to fifty; made magic, rare or unique it is one map, and its count must read 1 - the game
    /// uses up the whole item at a click whatever the number says (a magic map
    /// given a quantity of 2 here and loaded, one click consumed it all). Every one of the 150
    /// the game rolled holds 1, and the cube takes them one at a time - its recipes are written for
    /// <c>qty=1</c> and it blocks the rest "due to stackable maps".
    /// </summary>
    public static bool IsOneOnly(D2SLib.Model.Save.Item item) =>
        item.Quality != D2SLib.Model.Save.ItemQuality.Normal
        && IsStackable(item.Code)
        && ItemTypes.LineageOfBase(item.Code).Contains("map", StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// How many more <paramref name="stack"/> takes of its own kind, dropped onto it as the game stacks one onto another:
    /// up to its largest stack, counted by what is shown (a stack of one is stored as zero). Zero where it takes none:
    /// a base the game does not count (arrows, throwing weapons), a magic or rare one, which never stacks, or a full one.
    /// </summary>
    public static int RoomIn(D2SLib.Model.Save.Item stack) =>
        stack.Quality == D2SLib.Model.Save.ItemQuality.Normal && IsCounted(stack.Code) && MaximumFor(stack.Code) is { } most
            ? Math.Max(most - Shown(stack.Quantity), 0)
            : 0;

    /// <summary>
    /// How many of <paramref name="carried"/> go into <paramref name="stack"/> when it is let go of on it: the same base,
    /// stamped alike, each plain and counted - as many as there are, at most the room left (<see cref="RoomIn"/>).
    /// </summary>
    public static int Joining(D2SLib.Model.Save.Item stack, D2SLib.Model.Save.Item carried) =>
        !ReferenceEquals(stack, carried)
        && string.Equals(Key(stack.Code), Key(carried.Code), StringComparison.OrdinalIgnoreCase)
        && carried.Quality == D2SLib.Model.Save.ItemQuality.Normal
        && string.Equals(stack.Version, carried.Version, StringComparison.Ordinal)
            ? Math.Min(Shown(carried.Quantity), RoomIn(stack))
            : 0;

    /// <summary>
    /// The largest stack the game would make, held inside what the field can write. Null
    /// for a base that does not stack.
    /// </summary>
    public static int? MaximumFor(string? code) =>
        Key(code) is { } id && _maxStack.TryGetValue(id, out int most)
            ? Math.Clamp(most, 1, FieldMaximum)
            : null;

    /// <summary>
    /// The smallest stack the table names (<c>minstack</c>), at least one. Null for a base that does not
    /// stack. A key or a rune stack says 1, a quiver of arrows 100.
    /// </summary>
    public static int? MinimumFor(string? code) =>
        Key(code) is { } id && _maxStack.ContainsKey(id)
            ? Math.Clamp(_minStack.GetValueOrDefault(id, 1), 1, MaximumFor(id) ?? FieldMaximum)
            : null;

    private static void ReadBases(
        GameData game, string table,
        Dictionary<string, int> maxStack, Dictionary<string, int> minStack, Dictionary<string, string> typeOf)
    {
        try
        {
            var lines = Rows(game, table);
            if (lines.Count == 0)
                return;

            string[] header = lines[0];
            int code = Array.IndexOf(header, "code");
            int stackable = Array.IndexOf(header, "stackable");
            int most = Array.IndexOf(header, "maxstack");
            int least = Array.IndexOf(header, "minstack");
            int type = Array.IndexOf(header, "type");

            if (code < 0 || stackable < 0)
                return;

            foreach (string[] cells in lines.Skip(1))
            {
                if (cells.Length <= stackable || cells[stackable].Trim() != "1")
                    continue;

                string id = cells[code].Trim();
                if (id.Length == 0)
                    continue;

                maxStack[id] = most >= 0 && cells.Length > most
                               && int.TryParse(cells[most].Trim(), out int parsed) && parsed > 0
                    ? parsed
                    : FieldMaximum;

                if (least >= 0 && cells.Length > least && int.TryParse(cells[least].Trim(), out int fewest) && fewest > 0)
                    minStack[id] = fewest;

                if (type >= 0 && cells.Length > type && cells[type].Trim().Length > 0)
                    typeOf[id] = cells[type].Trim();
            }
        }
        catch (Exception)
        {
            // Without the table nothing is treated as stackable, which shows no quantity
            // rather than a wrong one.
        }
    }

    /// <summary>
    /// The item types whose quantity the game ignores, following <c>Equiv1</c>/<c>Equiv2</c>
    /// the way <c>BodyLoc</c> does - a type inherits its parent's flags, so a throwing base
    /// need not carry <c>Throwable</c> itself.
    /// </summary>
    private static HashSet<string> ReadUncountedTypes(GameData game)
    {
        var uncounted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var lines = Rows(game, "itemtypes");
            if (lines.Count == 0)
                return uncounted;

            string[] header = lines[0];
            int code = Array.IndexOf(header, "Code");
            int throwable = Array.IndexOf(header, "Throwable");
            int quiver = Array.IndexOf(header, "Quiver");
            int first = Array.IndexOf(header, "Equiv1");
            int second = Array.IndexOf(header, "Equiv2");

            if (code < 0)
                return uncounted;

            var rows = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (string[] cells in lines.Skip(1))
            {
                if (cells.Length > code && cells[code].Trim().Length > 0)
                    rows[cells[code].Trim()] = cells;
            }

            string Cell(string[] cells, int column) =>
                column >= 0 && cells.Length > column ? cells[column].Trim() : "";

            bool Unlimited(string type, HashSet<string> walked)
            {
                if (!walked.Add(type) || !rows.TryGetValue(type, out string[]? cells))
                    return false;

                if (Cell(cells, throwable) == "1" || Cell(cells, quiver).Length > 0)
                    return true;

                foreach (int parent in new[] { first, second })
                {
                    string above = Cell(cells, parent);
                    if (above.Length > 0 && Unlimited(above, walked))
                        return true;
                }

                return false;
            }

            foreach (string type in rows.Keys)
            {
                if (Unlimited(type, []))
                    uncounted.Add(type);
            }
        }
        catch (Exception)
        {
            // As above: every stack counted rather than the wrong ones hidden.
        }

        return uncounted;
    }

    private static IReadOnlyList<string[]> Rows(GameData game, string table) => game.Rows(table);
}
