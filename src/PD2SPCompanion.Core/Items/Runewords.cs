using System.Text;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// The runewords, from <c>runes.txt</c>, found by <b>the runes socketed in the item</b>.
/// <para>
/// An item also keeps a twelve-bit <c>RunewordId</c>, which is <b>the row's name key's string
/// index, cut to twelve bits</b> - see <see cref="IdOf"/>. Reading an item, the runes stay the
/// way to find its row: they need no table but this one.
/// </para>
/// <para>
/// The runes need no rule. A runeword keeps them socketed in the order the recipe names, and
/// all three match their row exactly: Nadir <c>r04 r03</c>, Steel <c>r03 r01</c>, Leaf
/// <c>r03 r08</c>. The base settles the few recipes that share a sequence.
/// </para>
/// </summary>
public static class Runewords
{
    /// <summary>
    /// One runeword: what it is called, the runes that make it in order, the bases it may be
    /// made in, and the property rows it grants.
    /// </summary>
    /// <param name="Key">The row's <c>Name</c> - "Runeword60" - which the string table words "Infinity".</param>
    /// <param name="Excluded">
    /// The row's <c>etype</c> columns: types it may not be made in even where an <c>itype</c> takes
    /// them. Wind's melee row excludes staves and throwing weapons, which have rows of their own -
    /// ignored, a Winged Knife's Wind was read as the melee one, which the game does not do
    /// (a level 99 Barbarian: the game wrote it back as "Wind Throwable").
    /// </param>
    public sealed record Word(
        string Name, string[] Runes, string[] Types, IReadOnlyList<RunewordMod> Mods, string Key = "",
        string[]? Excluded = null)
    {
        public string[] ExcludedTypes => Excluded ?? [];
    }

    /// <summary>One line of a runeword's row, in the same shape the other tables use.</summary>
    public sealed record RunewordMod(string Code, int Min, int Max, bool HasRange, int Param);

    private static List<Word> _words = [];

    /// <summary>Every finished runeword this season has, in the table's order.</summary>
    public static IReadOnlyList<Word> All => _words;

    /// <summary>The groups the PD2 wiki files runewords under, in its order.</summary>
    public static readonly IReadOnlyList<string> Categories = [N("Helms"), N("Chests"), N("Shields"), N("Quivers"), N("Weapons")];

    /// <summary>
    /// Which of the wiki's groups a runeword belongs to, worked out from the bases it may be made
    /// in - each <c>itype</c> walked up its parents in <c>itemtypes.txt</c>. A runeword made in
    /// several kinds of base is in several groups, as Spirit is a shield and a weapon.
    /// <para>
    /// Two class types descend only from "class item" and are placed by what they are: a
    /// Paladin item (<c>pala</c>) is his shield, a Sorceress item (<c>sorc</c>) her orb. With
    /// that, the installed S13 table gives Helms 8, Chests 22, Shields 11 and Quivers 3 - the
    /// wiki's own counts - and Weapons 74 against the wiki's 63, which is the wiki's to answer:
    /// the table is what the game makes.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> CategoriesOf(Word word)
    {
        var found = new HashSet<string>();

        foreach (string type in word.Types)
        {
            var line = ItemTypes.Ancestry(type);

            if (line.Contains("helm")) found.Add("Helms");
            if (line.Contains("tors")) found.Add("Chests");
            if (line.Contains("shld") || type == "pala") found.Add("Shields");
            if (line.Contains("misl")) found.Add("Quivers");
            if (line.Contains("weap") || type == "sorc") found.Add("Weapons");
        }

        return [.. Categories.Where(found.Contains)];
    }

    /// <summary>
    /// A runeword's name as the game shows it on the item: the string table's text for the row's key. runes.txt's
    /// "Rune Name" column is a note the game never shows, and PD2's notes are not its names - "Exile's Path" is
    /// Exile, "Doomsayer" Doom, "Widowmaker" Grief, "Bound by Duty" Chains of Honor, "Winter" Voice of Reason,
    /// "The Beast" Beast. The note where no table has the key.
    /// </summary>
    public static string GameName(Word word) =>
        StringTable.Current?.Get(word.Key)?.Trim() is { Length: > 0 } shown && !shown.Equals(word.Key, StringComparison.Ordinal)
            ? shown
            : word.Name;

    /// <summary>
    /// A runeword's name as a list should show it: the game's (<see cref="GameName"/>), with the version the
    /// row's note tacks on put in brackets where two rows share the name: "Echo Weapon" reads <b>Echo (Weapon)</b>,
    /// "Wind Staff" <b>Wind (Staff)</b>.
    /// <para>
    /// The installed S13 table has three such notes - Echo Weapon, Echo Quiver and Wind Staff beside Wind. The
    /// last word counts as a version only when it names what the runeword is made in: one of its own categories,
    /// singular, or one of its base types. Splitting on the last word alone would turn "Ancient's Pledge" into
    /// "Ancient's (Pledge)". The Grail groups its runewords by this name and keeps a find by the group's first
    /// row, so the names must stay as many as the notes were: the brackets keep the two Echoes and the two Winds
    /// apart, as the notes kept them.
    /// </para>
    /// </summary>
    public static string DisplayName(Word word)
    {
        string shown = GameName(word);

        int space = word.Name.LastIndexOf(' ');
        if (space <= 0)
            return shown;

        string last = word.Name[(space + 1)..];

        bool names = CategoriesOf(word).Any(category => category.TrimEnd('s').Equals(last, StringComparison.OrdinalIgnoreCase))
                     || word.Types.Any(type => ItemTypes.NameOf(type).Equals(last, StringComparison.OrdinalIgnoreCase));
        if (!names)
            return shown;

        // Where the table's name carries the version too, it is said once.
        if (shown.EndsWith(" " + last, StringComparison.OrdinalIgnoreCase))
            shown = shown[..^(last.Length + 1)];

        return $"{shown} ({last})";
    }

    /// <summary>
    /// The number an item made with this runeword stores: its key's string index as the game
    /// numbers strings - <c>string.tbl</c> from 0, <c>patchstring.tbl</c> from 10000,
    /// <c>expansionstring.tbl</c> from 20000 - cut to the field's twelve bits. Null when the
    /// string tables do not have the key.
    /// <para>
    /// The three S13 runewords on a played character hold exactly this:
    /// Insight ("Runeword62", <c>expansionstring</c> 568, 20568) holds 88, Peace
    /// ("Runeword98", 20603) holds 123, Purity (<c>patchstring</c> 1461, 11461) holds 3269. It
    /// accounts for every pattern in 969 runewords from the S9 and S12 item packs as well: a
    /// "RunewordNN" key holds NN + 26 below 76 and NN + 25 above 80, which are its place in
    /// <c>expansionstring</c> less 480 (20000 is 480 short of five times 4096), and the words
    /// PD2 renames in its own table - Delirium's classic 2718, Purity, Ferocity - hold their
    /// place in <c>patchstring</c> plus 1808 (10000 less 8192).
    /// </para>
    /// </summary>
    public static int? IdOf(Word word) =>
        StringTable.Current?.IndexOf(word.Key) is { } index ? index & 0xFFF : null;

    /// <summary>The runeword keyed so - "Runeword60" - or null.</summary>
    public static Word? ByKey(string? key) =>
        key is { Length: > 0 } ? _words.FirstOrDefault(word => word.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) : null;

    /// <summary>A rune's short name, "Tal" for the Tal Rune.</summary>
    public static string RuneName(string code) => ItemPresenter.BaseName(code).Replace(" Rune", "");

    /// <summary>The runes a runeword is made of, in order: "Tal + Eth".</summary>
    public static string Recipe(Word word) => string.Join(" + ", word.Runes.Select(RuneName));

    /// <summary>A rune's place in the ladder, El 1 to Zod 33, from its code - <c>r08</c> is 8.</summary>
    public static int RuneNumber(string code) =>
        code.Length >= 3 && code[0] is 'r' or 'R' && int.TryParse(code.AsSpan(1, 2), out int n) ? n : 0;

    /// <summary>
    /// How many of each rune, El to Zod, a set of items holds plus a materials tab - index 1 is
    /// El. A single rune counts one, a stack its quantity - <c>r08s</c>, or in Season 14 <c>r08</c> itself, which
    /// stacks (<see cref="Materials"/>: thirty counted as one would lose twenty-nine); anything socketed is never
    /// among the items given, since it sits inside its parent.
    /// </summary>
    public static int[] Stock(IEnumerable<Item> items, Func<int, int>? tab = null)
    {
        var stock = new int[34];

        foreach (var item in items)
        {
            string code = item.Code?.Trim() ?? "";
            int n = RuneNumber(code);
            if (n is < 1 or > 33)
                continue;

            if (code.Length == 3)
                stock[n] += Stacks.IsStackable(code) ? Stacks.Shown(item.Quantity) : 1;
            else if (code.Length == 4 && code[3] is 's' or 'S')
                stock[n] += Stacks.Shown(item.Quantity);
        }

        // The tab's first 33 slots are the rune stacks, El to Zod - see Materials.
        if (tab is not null)
            for (int n = 1; n <= 33; n++)
                stock[n] += tab(n);

        return stock;
    }

    /// <summary>How many times over a stock holds the runes a runeword needs, repeats counted.</summary>
    public static int Sets(Word word, int[] stock) =>
        word.Runes.GroupBy(RuneNumber).Min(need => need.Key is >= 1 and <= 33 ? stock[need.Key] / need.Count() : 0);

    /// <summary>Whether a stock holds every rune a runeword needs, repeats counted.</summary>
    public static bool CanMake(Word word, int[] stock) =>
        word.Runes.GroupBy(RuneNumber).All(need => need.Key is >= 1 and <= 33 && stock[need.Key] >= need.Count());

    /// <summary>
    /// The runeword this item was made with, or null for one that is not a runeword or whose
    /// runes match no recipe.
    /// </summary>
    public static Word? On(Item item)
    {
        // This was once guarded with `item.Flags is null`, because the flag is a bit in an
        // array that was not always there: every item off a save file carries one, the stand-in
        // unsocketing builds to reserve a gem's room did not (CharacterSession.Sockets),
        // and this was the first code to ask every
        // item whether it is a runeword - which is how it found out, by taking the editor
        // down with it while a rune was being turned out. `Item.Flags` materialises itself
        // now, so the guard moved to where the array lives and every other reader is covered
        // by it, not just this one.
        if (!item.IsRuneword || item.SocketedItems.Count == 0)
            return null;

        string[] runes = [.. item.SocketedItems.Select(gem => gem.Code?.Trim() ?? "")];

        var family = ItemTypes.LineageOfBase(item.Code);

        foreach (var word in _words)
        {
            if (!word.Runes.SequenceEqual(runes, StringComparer.OrdinalIgnoreCase))
                continue;

            // A sequence can belong to more than one recipe, one per base - told apart by the
            // types each takes and the types each excludes.
            if (word.Types.Length > 0 && !word.Types.Any(family.Contains))
                continue;
            if (word.ExcludedTypes.Any(family.Contains))
                continue;

            return word;
        }

        return null;
    }

    public static void Load(GameData game)
    {
        var found = new List<Word>();

        try
        {
            if (game.Lines("runes") is not { } lines)
                return;
            if (lines.Length == 0)
                return;

            string[] header = lines[0].Split('\t');

            int named = Array.IndexOf(header, "Rune Name");
            int keyed = Array.IndexOf(header, "Name");
            int live = Array.IndexOf(header, "complete");

            foreach (string line in lines.Skip(1))
            {
                string[] cells = line.Split('\t');
                if (cells.Length < header.Length)
                    continue;

                // 115 of the 198 rows are finished; the rest are drafts the mod has not
                // turned on, and an item can never have been made from one.
                if (live >= 0 && cells[live].Trim() != "1")
                    continue;

                string[] runes =
                [
                    .. Enumerable.Range(1, 6)
                        .Select(n => Array.IndexOf(header, $"Rune{n}"))
                        .Where(column => column >= 0 && cells.Length > column)
                        .Select(column => cells[column].Trim())
                        .Where(rune => rune.Length > 0)
                ];

                if (runes.Length == 0)
                    continue;

                var mods = new List<RunewordMod>();

                for (int n = 1; n <= 7; n++)
                {
                    int code = Array.IndexOf(header, $"T1Code{n}");
                    int low = Array.IndexOf(header, $"T1Min{n}");
                    int high = Array.IndexOf(header, $"T1Max{n}");
                    int spot = Array.IndexOf(header, $"T1Param{n}");

                    if (code < 0 || low < 0 || high < 0 || cells.Length <= high)
                        continue;
                    if (cells[code].Trim() is not { Length: > 0 } what)
                        continue;

                    bool ranged = int.TryParse(cells[low].Trim(), out int min)
                                  & int.TryParse(cells[high].Trim(), out int max);

                    // Through AffixRanges, because 142 of the 207 parameters in this table
                    // are **names**, not numbers - `T1Param3=Zeal` on Ferocity, `Holy Freeze`
                    // on an aura, `Taunt` on a hit-skill. Parsed as an int they all became 0,
                    // so the source offered parameter 0 while the item held the real skill id,
                    // the two never keyed together, and every one of those properties read as
                    // something nothing on the item accounts for.
                    int param = spot >= 0 && cells.Length > spot
                        ? AffixRanges.ParameterOf(what, cells[spot].Trim())
                        : 0;

                    mods.Add(new RunewordMod(what, min, max, ranged, param));
                }

                if (mods.Count == 0)
                    continue;

                string[] types =
                [
                    .. Enumerable.Range(1, 6)
                        .Select(n => Array.IndexOf(header, $"itype{n}"))
                        .Where(column => column >= 0 && cells.Length > column)
                        .Select(column => cells[column].Trim())
                        .Where(name => name.Length > 0)
                ];

                string[] excluded =
                [
                    .. Enumerable.Range(1, 3)
                        .Select(n => Array.IndexOf(header, $"etype{n}"))
                        .Where(column => column >= 0 && cells.Length > column)
                        .Select(column => cells[column].Trim())
                        .Where(name => name.Length > 0)
                ];

                string name = named >= 0 && cells.Length > named ? cells[named].Trim() : "";

                string key = keyed >= 0 && cells.Length > keyed ? cells[keyed].Trim() : "";

                found.Add(new Word(name, runes, types, mods, key, excluded));
            }
        }
        catch (Exception)
        {
            // Without the table a runeword is an item with sockets and no source, which is
            // what it was before any of this was read.
        }

        _words = found;
    }
}
