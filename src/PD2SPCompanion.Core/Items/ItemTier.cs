using System.Text;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// Whether a base is the normal, exceptional or elite version of itself.
/// <para>
/// <c>weapons.txt</c> and <c>armor.txt</c> give every row the three codes of its own
/// family - <c>normcode</c>, <c>ubercode</c>, <c>ultracode</c> - all pointing at the same
/// trio whichever member you look up. A Katar, a Quhab and a Suwayyah all read
/// <c>ktr/9ar/7ar</c>, so matching the row's own code against the three says which it is.
/// </para>
/// <para>
/// Weapons and armour have tiers, and so do PD2's quivers in <c>misc.txt</c> - Arrows,
/// Arrows2, Arrows3 (<c>aqv/aqv2/aqv3</c>), which its upgrade recipes step through like any
/// base. Any other misc row carries <c>normcode</c> pointing at itself and leaves the other
/// two empty, which is what separates a base with no variants from one that has them - a
/// rune is not "the normal version" of anything.
/// </para>
/// <para>
/// <b>On the naming.</b> Blizzard's data says norm/uber/ultra and the game never shows a
/// tier at all - it is implicit in the base's name, Katar to Quhab to Suwayyah - which is
/// why the category has no obvious label. Normal/Exceptional/Elite is what PD2's own wiki
/// uses, and it parenthesises them exactly as this does: "Item (Normal)". That is the
/// convention followed here.
/// </para>
/// </summary>
public static class ItemTier
{
    public const string Normal = "Normal";
    public const string Exceptional = "Exceptional";
    public const string Elite = "Elite";

    private static Dictionary<string, string> _tierOf = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Each tiered base's family, by its normal tier's code.</summary>
    private static Dictionary<string, string> _familyOf = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Each tiered base's three codes, normal first.</summary>
    private static Dictionary<string, string[]> _trioOf = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The bases that upgrade into a family from outside its three: by the family's normal code. See
    /// <see cref="Before"/>.
    /// </summary>
    private static Dictionary<string, string[]> _beforeOf = new(StringComparer.OrdinalIgnoreCase);

    public static void Load(GameData game)
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var families = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trios = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var pairs = new List<(string Base, string Next)>();

        foreach (string table in new[] { "weapons", "armor", "misc" })
            Read(game, table, found, families, trios, pairs);

        // A family of two - a row that names itself as its normal tier, a tier above, and no elite. The
        // Circlet is the one such base (S13): Circlet, Coronet, nothing. The game's upgrade recipes take a
        // "basic" item - one whose code is its row's normal code - to that row's tier above, so a Circlet is
        // basic and cubes into a Coronet, itself the basic tier of Coronet, Tiara, Diadem: four bases in a
        // line, the first two both normal. (PD2's loot filter says the same of a Circlet: upgrade with the
        // basic recipe. Seen in game.) It is normal, it upgrades, and it is of the
        // family it upgrades into - a set Circlet cubed to a Coronet is still that set piece.
        var before = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, next) in pairs)
        {
            found[id] = Normal;
            trios[id] = [id, next];
            string family = families.GetValueOrDefault(next, next);
            families[id] = family;

            if (!before.TryGetValue(family, out var into))
                before[family] = into = [];
            into.Add(id);
        }

        _tierOf = found;
        _familyOf = families;
        _trioOf = trios;
        _beforeOf = before.ToDictionary(each => each.Key, each => each.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The bases that upgrade into this base's family from outside its three tiers: a Circlet, for a
    /// Coronet, a Tiara or a Diadem. An item of the family may have begun as one of them. Empty for every
    /// family but that one.
    /// </summary>
    public static IReadOnlyList<string> Before(string? code) =>
        code?.Trim() is { Length: > 0 } id && _familyOf.TryGetValue(id, out string? family)
        && _beforeOf.TryGetValue(family, out var into)
            ? into
            : [];

    /// <summary>
    /// The base one tier up - a Katar's Quhab, a Quhab's Suwayyah - or null for an elite, or
    /// for a base with no tiers.
    /// </summary>
    public static string? Next(string? code)
    {
        if (code?.Trim() is not { Length: > 0 } id || !_trioOf.TryGetValue(id, out var trio))
            return null;

        int at = Array.FindIndex(trio, each => each.Equals(id, StringComparison.OrdinalIgnoreCase));
        return at >= 0 && at + 1 < trio.Length ? trio[at + 1] : null;
    }

    /// <summary>A tiered base's three codes, normal first, or null. A family of two - the Circlet's - is its two.</summary>
    public static IReadOnlyList<string>? Family(string? code) =>
        code?.Trim() is { Length: > 0 } id && _trioOf.TryGetValue(id, out var trio) ? trio : null;

    /// <summary>0 for normal, 1 exceptional, 2 elite; null for a base with no tiers.</summary>
    public static int? Rank(string? code) => Of(code) switch
    {
        Normal => 0,
        Exceptional => 1,
        Elite => 2,
        _ => null,
    };

    /// <summary>
    /// Whether two bases are tiers of one family - a War Staff, a Rune Staff and an Archon Staff.
    /// What an upgraded unique is: its row names the base it drops on, and the item has moved up
    /// the family since. The same base counts, of course.
    /// </summary>
    public static bool SameFamily(string? one, string? other)
    {
        string a = one?.Trim() ?? "", b = other?.Trim() ?? "";
        if (a.Length == 0 || b.Length == 0)
            return false;
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase))
            return true;

        return _familyOf.TryGetValue(a, out string? first) && _familyOf.TryGetValue(b, out string? second)
               && first.Equals(second, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The tier of this base, or null where the base has no variants to be one of.
    /// <para>
    /// The code off an item is padded to four characters, so this trims - a lookup with the
    /// raw field finds nothing and says so silently.
    /// </para>
    /// </summary>
    public static string? Of(string? code) =>
        code?.Trim() is { Length: > 0 } id && _tierOf.TryGetValue(id, out string? tier)
            ? tier
            : null;

    private static void Read(GameData game, string table, Dictionary<string, string> found,
                             Dictionary<string, string> families, Dictionary<string, string[]> trios,
                             List<(string Base, string Next)> pairs)
    {
        try
        {
            if (game.Lines(table) is not { } lines)
                return;
            if (lines.Length == 0)
                return;

            string[] header = lines[0].Split('\t');
            int code = Array.IndexOf(header, "code");
            int normal = Array.IndexOf(header, "normcode");
            int exceptional = Array.IndexOf(header, "ubercode");
            int elite = Array.IndexOf(header, "ultracode");

            if (code < 0 || normal < 0 || exceptional < 0 || elite < 0)
                return;

            foreach (string line in lines.Skip(1))
            {
                string[] cells = line.Split('\t');
                if (cells.Length <= elite)
                    continue;

                string id = cells[code].Trim();
                string first = cells[normal].Trim();
                string second = cells[exceptional].Trim();
                string third = cells[elite].Trim();

                // A family of two - itself, a tier above and no elite: joined to the family above once every
                // row is read (Load).
                if (id.Length > 0 && second.Length > 0 && third.Length == 0
                    && id.Equals(first, StringComparison.OrdinalIgnoreCase)
                    && !id.Equals(second, StringComparison.OrdinalIgnoreCase))
                {
                    pairs.Add((id, second));
                }

                // A family, not a row pointing at itself with the rest blank.
                if (id.Length == 0 || second.Length == 0 || third.Length == 0)
                    continue;

                families[id] = first;
                trios[id] = [first, second, third];

                if (id.Equals(first, StringComparison.OrdinalIgnoreCase))
                    found[id] = Normal;
                else if (id.Equals(second, StringComparison.OrdinalIgnoreCase))
                    found[id] = Exceptional;
                else if (id.Equals(third, StringComparison.OrdinalIgnoreCase))
                    found[id] = Elite;
            }
        }
        catch (Exception)
        {
            // Without the table no tier is offered, which says nothing rather than
            // something wrong.
        }
    }
}
