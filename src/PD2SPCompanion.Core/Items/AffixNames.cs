using System.Text;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>One half of a rare item's name, as something to pick.</summary>
public sealed record RareHalf(int Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>One named affix on an item: what it is called, and what it is.</summary>
/// <param name="Grants">
/// What the affix gives, a stat a line, or empty for a row that grants nothing - a rare
/// item's name, or an affix whose properties are computed by function rather than named.
/// </param>
public sealed record AffixName(
    string Kind, string Name, string Grants = "", bool IsRareName = false)
{
    public bool HasGrants => Grants.Length > 0;

    /// <summary>
    /// The tooltip, or null for a row with nothing to say. Null rather than an empty
    /// string: a tooltip whose content is merely empty still opens, as a bare frame.
    /// </summary>
    public string? Tooltip => HasGrants ? Grants : null;
}

/// <summary>
/// What an item's affixes are called.
/// <para>
/// Separate from <see cref="AffixRanges"/>, which reads the same tables for what an affix
/// <i>grants</i>. This is the other half: which rows the item names, so a magic item can say
/// it is Bronze and of Quality rather than 238 and 196.
/// </para>
/// </summary>
public static class AffixNames
{
    private static string[] _prefixes = [];
    private static string[] _suffixes = [];
    private static string[] _rarePrefixes = [];
    private static string[] _rareSuffixes = [];

    /// <summary>The magic tables' own keys, before the string table words them - see <see cref="MagicIdsNamed"/>.</summary>
    private static string[] _prefixKeys = [];
    private static string[] _suffixKeys = [];

    /// <summary>Which item types each rare name half is allowed and refused.</summary>
    private static Allowed[] _rarePrefixTypes = [];
    private static Allowed[] _rareSuffixTypes = [];

    private sealed record Allowed(string[] On, string[] NotOn);

    public static void Load(GameData game)
    {
        (_prefixes, _prefixKeys) = Names(game, "magicprefix");
        (_suffixes, _suffixKeys) = Names(game, "magicsuffix");
        (_rarePrefixes, _) = Names(game, "rareprefix");
        (_rareSuffixes, _) = Names(game, "raresuffix");
        _rarePrefixTypes = Restrictions(game, "rareprefix");
        _rareSuffixTypes = Restrictions(game, "raresuffix");
    }

    /// <summary>
    /// A rare item's own name, the two halves the game joins - "Dread Razor". Null on
    /// anything that is not rare.
    /// <para>
    /// <b>The two ids share one numbering.</b> <c>rareprefix.txt</c> holds 47 rows and a
    /// prefix id of 163 has been seen, so it cannot be an index into that file: the
    /// suffixes are numbered first and the prefixes continue from where they stop. The
    /// offset is therefore the suffix table's own length.
    /// </para>
    /// <para>
    /// <b>Not yet confirmed against a named item.</b> The one rare to hand is unidentified,
    /// and one place either way is the difference between Dread and Blood. Identifying a
    /// rare in game and reading its name settles it in a single edit - see
    /// <see cref="RareNamesAreOneBased"/>.
    /// </para>
    /// </summary>
    public static string? RareNameOf(Item item)
    {
        if (item.Quality != ItemQuality.Rare && item.Quality != ItemQuality.Craft)
            return null;

        string? first = Rare(_rarePrefixes, item.RarePrefixId - _rareSuffixes.Length);
        string? second = Rare(_rareSuffixes, item.RareSuffixId);

        return first is null && second is null
            ? null
            : string.Join(" ", new[] { first, second }
                .Where(part => part is not null)
                .Select(Capitalised));
    }

    /// <summary>
    /// A rare name half as the game writes it. The tables store some of them lowercase -
    /// `Bone` and `coat` - and the game shows "Bone Coat", so the first letter is raised
    /// here rather than left to look like a typo on screen.
    /// </summary>
    private static string Capitalised(string? half) =>
        Shown(half) is { Length: > 0 } word ? char.ToUpperInvariant(word[0]) + word[1..] : "";

    /// <summary>
    /// A rare name half as a player reads it.
    /// <para>
    /// The <c>name</c> column is a <b>key into the string tables</b>, not the words
    /// themselves. Most keys happen to be the word - <c>Bone</c>, <c>coat</c> - which is
    /// why reading the column raw works until it does not: <c>rareprefix</c> row 15 is
    /// <c>PlagueRI</c> and the game shows "Plague". Same lesson as <c>namestr</c> on a
    /// base item.
    /// </para>
    /// </summary>
    private static string Shown(string? half)
    {
        if (half is not { Length: > 0 } key)
            return "";

        return StringTable.Current?.Get(key) is { Length: > 0 } text && text != key
            ? text
            : key;
    }

    /// <summary>
    /// What a magic item is called: its prefix, its base, its suffix - "Crimson Amulet of
    /// Craftsmanship". The base sits in the middle, which is why this needs the name the
    /// item is already showing rather than working it out again.
    /// <para>
    /// A rare is named differently and has <see cref="RareNameOf"/> for it: its two halves
    /// stand alone, with the base named on the line beneath in game.
    /// </para>
    /// </summary>
    public static string? MagicNameOf(Item item, string baseName)
    {
        if (item.Quality != ItemQuality.Magic)
            return null;

        string? first = item.MagicPrefixIds is [var prefix, ..] && prefix > 0
            ? Magic(_prefixes, prefix)
            : null;

        string? second = item.MagicSuffixIds is [var suffix, ..] && suffix > 0
            ? Magic(_suffixes, suffix)
            : null;

        if (first is null && second is null)
            return null;

        return string.Join(" ", new[] { first, baseName, second }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    /// <summary>
    /// The halves offered for an item, narrowed to a family.
    /// <para>
    /// A null family means the item's own, which is the list someone building a rare the
    /// game could have made wants. An empty one means every name there is.
    /// </para>
    /// </summary>
    public static IReadOnlyList<RareHalf> FirstNames(Item item, string? family = null) =>
        Halves(_rarePrefixes, _rarePrefixTypes, _rareSuffixes.Length, item, family);

    public static IReadOnlyList<RareHalf> SecondNames(Item item, string? family = null) =>
        Halves(_rareSuffixes, _rareSuffixTypes, 0, item, family);

    /// <summary>
    /// The families the two tables actually restrict by, so the list offered is the list
    /// that makes a difference rather than every type the game has.
    /// </summary>
    public static IReadOnlyList<RareHalf> Families =>
        [.. _rarePrefixTypes.Concat(_rareSuffixTypes)
            .SelectMany(rule => rule.On)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(code => new RareHalf(0, code))
            .OrderBy(family => ItemTypes.NameOf(family.Name), StringComparer.OrdinalIgnoreCase)];

    /// <summary>The name those two ids make, for showing a choice before it is kept.</summary>
    public static string NameFrom(int first, int second) =>
        string.Join(" ", new[]
        {
            Rare(_rarePrefixes, first - _rareSuffixes.Length),
            Rare(_rareSuffixes, second),
        }.Where(part => part is not null).Select(Capitalised));

    /// <summary>
    /// The halves a rare name can be built from, with the ids the file stores for them.
    /// <para>
    /// The ids follow the same shared numbering the reading does, so a name picked here and
    /// a name read back are the same name. Anything that changes about that numbering
    /// changes both at once.
    /// </para>
    /// </summary>
    private static IReadOnlyList<RareHalf> Halves(
        string[] names, Allowed[] allowed, int offset, Item item, string? chosen)
    {
        bool everything = chosen is { Length: 0 };

        // The item's own: by both of its base's types, as the game asks. A dungeon map is a "t4m", which
        // descends from nothing, and a "t4me" that is a map - asked by the first alone it had no name to
        // be rare under.
        IReadOnlyCollection<string> family = chosen is { Length: > 0 }
            ? ItemTypes.Ancestry(chosen)
            : ItemTypes.LineageOfBase(item.Code);

        return
        [
            .. names
                .Select((name, index) => (Index: index, Name: name))
                .Where(row => row.Name.Length > 0
                              && (everything || Fits(allowed, row.Index, family)))
                .Select(row => new RareHalf(
                    row.Index + (RareNamesAreOneBased ? 1 : 0) + offset, row.Name))
                .OrderBy(half => half.Name, StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>
    /// Whether a name half may sit on this item.
    /// <para>
    /// The tables say it by family rather than by type, so a name allowed on <c>weap</c> is
    /// allowed on a Throwing Knife - which is why this asks the ancestry rather than the
    /// type. A row that names no types at all is taken as allowed anywhere; better to offer
    /// a name the game would refuse than to hide one it would not.
    /// </para>
    /// </summary>
    private static bool Fits(Allowed[] allowed, int index, IReadOnlyCollection<string> family)
    {
        if (index >= allowed.Length)
            return true;

        var rule = allowed[index];

        if (rule.NotOn.Any(family.Contains))
            return false;

        return rule.On.Length == 0 || rule.On.Any(family.Contains);
    }

    private static Allowed[] Restrictions(GameData game, string table)
    {
        try
        {
            if (game.Lines(table) is not { } lines)
                return [];
            if (lines.Length == 0)
                return [];

            string[] header = lines[0].Split('\t');
            int[] on = [.. Enumerable.Range(1, 7)
                .Select(n => Array.IndexOf(header, $"itype{n}"))];
            int[] notOn = [.. Enumerable.Range(1, 4)
                .Select(n => Array.IndexOf(header, $"etype{n}"))];

            return [.. lines.Skip(1).Select(line =>
            {
                string[] cells = line.Split('\t');

                string[] Pick(int[] columns) =>
                [
                    .. columns
                        .Where(column => column >= 0 && cells.Length > column)
                        .Select(column => cells[column].Trim())
                        .Where(type => type.Length > 0),
                ];

                return new Allowed(Pick(on), Pick(notOn));
            })];
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Whether a rare name id counts from one rather than zero. Suffix 7 reading as the
    /// seventh name rather than the eighth is the whole of the question.
    /// </summary>
    public const bool RareNamesAreOneBased = true;

    /// <summary>
    /// A magic affix id is a zero-based index into the data rows, blanks counted - checked
    /// against the items carrying them, not assumed. See <see cref="AffixRanges"/>, which
    /// reads the same rows for what they grant and carries the evidence.
    /// </summary>
    private static string? Magic(string[] names, int id) => At(names, id);

    /// <summary>
    /// Every magic affix id of this name - several rows share one, a level or a group apart. For
    /// an item known by its name alone, such as a trade listing, where the ids are not given.
    /// <para>
    /// By the words the game shows or by the table's own key: the PD2 trade site names affixes by
    /// the key, "Athlete's Coronet" where the game says "Athletic Coronet".
    /// </para>
    /// <para>0, no affix, is among them for the name the site gives an empty slot.</para>
    /// </summary>
    public static IReadOnlyList<int> MagicIdsNamed(bool prefix, string name)
    {
        var (worded, keys) = prefix ? (_prefixes, _prefixKeys) : (_suffixes, _suffixKeys);

        IReadOnlyList<int> named = [.. Enumerable.Range(1, Math.Max(0, worded.Length - 1))
            .Where(id => worded[id].Equals(name, StringComparison.OrdinalIgnoreCase)
                         || (id < keys.Length && keys[id].Equals(name, StringComparison.OrdinalIgnoreCase)))];

        // The trade site words an empty slot - id 0, "none" to the game - as the table's row 0,
        // which in magicsuffix is a real row: a prefix-only amulet is listed "Cunning Amulet of
        // Health". So that name may mean no affix at all, first, since an item that carries
        // none of the row's stat ties with it (the ladder's top 700: all 14 "of
        // Health" magic items lacked damage reduced, 5 were amulets past its level).
        // magicprefix's row 0 is blank, so no prefix name stands in for an empty one.
        bool emptyWorded = worded.Length > 0 && worded[0].Length > 0
            && (worded[0].Equals(name, StringComparison.OrdinalIgnoreCase)
                || (keys.Length > 0 && keys[0].Equals(name, StringComparison.OrdinalIgnoreCase)));

        return emptyWorded ? [0, .. named] : named;
    }

    private static string? Rare(string[] names, int id) =>
        At(names, RareNamesAreOneBased ? id - 1 : id);

    private static string? At(string[] names, int index) =>
        index >= 0 && index < names.Length && names[index].Length > 0
            ? names[index]
            : null;

    /// <returns>Each row's name as the game shows it, and the key it was worded from.</returns>
    private static (string[] Worded, string[] Keys) Names(GameData game, string table)
    {
        try
        {
            if (game.Lines(table) is not { } lines)
                return ([], []);
            if (lines.Length == 0)
                return ([], []);

            string[] header = lines[0].Split('\t');
            int column = Array.IndexOf(header, "Name");
            if (column < 0)
                column = Array.IndexOf(header, "name");
            if (column < 0)
                return ([], []);

            // The file ends with a newline, which splits into one more entry than the
            // table has rows. That entry is not a row, and counting it moved the boundary
            // between the two halves of the rare numbering by one: a Hard Leather Armor
            // the game calls "Bone Coat" came out as "Grim coat". Interior blanks are left
            // alone - the game counts those.
            var rows = lines.Skip(1).ToList();
            while (rows.Count > 0 && rows[^1].Trim().Length == 0)
                rows.RemoveAt(rows.Count - 1);

            // The Name column holds a string key, not the words the game prints, and 197 of
            // them resolve to something else - some to a different word rather than a
            // spelling: `Blank` is shown as "Void", `Assamic` as "Lunar", `of Warmth` as
            // "of Thawing". Found by hovering a glove in game that the editor called
            // "Athlete's" where the game called it "Athletic".
            string[] keys = [.. rows.Select(line =>
            {
                string[] cells = line.Split('\t');
                return cells.Length > column ? cells[column].Trim() : "";
            })];

            return ([.. keys.Select(key => StringTable.Current?.Get(key) is { Length: > 0 } worded ? worded : key)], keys);
        }
        catch (Exception)
        {
            return ([], []);
        }
    }
}
