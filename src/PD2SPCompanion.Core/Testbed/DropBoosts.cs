using System.Text.RegularExpressions;
using PD2SPCompanion.Core.GameFiles;
using PD2SPCompanion.Core.Items;

namespace PD2SPCompanion.Core.Testbed;

/// <summary>What an entry is: one of PD2's special items, a weighted material, or a high rune.</summary>
public enum DropKind
{
    /// <summary>An item PD2's own code drops through an item-less row, one in <c>Unique</c> x <c>Set</c> a kill.</summary>
    Special,

    /// <summary>The same kind of row for a corrupted zone's or a map's extra drop: shards, Tier 1 maps, unique maps.</summary>
    Zone,

    /// <summary>A material by its weight wherever a class lists it: the Worldstone Shard, the three keys.</summary>
    Material,

    /// <summary>A rune, El (#1) to Zod (#33), by its weight in the rune chain.</summary>
    Rune,

    /// <summary>A gem tier - Flawless, Perfect - by the weight of its class wherever a class lists it.</summary>
    Gem,
}

/// <summary>
/// One of an entry's columns: a difficulty ("N", "NM", "H"), an Uber Ancients tier ("T1"-"T3"), or "All" for a
/// special item with one row; the table row it reads - a special row, a difficulty's Act 5 "Good" class
/// (champions' and uniques' drop), or a boss's own class; the kills it counts where not any monster's
/// (<paramref name="Source"/>: "undead", the boss) - or, for a drop counted per pick of a class rather than per kill,
/// where it comes from (<paramref name="From"/>: an Act 5 champion or unique).
/// </summary>
public sealed record DropColumn(string Label, string Row, string? Source = null, string? From = null);

/// <summary>
/// One thing the drop panel boosts, keyed by its item's code (the high runes by theirs: r20 to r33; a zone's row by a
/// name of its own). <paramref name="Name"/> where the item's own name would mislead - the Uber Ancients' three rows
/// are tiers, not one sigil; a zone's row is a drop somewhere - else null. <paramref name="Icon"/>: the item code its
/// picture is, where the key is not one.
/// </summary>
public sealed record DropEntry(string Key, DropKind Kind, IReadOnlyList<DropColumn> Columns, string? Name = null, string? Icon = null);

/// <summary>An entry's odds in one column, as a probability, before and after; null where it does not drop.</summary>
public sealed record DropOdds(string Column, double? Before, double? After, string? Source = null, string? From = null);

/// <summary>What a boost costs the other drops of a class it is listed in: the class, and the share they keep.</summary>
public sealed record DropCost(string Row, double Kept);

/// <summary>
/// The drop panel's table, read once (DropBoosts.Book): its entries, their odds for any multiples, and what the
/// multiples cost - asked again at every change, without reading the archives again.
/// </summary>
public sealed class DropBook
{
    private readonly DropBoosts.Table _table;

    internal DropBook(DropBoosts.Table table)
    {
        _table = table;
        Entries = DropBoosts.Entries(table);
        Unknown = DropBoosts.Unknown(table);
    }

    /// <summary>The special items, the shard and the keys, then Lem to Zod.</summary>
    public IReadOnlyList<DropEntry> Entries { get; }

    /// <summary>Item-less rows with a chance that are neither a known item's nor an event's: new in this season.</summary>
    public IReadOnlyList<string> Unknown { get; }

    /// <summary>Each entry's odds in each column, before and after the multiples given (an entry left out is 1x).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<DropOdds>> Odds(IReadOnlyDictionary<string, int> boosts) =>
        DropBoosts.Odds(_table, Entries, boosts);

    /// <summary>
    /// How much of their chance the runes left at 1x keep with these multiples, given a rune drops from a Hell Act 5
    /// champion or unique: the boosted runes take their share from them - 1 where no rune is boosted.
    /// </summary>
    public double OtherRunesKept(IReadOnlyDictionary<string, int> boosts) => DropBoosts.OtherRunesKept(_table, boosts);

    /// <summary>
    /// The class whose other drops lose the most to the boosted shard, keys or gem tiers, and the share they keep -
    /// null where nothing weighted is boosted. Rune classes are the runes' own cost (<see cref="OtherRunesKept"/>).
    /// </summary>
    public DropCost? WorstCost(IReadOnlyDictionary<string, int> boosts) => DropBoosts.WorstCost(_table, boosts);
}

/// <summary>
/// The drop panel's entries and their odds: PD2's material and high rune drop rates in one panel picker, each entry
/// boosted on its own, the probability of vanilla listed beside the boosted one.
/// <para>
/// <b>Special items</b> - event rows left out. PD2's own code drops each through a <c>treasureclassex</c> row that holds no item, one a difficulty; its
/// chance per kill is one in <c>Unique</c> times <c>Set</c> (0 where it does not drop; see SpecialOdds), and the
/// boost divides <c>Unique</c>. <c>Set</c> is left as it is.
/// </para>
/// <para>
/// <b>The shard and the keys</b> - a weight wherever
/// a class lists them, as the page's material box did; that box leaves them to the panel now. <b>High runes</b> -
/// each rune's weight in the rune chain multiplied, the fall-backs untouched (DropOverride).
/// </para>
/// <para>
/// <b>Odds</b>, exact from the table: the shard and the runes as the drop chance from an Act 5 champion or unique
/// monster - one pick of the difficulty's Act 5 "Good" class, as a reference; a key per kill of its boss in Hell, the only place it drops. Per kill of anything
/// else would need the treasure class upgrades by monster level and PD2's own drop code.
/// </para>
/// </summary>
public static class DropBoosts
{
    /// <summary>
    /// The special rows' items, by the row's name without its difficulty. The rows name no item, so this is
    /// the one thing written down rather than read: which item each row is PD2's code for, by the item's name
    /// (misc.txt) - the Skeleton Key and the two books are PD2's "Unlimited" ones, the Jawbone "Rathma
    /// Ingredient". The four rows of events (Corrupted Zone and its T1 Maps, Unique Map Monsters and MapBoss)
    /// are not here on purpose; a row neither here nor among them is new, and
    /// <see cref="Unknown"/> names it.
    /// <para>
    /// Which kills a row counts (<c>Source</c>; null for any monster), from PD2's patch notes and wiki: the Jawbone undead, Flesh of Malic demons,
    /// the Demonic Insignia Act 5's red portal zones, the Talisman corrupted zones, the sigils map monsters of
    /// their tier - the last four of medium confidence, no rate for them published.
    /// </para>
    /// </summary>
    private static readonly (string Row, string Code, string? Name, string? Source)[] Specials =
    [
        ("Larzuk Puzzlebox", "lbox", null, null),
        ("Larzuk Puzzlepiece", "lpp", null, null),
        ("SkeletonKey", "rkey", null, null),
        ("Horadric ID Book", "rid", null, null),
        ("Horadric TP Book", "rtp", null, null),
        ("Lillith Mirror", "llmr", null, null),
        ("Vial of Lightsong", "lsvl", null, null),
        ("Demonic Cube", "imrn", null, null),
        ("Rathma Jawbone", "rtmo", null, L("undead")),
        ("lucb", "lucb", null, L("Act 5 red portal")),
        ("lucc", "lucc", null, L("corrupted zone")),
        ("lucd", "lucd", null, L("demon")),
        // Three rows, a tier each; the sigil is any of the three (ubaa, ubab, ubac) as far as the row says.
        ("UberAncients", "ubaa", L("Uber Ancients sigils"), L("map monster")),
    ];

    /// <summary>The Treasure Fallen's class a difficulty - before the list that reads it, which is built in order.</summary>
    private static readonly (string Label, string Row)[] Fallen =
        [("N", "Treasure Fallen N"), ("NM", "Treasure Fallen NM"), ("H", "Treasure Fallen H")];

    /// <summary>
    /// The weighted materials besides the shard, each counted per kill of the monster whose class lists it, a
    /// column a difficulty where it has one (the page's material box retired into these): the
    /// keys from their bosses in Hell - the Key of Terror also from Blood Raven, the Key of Hate from the Bloodwitch,
    /// the Key of Destruction from Izual, the panel counting the boss each is farmed from; the essences from their act
    /// bosses (Suffering from Duriel too); the infusions, the map orbs and Scour from the Treasure Fallen; the scarab
    /// from the T4 map bosses (also the Uber Ancients, Shadow of Mendeln, Lucion's chest).
    /// </summary>
    private static readonly (string Code, (string Label, string Row)[] Columns, string Source)[] Weighted =
    [
        ("pk1", [("H", "Countess Item (H)")], L("Countess")),
        ("pk2", [("H", "Summoner (H)")], L("Summoner")),
        ("pk3", [("H", "Nihlathak (H)")], L("Nihlathak")),
        ("tes", Act("Andariel"), L("Andariel")),
        ("ceh", Act("Mephisto"), L("Mephisto")),
        ("bet", Act("Diablo"), L("Diablo")),
        ("fed", Act("Baal"), L("Baal")),
        .. new[] { "crfb", "crfc", "crfs", "crfh", "crfv", "crfu", "crfp", "irma", "irra", "rrra", "urma", "scou" }
            .Select(code => (code, Fallen, L("Treasure Fallen"))),
        ("scrb", [("All", "T4FinalMapBoss")], L("T4 map boss")),
    ];

    /// <summary>An act boss's class a difficulty: "Andariel", "Andariel (N)", "Andariel (H)".</summary>
    private static (string Label, string Row)[] Act(string boss) =>
        [("N", boss), ("NM", $"{boss} (N)"), ("H", $"{boss} (H)")];

    /// <summary>The weighted materials the panel boosts: the shard and those above.</summary>
    public static IReadOnlyList<string> MaterialKeys { get; } = ["wss", .. Weighted.Select(material => material.Code)];

    /// <summary>
    /// The switch that leaves chipped and flawed gems out of the champions' and uniques' drops:
    /// kept with the multiples, on at any value over 1, so presets and the install hold it as they do them.
    /// </summary>
    public const string LowGemsOut = "gems-out";

    /// <summary>The champions' and uniques' drop classes the switch reaches: every act's in every difficulty, and the maps'.</summary>
    private static readonly Regex GoodClass = new(@"^(Act [1-5]( \((N|H)\))? Good|Map Good t[1-3])$", RegexOptions.IgnoreCase);

    /// <summary>Whether this item of this class is one the switch leaves out.</summary>
    internal static bool LeftOut(IReadOnlyDictionary<string, int>? boosts, string className, string item) =>
        Of(boosts, LowGemsOut) > 1 && GoodClass.IsMatch(className)
        && (item.Equals("Chipped Gem", StringComparison.OrdinalIgnoreCase) || item.Equals("Flawed Gem", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The gem tiers the panel boosts, flawless and perfect only: the class a
    /// champion's or unique's drop lists for the tier, its entry, its picture (the amethyst of the tier) and its name.
    /// </summary>
    private static readonly (string Class, string Key, string Icon, string Name)[] Gems =
    [
        ("Flawless Gem", "gem-flawless", "gzv", L("Flawless gems")),
        ("Perfect Gem", "gem-perfect", "gpv", L("Perfect gems")),
    ];

    /// <summary>
    /// The item-less rows for a place's extra drop, taken in once corrupted zones had been seen in single player
    /// - read as drops, not events:
    /// Worldstone Shards and Tier 1 maps from corrupted-zone kills (which row is which rests on their names), unique
    /// maps from map monsters and from map bosses.
    /// </summary>
    private static readonly (string Row, string Key, string Icon, string Name, string Source)[] Zones =
    [
        ("Corrupted Zone", "zone-wss", "wss", L("Worldstone Shards in corrupted zones"), L("corrupted zone")),
        ("Corrupted Zone T1 Maps", "zone-t1", "t11", L("Tier 1 maps in corrupted zones"), L("corrupted zone")),
        ("Unique Map Monsters", "map-unique", "t31", L("Unique maps from map monsters"), L("map monster")),
        ("Unique Map MapBoss", "map-boss", "t31", L("Unique maps from map bosses"), L("map boss")),
    ];

    /// <summary>The Act 5 "Good" classes the shard and the runes are counted from, a difficulty each.</summary>
    private static readonly (string Label, string Row)[] Good =
        [("N", "Act 5 Good"), ("NM", "Act 5 (N) Good"), ("H", "Act 5 (H) Good")];

    private static readonly Regex Column = new(@"^(?<row>.*?)\s?(?<column>NM|N|H|T[1-3])$");

    internal static readonly Regex RuneClass = new("^Runes [0-9]+( equiv)?$", RegexOptions.IgnoreCase);

    internal static readonly Regex RuneCode = new("^r(?<n>[0-9]{2})s?$", RegexOptions.IgnoreCase);

    /// <summary>The installed table, read once, for the panel to ask its entries and their odds of.</summary>
    public static DropBook Book(GameData game) => new(Read(game));

    /// <summary>The boost an entry has: 1 where none is given.</summary>
    public static int Of(IReadOnlyDictionary<string, int>? boosts, string key) =>
        boosts is not null && boosts.TryGetValue(key, out int boost) && boost > 1 ? boost : 1;

    /// <summary>
    /// A special row's chance, one in <c>Unique</c> x <c>Set</c>, made <paramref name="boost"/> times as likely:
    /// <c>Unique</c> lowered first, and <c>Set</c> once <c>Unique</c> is 1 - so the Demonic Cube's 1000 x 1000 at
    /// 1000x is 1 x 1000. Only ever lowered (the research: PD2 may read these columns as 16 bits, and
    /// the Mirror's Normal 75000 is already past them); nought stays nought, nothing under 1. A blank <c>Set</c>
    /// (<paramref name="set"/> null) stays blank unless it has to take the rest.
    /// </summary>
    public static (int Unique, int? Set) Divided(int unique, int? set, int boost)
    {
        if (unique <= 0 || boost <= 1)
            return (unique, set);

        double target = (double)unique * (set ?? 1) / boost;
        double first = Math.Round(target / (set ?? 1), MidpointRounding.AwayFromZero);
        if (first >= 1)
            return ((int)first, set);

        return (1, Math.Max(1, (int)Math.Round(target, MidpointRounding.AwayFromZero)));
    }

    /// <summary>Which entry a special or a zone's row is, by its name - null for any other row.</summary>
    internal static string? SpecialKeyOf(string row)
    {
        foreach (var zone in Zones)
        {
            if (zone.Row.Equals(row, StringComparison.OrdinalIgnoreCase))
                return zone.Key;
        }

        string named = Split(row).Row;
        foreach (var (special, code, _, _) in Specials)
        {
            if (special.Equals(named, StringComparison.OrdinalIgnoreCase) || special.Equals(row, StringComparison.OrdinalIgnoreCase))
                return code;
        }
        return null;
    }

    /// <summary>
    /// Which weighted entry an item code is - a high rune (r20 to r33) or one of <see cref="MaterialKeys"/>, its
    /// stacked form too ("wsss") - or null. <paramref name="inRunes"/>: whether the class is in the rune chain,
    /// the only place a high rune's weight is raised.
    /// </summary>
    internal static string? WeightedKeyOf(string code, bool inRunes)
    {
        if (RuneCode.Match(code) is { Success: true } rune)
            return inRunes && int.Parse(rune.Groups["n"].Value) is >= 1 and int n ? $"r{n}" : null;

        foreach (var gem in Gems)
        {
            if (!inRunes && code.Equals(gem.Class, StringComparison.OrdinalIgnoreCase))
                return gem.Key;
        }

        foreach (string key in MaterialKeys)
        {
            if (code.Equals(key, StringComparison.OrdinalIgnoreCase) || code.Equals(key + "s", StringComparison.OrdinalIgnoreCase))
                return key;
        }
        return null;
    }

    // ---- the table, as far as the panel reads it ---------------------------------------------------------

    internal sealed record Row(string Name, int Picks, string Unique, string Set, int NoDrop, IReadOnlyList<(string Item, int Prob)> Items);

    internal sealed record Table(IReadOnlyDictionary<string, Row> Rows, IReadOnlyList<string> Order);

    private static Table Read(GameData game)
    {
        var (table, _) = game.ReadExcel("treasureclassex");
        int name = table.IndexOfColumn("Treasure Class"), picks = table.IndexOfColumn("Picks"),
            unique = table.IndexOfColumn("Unique"), set = table.IndexOfColumn("Set"), noDrop = table.IndexOfColumn("NoDrop");
        var slots = Enumerable.Range(1, 10)
            .Select(n => (Item: table.IndexOfColumn($"Item{n}"), Prob: table.IndexOfColumn($"Prob{n}")))
            .Where(slot => slot.Item >= 0 && slot.Prob >= 0)
            .ToList();

        static string Cell(string[] cells, int at) => at >= 0 && at < cells.Length ? cells[at].Trim() : "";

        var rows = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (string[] cells in table.Rows)
        {
            string named = Cell(cells, name);
            if (named.Length == 0 || rows.ContainsKey(named))
                continue;

            var items = slots
                .Where(slot => Cell(cells, slot.Item).Length > 0)
                .Select(slot => (Cell(cells, slot.Item), int.TryParse(Cell(cells, slot.Prob), out int prob) ? prob : 0))
                .ToList();

            rows[named] = new Row(named,
                int.TryParse(Cell(cells, picks), out int count) ? count : 1,
                Cell(cells, unique), Cell(cells, set),
                int.TryParse(Cell(cells, noDrop), out int nothing) ? nothing : 0, items);
            order.Add(named);
        }

        return new Table(rows, order);
    }

    internal static IReadOnlyList<DropEntry> Entries(Table table)
    {
        var entries = new List<DropEntry>();

        foreach (var (special, code, named, source) in Specials)
        {
            var columns = table.Order
                .Select(name => table.Rows[name])
                .Where(row => row.Items.Count == 0)
                .Select(row => (Row: row, Split: Split(row.Name)))
                .Where(each => each.Split.Row.Equals(special, StringComparison.OrdinalIgnoreCase)
                               || each.Row.Name.Equals(special, StringComparison.OrdinalIgnoreCase))
                .Select(each => new DropColumn(
                    each.Row.Name.Equals(special, StringComparison.OrdinalIgnoreCase) ? "All" : each.Split.Column,
                    each.Row.Name, source))
                .ToList();

            if (columns.Count > 0)
                entries.Add(new DropEntry(code, DropKind.Special, columns, named));
        }

        // A place's extra drops: one row each, every difficulty.
        foreach (var (row, key, icon, name, source) in Zones)
        {
            if (table.Rows.ContainsKey(row))
                entries.Add(new DropEntry(key, DropKind.Zone, [new DropColumn("All", row, source)], name, icon));
        }

        // The shard from the Act 5 champions and uniques, a difficulty each; the keys from their bosses in Hell.
        if (Good.Any(good => table.Rows.ContainsKey(good.Row)))
            entries.Add(new DropEntry("wss", DropKind.Material,
                [.. Good.Select(good => new DropColumn(good.Label, good.Row, From: L("an Act 5 Champion or Unique monster")))]));
        foreach (var (code, columns, source) in Weighted)
        {
            if (columns.Any(column => table.Rows.ContainsKey(column.Row)))
                entries.Add(new DropEntry(code, DropKind.Material, [.. columns.Select(column => new DropColumn(column.Label, column.Row, source))]));
        }

        // The gem tiers, counted as the runes are: from an Act 5 champion or unique.
        foreach (var (gemClass, key, icon, name) in Gems)
        {
            if (table.Rows.ContainsKey(gemClass))
                entries.Add(new DropEntry(key, DropKind.Gem, [.. Good.Select(good => new DropColumn(good.Label, good.Row))], name, icon));
        }

        // El to Zod: every rune the chain holds, its picture and name by its code.
        var runes = table.Rows.Values
            .Where(row => RuneClass.IsMatch(row.Name))
            .SelectMany(row => row.Items)
            .Select(item => RuneCode.Match(item.Item))
            .Where(match => match.Success)
            .Select(match => int.Parse(match.Groups["n"].Value))
            .Distinct()
            .Order();

        foreach (int rune in runes)
            entries.Add(new DropEntry($"r{rune}", DropKind.Rune, [.. Good.Select(good => new DropColumn(good.Label, good.Row))], Icon: $"r{rune:00}"));

        return entries;
    }

    internal static IReadOnlyList<string> Unknown(Table table)
    {
        var known = Specials.Select(special => special.Row).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. table.Rows.Values
            .Where(row => row.Items.Count == 0 && (row.Unique.Length > 0 || row.Set.Length > 0))
            .Select(row => row.Name)
            .Where(name => SpecialKeyOf(name) is null && !known.Contains(Split(name).Row))];
    }

    /// <summary>A row's name split into the item's and the column: "Larzuk Puzzlebox NM", "lucbH", "UberAncients T2".</summary>
    private static (string Row, string Column) Split(string name) =>
        Column.Match(name) is { Success: true } found && found.Groups["row"].Value.Length > 0
            ? (found.Groups["row"].Value, found.Groups["column"].Value)
            : (name, "All");

    // ---- the odds ----------------------------------------------------------------------------------------

    internal static IReadOnlyDictionary<string, IReadOnlyList<DropOdds>> Odds(
        Table table, IReadOnlyList<DropEntry> entries, IReadOnlyDictionary<string, int> boosts)
    {
        var none = new Dictionary<string, int>();
        var odds = new Dictionary<string, IReadOnlyList<DropOdds>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            odds[entry.Key] = [.. entry.Columns.Select(column =>
            {
                if (entry.Kind is DropKind.Special or DropKind.Zone)
                    return SpecialOdds(table, column, Of(boosts, entry.Key));

                string item = ItemOf(entry);
                double before = Expected(table, column.Row, item, none, []);
                double after = Expected(table, column.Row, item, boosts, []);
                return new DropOdds(column.Label, before > 0 ? before : null, after > 0 ? after : null, column.Source, column.From);
            })];
        }
        return odds;
    }

    /// <summary>What the tables call a weighted entry's item: a rune by its code ("r01"), a gem tier by its class, a material by its code.</summary>
    private static string ItemOf(DropEntry entry) => entry.Kind switch
    {
        DropKind.Rune => entry.Icon ?? entry.Key,
        DropKind.Gem => Gems.FirstOrDefault(gem => gem.Key == entry.Key).Class ?? entry.Key,
        _ => entry.Key,
    };

    /// <summary>
    /// A special row's chance per kill: one in <c>Unique</c> times <c>Set</c>, a blank <c>Set</c> one - PD2's
    /// published rates are exactly that (Lilith's Mirror in Hell 25000 x 1000, "1:25m"; the Jawbone 2250 x 10,
    /// "1:22.5k"), and its Season 9 notes moved these drops "from hardcode to softcode" for single player to mod.
    /// <c>Unique</c> 0 is no drop there.
    /// </summary>
    private static DropOdds SpecialOdds(Table table, DropColumn column, int boost)
    {
        if (!table.Rows.TryGetValue(column.Row, out var row) || !int.TryParse(row.Unique, out int unique) || unique <= 0)
            return new DropOdds(column.Label, null, null, column.Source);

        int? set = int.TryParse(row.Set, out int second) && second > 0 ? second : null;
        var (lowered, setLowered) = Divided(unique, set, boost);
        return new DropOdds(column.Label, 1.0 / ((double)unique * (set ?? 1)), 1.0 / ((double)lowered * (setLowered ?? 1)), column.Source);
    }

    /// <summary>
    /// How many of this item one draw of a class gives, on average, with the multiples given - as DropOverride
    /// writes them: a class's picks over its weights and its no-drop (counted at one player), a negative pick count
    /// the guaranteed list (each item its Prob times, up to that many), nested classes followed down. Not counted:
    /// the six items a kill can drop at most, and the treasure class upgrades by monster level - neither reaches
    /// the classes the panel counts from. A small expected count is the chance itself.
    /// </summary>
    private static double Expected(Table table, string name, string code, IReadOnlyDictionary<string, int> boosts, Dictionary<string, double> known)
    {
        if (known.TryGetValue(name, out double done))
            return done;
        known[name] = 0;

        if (!table.Rows.TryGetValue(name, out var row) || row.Items.Count == 0)
            return 0;

        bool inRunes = RuneClass.IsMatch(name);
        // The item itself, or its stacked form: the rune chain lists every rune so ("r05s", "r33s").
        double Of((string Item, int Prob) item) =>
            item.Item.Equals(code, StringComparison.OrdinalIgnoreCase) || item.Item.Equals(code + "s", StringComparison.OrdinalIgnoreCase)
                ? 1
                : table.Rows.ContainsKey(item.Item) ? Expected(table, item.Item, code, boosts, known) : 0;

        double count = 0;
        if (row.Picks < 0)
        {
            int left = -row.Picks;
            foreach (var item in row.Items)
            {
                if (left <= 0)
                    break;
                int taken = Math.Min(Math.Max(item.Prob, 0), left);
                count += taken * Of(item);
                left -= taken;
            }
        }
        else
        {
            double Weight((string Item, int Prob) item) =>
                LeftOut(boosts, name, item.Item) ? 0
                : WeightedKeyOf(item.Item, inRunes) is { } key ? item.Prob * (double)DropBoosts.Of(boosts, key)
                : item.Prob;

            double whole = row.Items.Sum(Weight) + row.NoDrop;
            if (whole > 0)
                count = Math.Max(1, row.Picks) * row.Items.Sum(item => Weight(item) / whole * Of(item));
        }

        known[name] = count;
        return count;
    }

    // ---- what it costs -----------------------------------------------------------------------------------

    internal static double OtherRunesKept(Table table, IReadOnlyDictionary<string, int> boosts)
    {
        // Within the rune chain Hell's champions and uniques enter - given a rune drops - so it is the runes' own
        // cost: a boosted shard takes from every rune alike, and says so on its own header (WorstCost).
        if (!table.Rows.TryGetValue("Act 5 (H) Good", out var good)
            || good.Items.FirstOrDefault(item => RuneClass.IsMatch(item.Item)).Item is not { } hell)
            return 1;

        var none = new Dictionary<string, int>();
        double before = 0, after = 0;
        for (int rune = 1; rune <= DropOverride.LastRune; rune++)
        {
            if (Of(boosts, $"r{rune}") > 1)
                continue;
            before += Expected(table, hell, $"r{rune:00}", none, []);
            after += Expected(table, hell, $"r{rune:00}", boosts, []);
        }
        return before > 0 ? after / before : 1;
    }

    internal static DropCost? WorstCost(Table table, IReadOnlyDictionary<string, int> boosts)
    {
        DropCost? worst = null;
        foreach (var row in table.Rows.Values)
        {
            if (row.Picks < 0 || RuneClass.IsMatch(row.Name))
                continue;

            double before = row.NoDrop, after = row.NoDrop;
            bool raised = false;
            foreach (var item in row.Items)
            {
                before += item.Prob;
                int boost = WeightedKeyOf(item.Item, inRunes: false) is { } key ? Of(boosts, key) : 1;
                after += item.Prob * (double)boost;
                raised |= boost > 1;
            }

            if (raised && after > 0 && before / after is var kept && (worst is null || kept < worst.Kept))
                worst = new DropCost(row.Name, kept);
        }
        return worst;
    }
}
