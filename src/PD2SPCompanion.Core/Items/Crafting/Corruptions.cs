using System.Text;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>What a corruption granted: a property code and the range it rolled in.</summary>
/// <param name="Param">
/// The recipe's parameter for this property. Where a part of the property is written from
/// it - a cold or poison length - nothing else can supply that part.
/// </param>
public sealed record CorruptionGrant(string Code, int Min, int Max, int Param = 0);

/// <summary>
/// Which property on an item came from corrupting it.
/// <para>
/// A corrupted item stores <c>corrupted</c> - <b>the id of the corruption, not its
/// effect</b> - and the effect is whatever <c>cubemain.txt</c> says that id grants for this
/// item's type. The game marks the resulting line with a red asterisk; this is the lookup
/// behind doing the same.
/// </para>
/// <para>
/// <b>The key is the id and the type together.</b> Of the 77 ids, 43 mean different things
/// on different slots: id 3 is <c>att 150-250</c> on a weapon, <c>100-150</c> on a belt or
/// a ring and <c>50-100</c> on a quiver. The id alone answers the wrong question.
/// </para>
/// </summary>
public static class Corruptions
{
    /// <summary>The corruption that gives sockets rather than a property.</summary>
    public const int Sockets = 32;

    /// <summary>
    /// Not an outcome we carry - a row of the corruption sequence that turns the item into
    /// something else entirely. Kept while the bands are measured, then dropped.
    /// </summary>
    private const int NotACorruption = -1;

    /// <summary>
    /// Whether a grant is only the dye - worded nowhere in the corruption panel, which is about
    /// what an outcome does; the tint shows on the item and in its property row.
    /// </summary>
    public static bool IsTint(string code) =>
        AffixRanges.StatsWritten(code) is [var only] && string.Equals(only, Dyes.Stat, StringComparison.OrdinalIgnoreCase);

    /// <summary>The Worldstone Shard, and the Corrupted one Annihilus is corrupted with.</summary>
    public const string PlainShard = "wss", AnnihilusShard = "cwss";

    /// <summary>
    /// The shard this item is corrupted with - the Corrupted Worldstone Shard for Annihilus,
    /// the plain one for everything else - or null where nothing corrupts it.
    /// </summary>
    public static string? ShardFor(Item item) =>
        Available(item, asIfClean: true).FirstOrDefault()?.Shard;

    /// <summary>The stat holding the id, and the flag that says the item is corrupted.</summary>
    public const string IdStat = "corrupted";
    public const string FlagStat = "corruptor";

    /// <summary>
    /// How likely an outcome is, which the wiki lists as low, mid and high rarity.
    /// <para>
    /// It is not a column: it is <b>the width of the band that selects it</b>. A glove's
    /// outcomes come in bands of 45, 30 and 19, and the sockets one takes 295 on its own.
    /// Since the roll is 1-1000 and the first band wins, the width <i>is</i> the chance -
    /// so rolling straight off the table already has the right odds.
    /// </para>
    /// </summary>
    public enum Rarity { Sockets, Low, Mid, High }

    /// <summary>
    /// One outcome: the id it stamps, the top of the roll band that selects it, what the
    /// item must be for it to apply, and what it grants.
    /// </summary>
    public sealed record CorruptionOption(
        int Id,
        int Band,
        string Wants,
        IReadOnlyList<string> Conditions,
        IReadOnlyList<CorruptionGrant> Grants)
    {
        /// <summary>How wide the band is, which is how likely it is out of a thousand.</summary>
        public int Width { get; init; }

        /// <summary>
        /// The shard the recipe takes: <c>wss</c>, the Worldstone Shard everything else is
        /// corrupted with, or <c>cwss</c>, the Corrupted Worldstone Shard only Annihilus takes.
        /// </summary>
        public string Shard { get; init; } = PlainShard;

        public Rarity Tier { get; init; } = Rarity.Low;

        /// <summary>
        /// How much of the run's mod space this outcome takes: the share **among
        /// modifiers**, which is the number the wiki prints - a glove's three tiers read
        /// 48, 32 and 20. Kept because it is how the wiki's tables can be checked against
        /// ours, though the panel no longer shows it.
        /// </summary>
        public int Space { get; init; }

        /// <summary>
        /// How likely this outcome is, <b>out of the whole 1-to-1000 roll</b>.
        /// <para>
        /// The same denominator the group headings use, which is the point. Measured
        /// against its own pool instead, a socket outcome read 48% under a heading that
        /// said 25% and eight modifiers read 6% each under one that said 24% - each figure
        /// true, and the two together saying nothing. Out of the whole roll the children
        /// add up to their heading, at the price of smaller numbers.
        /// </para>
        /// </summary>
        public string Chance => $"{Width * 100.0 / Height:0.#}%";

        /// <summary>
        /// The top of the roll this outcome is a band of: 1000 on gear, <b>3000 on a map</b> - the recipe
        /// that sets the roll says so (<see cref="HeightFor"/>).
        /// </summary>
        public int Height { get; init; } = Thousand;

        /// <summary>The one outcome that is not a property.</summary>
        public bool GivesSockets => Id == Sockets;

        /// <summary>The socket count it bores, which on some rows is a range.</summary>
        public (int Least, int Most) Holes =>
            Grants.FirstOrDefault(g => g.Code.Equals("sock", StringComparison.OrdinalIgnoreCase))
                is { } sock
                ? (sock.Min, sock.Max)
                : (0, 0);

        /// <summary>How many sockets it bores at most, where that is what it does.</summary>
        public int SocketsGiven => Holes.Most;

        /// <summary>
        /// What it does, worded the way the game words a property and carrying the range it
        /// rolls in - which is how the wiki lists them and how the game prints its brackets.
        /// </summary>
        public string Readable
        {
            get
            {
                if (GivesSockets)
                {
                    // A white item's row reads sock 1-6 - one roll inside the range, capped
                    // by what the base can hold - where a rare weapon's names an exact
                    // count. Printing the range's top for both would promise six sockets on
                    // something that rolls one.
                    var (least, most) = Holes;
                    return (least, most) switch
                    {
                        (0, 0) => "sockets",
                        var (a, b) when a == b => L($"+{b} sockets"),
                        var (a, b) => L($"+{a}-{b} sockets"),
                    };
                }

                // Each stat's words, and the range of a stat worded plainly: a description group rolling one range is
                // said once below - "All Resistances", where the four resistances read one by one.
                var said = new List<(string Stat, string Words)>();
                var spans = new Dictionary<string, (int Min, int Max)>(StringComparer.OrdinalIgnoreCase);

                // Not the tint: Annihilus's every outcome carries it, and a list saying it five
                // times says nothing. The property row still shows it.
                foreach (var grant in Grants.Where(grant => !IsTint(grant.Code)))
                {
                    // A damage property names no stat of its own and is worded by its
                    // function instead - otherwise the commonest corruption a weapon has
                    // reads as "dmg%".
                    var stats = AffixRanges.StatsOf(grant.Code);
                    if (stats.Count == 0 && AffixRanges.WordingFor(grant.Code) is { } worded)
                        stats = [worded];

                    foreach (string stat in stats.DefaultIfEmpty(grant.Code))
                    {
                        // The part's own range, not the row's - see AffixRanges.RangeFor.
                        var span = AffixRanges.RangeFor(
                            grant.Code, stat, grant.Min, grant.Max, grant.Param);

                        // As in the affix list: a parameter that picks the wording is read
                        // before the stat's own string, which is only the first of its set.
                        if (StatText.ByParameter(stat, span.Min, grant.Param) is { } picked)
                        {
                            said.Add((stat, span.Min == span.Max
                                ? picked
                                : $"{picked} [{span.Min} - {span.Max}]"));

                            continue;
                        }

                        string words = StatText.DescribeRange(
                            stat, span.Min, span.Max, SkillNames.NameOf(grant.Param)) ?? stat;

                        // Some forms carry no number of their own - the game composes them
                        // elsewhere - and a corruption read as "Enhanced Maximum Damage"
                        // with no range says half of what it is.
                        // Not a tint, which names its colour and has no number to show.
                        // Nor a flag: "Monsters have Cannot Be Frozen" is one bit, said or not said.
                        if (!words.Any(char.IsDigit) && !string.Equals(stat, Dyes.Stat, StringComparison.OrdinalIgnoreCase)
                            && StatFields.CeilingOf(stat) != 1)
                            words += span.Min == span.Max ? $" {span.Min}" : $" [{span.Min} - {span.Max}]";

                        said.Add((stat, words));
                        spans[stat] = (span.Min, span.Max);
                    }
                }

                // A whole group as one line, where its first member was.
                var lines = new List<string>();
                var groups = StatText.GroupedRanges(spans);
                var saidOnce = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (stat, words) in said)
                {
                    if (groups.FirstOrDefault(group => group.Stats.Contains(stat, StringComparer.OrdinalIgnoreCase)) is { Text: { } whole } group)
                    {
                        if (saidOnce.Add(whole))
                            lines.Add(whole);
                        continue;
                    }

                    lines.Add(words);
                }

                return lines.Count > 0 ? string.Join(", ", lines) : Summary;
            }
        }

        /// <summary>What it does, in the property codes the table uses.</summary>
        public string Summary => GivesSockets
            ? SocketsGiven > 0 ? L($"{SocketsGiven} sockets") : "sockets"
            : string.Join(", ", Grants.Select(g =>
                g.Min == g.Max ? $"{g.Code} {g.Min}" : $"{g.Code} {g.Min}-{g.Max}"));
    }

    /// <summary>
    /// A corruption worked out but not yet written: which one, the numbers it would put on
    /// the item, and the sockets it would bore. Held while it is being looked at, so the
    /// values shown are the values kept.
    /// </summary>
    public sealed record CorruptionDraft(
        CorruptionOption Option,
        IReadOnlyList<AffixRanges.Wrote> Values,
        int Sockets)
    {
        /// <summary>What <c>corruptor</c> takes beside the id (<see cref="FlagFor(Item, CorruptionOption, Random?)"/>).</summary>
        public int Flag { get; init; } = 1;

        /// <summary>What it will do, in the game's own words.</summary>
        public IReadOnlyList<string> Lines =>
            Sockets > 0
                ? [L($"{Sockets} sockets")]
                : [.. Values.Where(v => !string.Equals(v.Stat, Dyes.Stat, StringComparison.OrdinalIgnoreCase)).Select(v =>
                    StatText.Describe(
                        v.Stat, v.Value, SkillNames.NameOf(StatText.SkillOf(v.Stat, v.SkillId, v.Param)),
                        v.SkillLevel, v.MaxCharges)
                    ?? $"{v.Stat} {v.Value}")];
    }

    /// <summary>
    /// Every outcome the cube can roll, <b>in table order</b>, whatever it is for.
    /// <para>
    /// Not grouped by input, because the roll is not. A no-socket weapon's ladder is built
    /// from four different inputs in turn - <c>weap,nos</c> for the brick at 250, then
    /// <c>bow,nos</c> or the generic <c>weap,nos</c> rows for the socket counts, then
    /// <c>weap,nos</c> again for the modifiers from 525 up - and the bands climb straight
    /// through the lot. Grouping by input cut that ladder into pieces and handed a bow the
    /// four socket rows of <c>bow,nos</c> as its entire fortune.
    /// </para>
    /// </summary>
    private static List<CorruptionOption> _ladder = [];

    private static Dictionary<(int Id, string Type), CorruptionGrant[]> _grants = [];

    /// <summary>
    /// Every property on an item. A simple item carries no list at all, which is a null
    /// rather than an empty one.
    /// </summary>
    private static IEnumerable<ItemStat> Properties(Item item) =>
        item.StatLists is { } lists
            ? lists.SelectMany(list => list.Stats ?? [])
            : [];

    /// <summary>
    /// Whether this item has been corrupted at all: the corruption's id, or the roll's mark alone. A shard
    /// spent to no effect counts (<see cref="IsSpent"/>) - the game prints "Corrupted" on such an item and
    /// takes no other shard, so it is corrupted, with nothing to show for it: a spent
    /// shard still counts as corrupted.
    /// </summary>
    public static bool IsCorrupted(Item item) =>
        Properties(item)
            .Any(stat => string.Equals(stat.Stat, FlagStat, StringComparison.OrdinalIgnoreCase)
                         || string.Equals(stat.Stat, IdStat, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether the item is corrupted with nothing: the roll's mark (<c>corruptor</c>) and no corruption
    /// beside it. The game sets the roll on the first transmute and looks for the band it falls in on the
    /// second; where it falls in none the item is left like this, and "not rolled yet" - the guard on the
    /// first - turns every later shard away. **Two dungeons in three end so**: their ten outcomes are bands
    /// of 100 up to 1000 on a roll of 3000 (31 of the fifty tried). The editor may still give such an item a corruption in the mark's
    /// place, or take the mark off - and the game then takes a shard again (seen in game).
    /// </summary>
    public static bool IsSpent(Item item) =>
        Properties(item).Any(stat => string.Equals(stat.Stat, FlagStat, StringComparison.OrdinalIgnoreCase))
        && !Properties(item).Any(stat => string.Equals(stat.Stat, IdStat, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether Lucion's desecration has touched this item - PD2's second corruption, from S10.
    /// The installed <c>itemstatcost.txt</c> carries it as a pair shaped like corruption's own:
    /// <c>desecrated</c> (206) and <c>desecrator</c> (207), against <c>corrupted</c> (360) and
    /// <c>corruptor</c> (361). Either one counts, as either counts for corruption.
    /// </summary>
    public static bool IsDesecrated(Item item) =>
        Properties(item)
            .Any(stat => string.Equals(stat.Stat, "desecrated", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(stat.Stat, "desecrator", StringComparison.OrdinalIgnoreCase));

    /// <summary>The corruption's id, or null on an item that has not been corrupted.</summary>
    public static int? IdOn(Item item) =>
        Properties(item)
            .FirstOrDefault(stat => string.Equals(stat.Stat, IdStat, StringComparison.OrdinalIgnoreCase))
            ?.Value;

    /// <summary>
    /// What the corruption on this item granted, or empty where it granted no property -
    /// which is every uncorrupted item, and a corrupted one whose corruption was sockets.
    /// <para>
    /// The item's own type answers before its parents, because a throwing weapon is both a
    /// <c>thro</c> and a <c>weap</c> and the two rows differ.
    /// </para>
    /// </summary>
    public static IReadOnlyList<CorruptionGrant> On(Item item) =>
        IdOn(item) is { } id ? Carried(item, id) : [];

    /// <summary>
    /// What corruption <paramref name="id"/> gave this item: this build's grant, unless the item carries none of it and an
    /// earlier build of the season gave one it does carry (<see cref="LoadEarlier"/>) - a corruption made before a patch
    /// re-dealt it. This build's answers first, and where none of them fits it still answers.
    /// </summary>
    private static IReadOnlyList<CorruptionGrant> Carried(Item item, int id)
    {
        var now = GrantsFor(item, id);
        if (_earlier.Count == 0 || now.Count > 0 && Carries(item, now))
            return now;

        foreach (var earlier in _earlier)
        {
            var then = Lookup(earlier, item, id);
            if (then.Count > 0 && Carries(item, then))
                return then;
        }

        return now;
    }

    /// <summary>Whether the item holds every stat the grants write; a grant that writes none - sockets - is no evidence either way.</summary>
    private static bool Carries(Item item, IReadOnlyList<CorruptionGrant> grants)
    {
        var held = Properties(item).Select(stat => stat.Stat).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return grants.All(grant => AffixRanges.StatsWritten(grant.Code) is not { Count: > 0 } written || written.Any(held.Contains));
    }

    /// <summary>
    /// What corruption <paramref name="id"/> grants this item, by its row for the item's unique, code or
    /// nearest type - <see cref="On"/>'s answer for any id, not only the one on the item.
    /// </summary>
    public static IReadOnlyList<CorruptionGrant> GrantsFor(Item item, int id) => Lookup(_grants, item, id);

    private static IReadOnlyList<CorruptionGrant> Lookup(Dictionary<(int Id, string Type), CorruptionGrant[]> grants, Item item, int id)
    {
        // What a cube recipe names as its input is not always an item type. Most are -
        // weap, tors, helm, thro - but a ring is named by its base code, `rin`, and the
        // Annihilus by its name. So the unique's own name is tried first, then the item's
        // code, then its types, nearest first: a throwing weapon is both a `thro` and a
        // `weap` and the two rows grant different amounts.
        if (ItemPresenter.QualityIndex(item) is { Length: > 0 } named
            && grants.TryGetValue((id, named), out var byName))
        {
            return byName;
        }

        if (item.Code?.Trim() is { Length: > 0 } code
            && grants.TryGetValue((id, code), out var byCode))
        {
            return byCode;
        }

        foreach (string step in ItemTypes.LineageOfBase(item.Code))
        {
            if (grants.TryGetValue((id, step), out var found))
                return found;
        }

        return [];
    }

    /// <summary>
    /// Every corruption this item could take, in the order the roll walks them.
    /// <para>
    /// The recipe's input names both what the item must be and the conditions it must
    /// meet - <c>weap,nos,nor</c> is a weapon with no sockets of normal quality - and all
    /// of them are checked, so the list is the one the cube would actually choose from.
    /// </para>
    /// </summary>
    public static IReadOnlyList<CorruptionOption> Available(Item item, bool asIfClean = false)
    {
        // What the item would be with its corruption taken off. It matters because the
        // conditions read the item: a weapon whose corruption *was* its sockets has
        // sockets now, so asking what it can take answers with the socketed rows - and
        // the socket outcomes go missing from a list whose whole purpose is to say what
        // could be chosen instead.
        // As if clean means with none, whatever an affix gives: the row a socket corruption stands on is found by this
        // list (On), and without it a Jeweler's beside the corruption read as sockets from nowhere. Apply holds the count.
        int sockets = asIfClean && IdOn(item) == Sockets ? 0 : item.TotalNumberOfSockets;

        var heads = new HashSet<string>(Heads(item), StringComparer.OrdinalIgnoreCase);
        var rows = new List<CorruptionOption>();

        // Turned away before any roll: a white map with a shard is a recipe of its own that does
        // nothing ("map,nor" + wss, op 30), standing before the one that sets the roll.
        if (_refused.Any(row => heads.Contains(row.Wants) && Fits(item, row, sockets)))
            return [];

        int height = HeightFor(heads);

        // The bands are cumulative and the first one the roll falls inside wins, so a row
        // whose band is no higher than one already passed can never be reached. That is how
        // a white item is held to sockets: its own two rows run 500 and 1000, covering the
        // thousand between them, and every modifier row below is dead.
        int reached = 0;

        foreach (var row in _ladder)
        {
            if (reached >= height)
                break;

            if (!heads.Contains(row.Wants) || !Fits(item, row, sockets))
                continue;

            if (row.Band <= reached)
                continue;

            rows.Add(row with { Width = row.Band - reached, Height = height });
            reached = row.Band;
        }

        Tier(rows);
        rows.RemoveAll(row => row.Id == NotACorruption);
        return rows;
    }

    /// <summary>The roll the cube makes on gear, and so the height that ladder climbs to.</summary>
    internal const int Thousand = 1000;

    /// <summary>How far a map's corruption rolls (cubemain's <c>corruptnum 1 3000</c>).</summary>
    internal const int MapRoll = 3000;

    /// <summary>The tops the installed cube's recipes set, each once, lowest first: <c>pd2spc seasonfacts</c>'s line for the rolls.</summary>
    internal static IReadOnlyList<int> RollHeights() => [.. _heights.Values.Distinct().Order()];

    /// <summary>
    /// How high the roll goes for an item: what the recipe that sets it says, by the first of the item's
    /// names and types a recipe is written for. <b>A map's is 3000</b> - "map" + wss sets
    /// <c>corruptnum 1 3000</c> where every piece of gear's sets 1 to 1000 - and its eleven outcomes are
    /// bands of 270, 9% each, the last thirty of the three thousand making it a unique map. Read as a
    /// thousand, the ladder stopped at the fourth outcome and gave each 27%.
    /// <para>
    /// A dungeon is a map too, and its ten outcomes are bands of 100 up to 1000: by the table two rolls
    /// in three land in none. Shown as the table has it, 3.3% each; whether the game agrees is open.
    /// </para>
    /// </summary>
    private static int HeightFor(IEnumerable<string> heads) =>
        heads.Select(head => _heights.TryGetValue(head, out int height) ? height : 0).FirstOrDefault(height => height > 0)
            is > 0 and var found ? found : Thousand;

    private static Dictionary<string, int> _heights = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Items a shard does nothing to, by a recipe that says so (<c>op 30</c>): kept as bare rows to ask <see cref="Fits"/> of.</summary>
    private static List<CorruptionOption> _refused = [];

    /// <summary>Whether a code is one of the two shards that corrupt.</summary>
    public static bool IsShard(string code) =>
        code.Equals(PlainShard, StringComparison.OrdinalIgnoreCase) || code.Equals(AnnihilusShard, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What the cube would give this item, rolled the way it rolls: a number from 1 to
    /// 1000, and the first band it falls inside. Null when nothing applies. With
    /// <paramref name="asIfClean"/>, asked of the item as its own corruption would leave it once
    /// cleaned - as <see cref="Available"/> - for a roll that takes that corruption's place.
    /// </summary>
    public static CorruptionOption? Roll(Item item, Random random, bool asIfClean = false)
    {
        var open = Available(item, asIfClean);
        if (open.Count == 0)
            return null;

        // Weighted by band width, which is what the cube's own 1-to-1000 amounts to - but
        // rolled across the outcomes that are kept rather than across the whole thousand.
        // A quarter of the real roll turns the item into a random rare, and an editor that
        // bricked what it was asked to improve would be a poor joke. Sockets stay in: a
        // max-socket corruption is a result somebody wants.
        int space = open.Sum(option => Math.Max(1, option.Width));
        int rolled = random.Next(1, space + 1);

        foreach (var option in open)
        {
            rolled -= Math.Max(1, option.Width);
            if (rolled <= 0)
                return option;
        }

        return open[^1];
    }

    /// <summary>
    /// Works out what a corruption would do to this item without touching it. Each property
    /// rolls once inside its range, and every stat that property names takes that roll -
    /// which is how a real one lands. A null <paramref name="random"/> takes the lowest.
    /// </summary>
    public static CorruptionDraft Draft(Item item, CorruptionOption option, Random? random)
    {
        var values = new List<AffixRanges.Wrote>();

        foreach (var grant in option.Grants)
        {
            if (grant.Code.Equals("sock", StringComparison.OrdinalIgnoreCase))
                continue;

            // A packed property's two numbers are not an order, so the top is not
            // necessarily the larger - and Next would throw on the pair rather than roll it.
            int rolled = random is null || grant.Max <= grant.Min
                ? grant.Min
                : random.Next(grant.Min, grant.Max + 1);
            values.AddRange(
                AffixRanges.Written(grant.Code, grant.Min, grant.Max, rolled, grant.Param));
        }

        int sockets = 0;
        if (option.GivesSockets && option.SocketsGiven > 0)
        {
            // The recipe says how many, the base says how many it can hold, and the smaller
            // wins: the game will not bore a fourth into something that takes three. Where
            // the recipe names a range - which is every white item's row - it is rolled
            // first and then held to the base.
            var (least, most) = option.Holes;
            int wanted = random is null || least >= most ? most : random.Next(least, most + 1);

            int room = ItemBases.MaximumSockets(item);
            sockets = Math.Max(1, room > 0 ? Math.Min(wanted, room) : wanted);
        }

        return new CorruptionDraft(option, values, sockets) { Flag = FlagFor(item, option, random) };
    }

    /// <summary>
    /// What <c>corruptor</c> holds once an outcome is written, as the cube leaves it: it rolls 1 to 1000 into the stat
    /// (<c>corruptnum 1 1000</c>) and the winning row adds 1001 (<c>corruptnum 1001</c>) - so in Season 14's twelve-bit
    /// field, 1001 plus a roll inside the outcome's band. PhysSpear's Atma's Scarab, corrupted in game: 1470, a roll of
    /// 469 in the amulet's fire resistance band 431-475. In Season 13's one bit - a legacy item's, by its
    /// stamp - it is 1. A map's roll runs to 3000 and its row adds 3001, held at the field's 4095: SeqBarb's magic map, 79,
    /// 3374 - a roll of 373 in its band 271-540 - and its two rare ones, 85 and 87, whose bands run past 1094, both 4095
    /// (corrupted in game). A null <paramref name="random"/> takes the band's top.
    /// </summary>
    public static int FlagFor(Item item, CorruptionOption option, Random? random)
    {
        int ceiling = StatFields.CeilingFor(item, FlagStat);
        if (option.Height is not (Thousand or MapRoll) || ceiling < 2 * Thousand)
            return 1;

        int high = option.Band, low = Math.Max(1, option.Band - Math.Max(1, option.Width) + 1);
        return Math.Min(ceiling, option.Height + 1 + (random is null || low >= high ? high : random.Next(low, high + 1)));
    }

    /// <summary><see cref="FlagFor(Item, CorruptionOption, Random?)"/> for an outcome named by its id; 1 where none of the item's has it.</summary>
    public static int FlagFor(Item item, int id, Random? random) =>
        Available(item, asIfClean: true).FirstOrDefault(option => option.Id == id) is { } option ? FlagFor(item, option, random) : 1;

    /// <summary>Corrupts an item, rolling as it goes. The one-step form of the two above.</summary>
    public static void Apply(Item item, CorruptionOption option, Random? random) =>
        Apply(item, Draft(item, option, random));

    /// <summary>
    /// Writes a draft onto the item: stamps the id, raises the flag, and puts the numbers
    /// where they go.
    /// </summary>
    public static void Apply(Item item, CorruptionDraft draft)
    {
        // Never two corruptions on one item, which is the game's own rule - the winning
        // recipe stamps corruptnum past the end of the roll so it cannot happen twice.
        item.StatLists.FirstOrDefault()?.Stats.RemoveAll(stat =>
            string.Equals(stat.Stat, IdStat, StringComparison.OrdinalIgnoreCase)
            || string.Equals(stat.Stat, FlagStat, StringComparison.OrdinalIgnoreCase));

        var list = ItemStats.WriteValues(item, draft.Values);

        if (draft.Sockets > 0)
        {
            // Never below what the item's own row or affix gives.
            item.TotalNumberOfSockets = (byte)Math.Max(draft.Sockets, SocketsWithout(item));
            item.IsSocketed = true;
        }

        Add(list, IdStat, draft.Option.Id);
        Add(list, FlagStat, draft.Flag);

        // WriteValues sorted the list; the pair came after it.
        ItemStats.InIdOrder(item);
    }

    /// <summary>What a clean did to one property: what it held, and what it holds now.</summary>
    public sealed record CleanedStat(string Stat, int Was, int Now, bool Removed);

    /// <summary>
    /// Takes the corruption off an item, and rerolls what is left.
    /// <para>
    /// The split cannot be recovered. A glove holding <c>item_armor_percent 67</c> was 16
    /// plus a corruption of 51, and would read exactly the same had it been 17 plus 50 -
    /// the save stores the sum and nothing else. An earlier version of this constrained the
    /// original from both ends and rolled inside what was left, which is the most that can
    /// be inferred, but it still hands back a number that is not the one the item had.
    /// </para>
    /// <para>
    /// So cleaning is <b>a reroll with a price</b> instead - but only of what the
    /// corruption touched. The properties it wrote are rolled again from whatever sources
    /// remain, and everything else on the item is left exactly as it stands. Rolling the
    /// whole item was the first attempt, and it meant taking a corruption off a ring also
    /// moved numbers the corruption never went near.
    /// </para>
    /// <para>
    /// A property the corruption alone granted has no source left once it is gone, so it is
    /// removed rather than rolled down to nothing.
    /// </para>
    /// </summary>
    public static IReadOnlyList<CleanedStat> Clean(Item item, Random? random)
    {
        var told = new List<CleanedStat>();

        if (item.StatLists.FirstOrDefault() is not { } list)
            return told;

        // What the corruption wrote, which is all a clean is entitled to move. Asked
        // before the corruption is taken off, because afterwards there is nothing to ask.
        var removed = On(item);

        // The desecration is told from the corruption beside it - a max resists desecration on
        // a max resists corruption is 4 split 2 and 2 - so it is told now, while the corruption
        // is still there to tell it by, and kept. Asked afterwards, 4 fitted no mod and the
        // clean took the desecration's share too.
        Desecrations.Pin(item);

        // Sockets the corruption bored close up again - down to what the item's own sources give, not to none
        // (else an affix's sockets go with a socket corruption changed for another).
        if (IdOn(item) == Sockets)
        {
            int left = SocketsWithout(item);
            item.TotalNumberOfSockets = (byte)left;
            item.IsSocketed = left > 0;
            // Extended items only. On a compact one this bit is a quest flag, not a count.
            if (!item.IsCompact)
                item.NumberOfSocketedItems = (byte)Math.Min(item.NumberOfSocketedItems, left);
        }

        // Off first, so nothing counts the corruption among the sources that remain.
        list.Stats.RemoveAll(stat =>
            string.Equals(stat.Stat, IdStat, StringComparison.OrdinalIgnoreCase)
            || string.Equals(stat.Stat, FlagStat, StringComparison.OrdinalIgnoreCase));

        var cleaned = TakeOff(item, removed, random, withCorruption: false);

        // A flat damage as the base holds it (DamageKinds): what every writer of an item ends on.
        DamageKinds.Settle(item);
        return cleaned;
    }

    /// <summary>
    /// The sockets an item keeps once a socket corruption is off: what its own row or a socket affix gives
    /// (<see cref="AffixRanges.SocketSpan"/> - the most of a range, the save keeping no roll under the
    /// corruption's count), held to what the base takes; none with no such source. A socket a quest bored
    /// (Larzuk's) leaves no trace on the item, and goes with the corruption's.
    /// </summary>
    public static int SocketsWithout(Item item) =>
        AffixRanges.SocketSpan(item) is { } own ? Math.Min(own.Most, Math.Max(ItemBases.MaximumSockets(item), 0)) : 0;

    /// <summary>
    /// Takes a mod's share off every line it wrote, the marker stats already gone. A corruption's
    /// clean asks it without the corruption; a desecration's clean with it, since the corruption
    /// stays.
    /// <para>
    /// <b>Subtracted, not rerolled</b>, wherever that can be done: what is left must lie between
    /// the line less the most the mod could have added and the line less the least, and inside
    /// what the item's other sources roll - and it is rolled inside both. Rolling the line again
    /// from the other sources alone lost any share with no range behind it: a crafted amulet's
    /// resists, the recipe's plus an affix's, came back as the affix's alone (24 to 9, against
    /// a range of 13-21), and where only the recipe gave the line it kept the mod's share
    /// (17 against 5-10). Measured on the market's desecrated amulets.
    /// </para>
    /// <para>
    /// Rolled again, as before, where the two do not meet - an edited item, or a line whose
    /// field is full (Cannot Be Frozen is one bit, so two grants of it hold 1) - and taken off
    /// where nothing else grants it.
    /// </para>
    /// <para>
    /// <b>Known where it can be told</b> (the Forge's sweep): a grant writing several lines with one roll -
    /// all resistances, enhanced damage's two halves - left a line nothing else grants, and that line holds its roll
    /// exactly; the lines it shares come off by that number, and the corruption under a desecration keeps the 19 it
    /// had rather than a new roll of 17. Where it cannot be told, the lines one roll wrote are rolled again as one: the
    /// two halves of enhanced damage came back 35 and 32.
    /// </para>
    /// </summary>
    public static IReadOnlyList<CleanedStat> TakeOff(Item item, IReadOnlyList<CorruptionGrant> removed, Random? random, bool withCorruption)
    {
        random ??= new Random();
        var told = new List<CleanedStat>();

        // A set piece's mods are on its own list, and its green lists count only with enough of the set worn: neither
        // lines to leave room beside nor sources of what is left. Answered across them, a corruption's line over a green
        // line of the same number stayed for good, with nothing granting it.
        bool ownListOnly = item.Quality == ItemQuality.Set;
        var (others, held, packed) = AffixRanges.Reroll(item, random, withCorruption, ownListOnly ? AffixRanges.RollScope.Own : AffixRanges.RollScope.All);
        var left = ownListOnly ? AffixRanges.OwnOf(item) : AffixRanges.For(item);
        IEnumerable<ItemStat> Lines() => ownListOnly ? ItemStats.Home(item).Stats : ItemStats.Every(item);

        // What the mod could have added to each line, keyed as the item's lines are.
        var share = new Dictionary<string, (int Min, int Max)>(StringComparer.OrdinalIgnoreCase);
        foreach (var grant in removed)
        {
            foreach (string stat in AffixRanges.StatsWritten(grant.Code))
            {
                var range = AffixRanges.RangeFor(grant.Code, stat, grant.Min, grant.Max, grant.Param);
                string key = AffixRanges.KeyOf(stat, grant.Param);
                share[key] = share.TryGetValue(key, out var had)
                    ? (had.Min + range.Min, had.Max + range.Max)
                    : (range.Min, range.Max);
            }
        }

        var touched = removed.SelectMany(grant => AffixRanges.StatsWritten(grant.Code))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Each key's one grant, where one alone writes it - and that grant's roll where a line of it is the grant's
        // alone (nothing left grants it) and holds a number its range allows: the share of every line it wrote.
        var grantOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int at = 0; at < removed.Count; at++)
        {
            foreach (string stat in AffixRanges.StatsWritten(removed[at].Code))
            {
                string key = AffixRanges.KeyOf(stat, removed[at].Param);
                grantOf[key] = grantOf.ContainsKey(key) ? -1 : at;
            }
        }

        var knownRoll = new Dictionary<int, int>();
        for (int at = 0; at < removed.Count; at++)
        {
            var grant = removed[at];
            foreach (string stat in AffixRanges.StatsWritten(grant.Code))
            {
                string key = AffixRanges.KeyOf(stat, grant.Param);
                var range = AffixRanges.RangeFor(grant.Code, stat, grant.Min, grant.Max, grant.Param);
                if (grantOf.GetValueOrDefault(key, -1) == at && !left.ContainsKey(key) && range.Max > range.Min
                    && Lines().FirstOrDefault(line => string.Equals(AffixRanges.KeyOf(line), key, StringComparison.OrdinalIgnoreCase)) is { } alone
                    && alone.Value >= range.Min && alone.Value <= range.Max)
                {
                    knownRoll[at] = alone.Value;
                    break;
                }
            }
        }

        // One draw for the lines one grant wrote alike, so its halves stay one roll.
        var drawn = new Dictionary<(int Grant, int Low, int High), int>();

        // Over the item's own lines rather than over names, so a stat carried twice for two
        // different subjects - Halaberd's Reign holds item_singleskill for Battle Cry and
        // again for Battle Orders - is answered line by line instead of both lines taking
        // whichever one the name found first. Lines sharing a key are one property split in
        // two - The Iron Jang Bong carries its faster cast rate as 50 and 10 - and are answered
        // as their sum, the change going on the first, which is the one a corruption adds to.
        var carrying = Lines()
            .Where(stat => stat.Stat is { Length: > 0 } had && touched.Contains(had))
            .GroupBy(stat => AffixRanges.KeyOf(stat), StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var lines in carrying)
        {
            var carried = lines.First();
            string name = carried.Stat!;
            string key = lines.Key;

            // What the other lines of it hold, which the first has to leave room for.
            int beside = packed.ContainsKey(key) ? 0 : lines.Skip(1).Sum(line => line.Value);
            int total = carried.Value + beside;

            // A field holds so much: Cannot Be Frozen is one bit. A full one says nothing of how
            // much was poured into it, so what is left may be anything the rest can roll.
            int top = StatFields.CeilingFor(item, name);

            // The mod's share off, where the line is a plain number and something else grants it.
            if (!packed.ContainsKey(key)
                && share.TryGetValue(key, out var part)
                && left.TryGetValue(key, out var rest))
            {
                int restMin = Math.Min(rest.Min, top), restMax = Math.Min(rest.Max, top);
                int low = total >= top ? restMin : Math.Max(restMin, total - part.Max);
                int high = total >= top ? restMax : Math.Min(restMax, total - part.Min);

                int grantAt = grantOf.GetValueOrDefault(key, -1);

                // Its grant's roll known: the line less exactly that, where what is left is still a number the rest gives.
                if (grantAt >= 0 && knownRoll.TryGetValue(grantAt, out int mine) && total < top
                    && total - mine >= restMin && total - mine <= restMax && total - mine - beside is var exact && exact > 0)
                {
                    if (exact != carried.Value)
                        told.Add(new CleanedStat(name, carried.Value, exact, Removed: false));

                    carried.Value = exact;
                    continue;
                }

                int draw = 0;
                if (low <= high && !drawn.TryGetValue((grantAt, low, high), out draw))
                    drawn[(grantAt, low, high)] = draw = random.Next(low, high + 1);

                if (low <= high && draw - beside is var kept && kept > 0)
                {
                    if (kept != carried.Value)
                        told.Add(new CleanedStat(name, carried.Value, kept, Removed: false));

                    carried.Value = kept;
                    continue;
                }
            }

            if (others.TryGetValue(key, out int rolled))
            {
                rolled = Math.Min(rolled, top);
                if (rolled - beside > 0)
                    rolled -= beside;

                if (rolled != carried.Value)
                    told.Add(new CleanedStat(name, carried.Value, rolled, Removed: false));

                carried.Value = rolled;

                // What remains may be a packed property, whose number means nothing without
                // the skill beside it. The corruption leaving must not take that with it.
                if (packed.TryGetValue(AffixRanges.KeyOf(carried), out var parts))
                {
                    carried.SkillId = parts.SkillId;
                    carried.SkillLevel = parts.SkillLevel;
                    carried.MaxCharges = parts.MaxCharges;
                }

                continue;
            }

            // Nothing left grants it. Either the corruption was its only source, or its
            // property has no range and keeping what is there is the honest answer.
            if (held.Contains(name))
                continue;

            told.Add(new CleanedStat(name, carried.Value, 0, Removed: true));
            ItemStats.Remove(item, carried);
        }

        return told;
    }

    /// <summary>
    /// Works out how likely each outcome on a ladder is, and which of the three tiers it
    /// belongs to.
    /// <para>
    /// The widths are measured where the ladder is built, and since the roll is 1-1000 and
    /// the first band wins, <b>the width is the chance</b>. The widths fall into plain
    /// groups; where a slot leads with one outsized band - a glove's Enhanced Defense takes
    /// 29.5% on its own - that band belongs with the commonest tier rather than being a
    /// tier of its own, so only the last three groups are kept.
    /// </para>
    /// <para>
    /// Checked against the wiki's own glove table: eight, eight and eight, the same
    /// modifiers in the same columns.
    /// </para>
    /// </summary>
    private static void Tier(List<CorruptionOption> rows)
    {
        int to = rows.Count;

        // Two pools, measured apart, because the wiki gives two sets of odds and each is a
        // share of its own: a modifier against the other modifiers - 48, 32 and 20 for the
        // three tiers - and a socket count against the other socket counts, 48, 28 and 24.
        int mods = 0;
        int holes = 0;

        for (int n = 0; n < to; n++)
        {
            if (rows[n].Id == NotACorruption)
                continue;

            if (rows[n].GivesSockets)
                holes += rows[n].Width;
            else
                mods += rows[n].Width;
        }

        for (int n = 0; n < to; n++)
            rows[n] = rows[n] with { Space = rows[n].GivesSockets ? holes : mods };

        int groups = 0;
        int widest = 0;

        for (int n = 0; n < to; n++)
        {
            if (rows[n].GivesSockets || rows[n].Id == NotACorruption)
                continue;

            if (widest == 0 || rows[n].Width < widest * 0.8)
            {
                widest = rows[n].Width;
                groups++;
            }
        }

        int skip = Math.Max(0, groups - 3);
        int step = -1;
        widest = 0;

        for (int n = 0; n < to; n++)
        {
            if (rows[n].Id == NotACorruption)
                continue;

            if (rows[n].GivesSockets)
            {
                rows[n] = rows[n] with { Tier = Rarity.Sockets };
                continue;
            }

            if (widest == 0 || rows[n].Width < widest * 0.8)
            {
                widest = rows[n].Width;
                step++;
            }

            rows[n] = rows[n] with
            {
                Tier = (step - skip) switch
                {
                    <= 0 => Rarity.Low,
                    1 => Rarity.Mid,
                    _ => Rarity.High,
                },
            };
        }
    }

    /// <summary>Two outcomes' grants under one id, as the range either could have written.</summary>
    private static CorruptionGrant[] Widen(IReadOnlyList<CorruptionGrant> one, IReadOnlyList<CorruptionGrant> other)
    {
        var codes = one.Concat(other).Select(grant => (grant.Code, grant.Param)).Distinct().ToList();
        return
        [
            .. codes.Select(code =>
            {
                var a = one.FirstOrDefault(grant => grant.Code == code.Code && grant.Param == code.Param);
                var b = other.FirstOrDefault(grant => grant.Code == code.Code && grant.Param == code.Param);
                return a is null || b is null
                    ? new CorruptionGrant(code.Code, 0, (a ?? b)!.Max, code.Param)
                    : new CorruptionGrant(code.Code, Math.Min(a.Min, b.Min), Math.Max(a.Max, b.Max), code.Param);
            }),
        ];
    }

    /// <summary>A marker stat onto a list - the corruption's pair, or the desecration's.</summary>
    public static void AddStat(ItemStatList list, string name, int value) => Add(list, name, value);

    private static void Add(ItemStatList list, string name, int value)
    {
        if (StatCatalogue.IdOf(name) is not { } id)
            return;

        list.Stats.Add(new ItemStat { Id = id, Stat = name, Value = value });
    }

    /// <summary>
    /// What a recipe might have named this item as, most specific first: its own base code,
    /// then its type and the types above it.
    /// </summary>
    private static IEnumerable<string> Heads(Item item)
    {
        // A recipe may name one unique outright - "Annihilus" + cwss - which is the unique's
        // own `index`, not its base: Annihilus is a small charm, and no charm row takes cwss.
        if (ItemPresenter.QualityIndex(item) is { Length: > 0 } named)
            yield return named;

        if (item.Code?.Trim() is { Length: > 0 } code)
            yield return code;

        foreach (string step in ItemTypes.LineageOfBase(item.Code))
            yield return step;
    }

    /// <summary>
    /// Whether an item meets a recipe's conditions. <c>nos</c> and <c>sock</c> are about
    /// sockets; the rest name a quality.
    /// </summary>
    private static bool Fits(Item item, CorruptionOption option, int sockets) => Meets(item, option.Conditions, sockets);

    /// <summary>Whether an item meets the conditions a recipe's input lists after its type: <c>nos</c>, <c>mag</c>, <c>uni</c>.</summary>
    public static bool Meets(Item item, IEnumerable<string> conditions, int sockets)
    {
        foreach (string condition in conditions)
        {
            bool ok = condition switch
            {
                "nos" => sockets == 0,
                "sock" => sockets > 0,
                "nor" => item.Quality == ItemQuality.Normal,
                "hiq" => item.Quality == ItemQuality.Superior,
                "low" => item.Quality == ItemQuality.Inferior,
                "mag" => item.Quality == ItemQuality.Magic,
                "rar" => item.Quality == ItemQuality.Rare,
                "set" => item.Quality == ItemQuality.Set,
                "uni" => item.Quality == ItemQuality.Unique,
                "crf" => item.Quality is ItemQuality.Craft or ItemQuality.Tempered,

                // A condition nobody here understands is not a reason to offer the row.
                _ => false,
            };

            if (!ok)
                return false;
        }

        return true;
    }

    /// <summary>The stats those grants write, which is what a page marks.</summary>
    public static IReadOnlyList<string> StatsOn(Item item) =>
        [.. On(item)
            .SelectMany(grant => AffixRanges.StatsWritten(grant.Code))
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Whether the corruption on the item gave it sockets - the game's red <c>*</c> on "Socketed (n)".</summary>
    public static bool GaveSockets(Item item) =>
        On(item).Any(grant => grant.Code.Equals("sock", StringComparison.OrdinalIgnoreCase));


    public static void Load(GameData game)
    {
        var read = Read(game.Lines("cubemain"));
        _grants = read.Grants;
        _heights = read.Heights;
        _refused = read.Refused;
        _ladder = read.Ladder;
        _earlier = [];
    }

    /// <summary>
    /// The corruptions an earlier build of the season gave, newest first: each build's cubemain, which the season's snapshot
    /// keeps where a patch changed it (<c>SeasonSnapshot.KeepEarlierBuilds</c>). A patch re-dealt corruption outcomes -
    /// Half Freeze on boots, gloves and belts from id 34 to 92, a helm's or a chest's curse duration to curse
    /// effectiveness - and the items corrupted before it keep what they were given, as the game keeps them: 72 of the
    /// S14 ladders' 15,154 items read as unexplained under the new build alone.
    /// </summary>
    public static void LoadEarlier(IEnumerable<string[]> cubemains) =>
        _earlier = [.. cubemains.Select(lines => Read(lines).Grants).Where(grants => grants.Count > 0)];

    private static List<Dictionary<(int Id, string Type), CorruptionGrant[]>> _earlier = [];

    private sealed record Parsed(
        Dictionary<(int Id, string Type), CorruptionGrant[]> Grants, List<CorruptionOption> Ladder,
        Dictionary<string, int> Heights, List<CorruptionOption> Refused);

    /// <summary>A cubemain read: what each corruption id grants each kind of item, and the roll's ladder.</summary>
    private static Parsed Read(string[]? lines)
    {
        var found = new Dictionary<(int, string), CorruptionGrant[]>();
        var ordered = new List<CorruptionOption>();
        var heights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var refused = new List<CorruptionOption>();

        try
        {
            if (lines is null || lines.Length == 0)
                return new Parsed(found, ordered, heights, refused);

            string[] header = lines[0].Split('\t');
            int input = Array.IndexOf(header, "input 1");
            if (input < 0)
                return new Parsed(found, ordered, heights, refused);

            var slots = Enumerable.Range(1, 7)
                .Select(n => (
                    Code: Array.IndexOf(header, $"mod {n}"),
                    Low: Array.IndexOf(header, $"mod {n} min"),
                    High: Array.IndexOf(header, $"mod {n} max"),
                    Spot: Array.IndexOf(header, $"mod {n} param")))
                .Where(slot => slot.Code >= 0 && slot.Low >= 0 && slot.High >= 0)
                .ToArray();

            int band = Array.IndexOf(header, "value");
            int second = Array.IndexOf(header, "input 2");
            int output = Array.IndexOf(header, "output");

            // Both ops guard on stat 361. Op 16 is "below this value", which is the roll
            // and so the ladder. Op 18 is "not corrupted yet" and no roll at all: the rows
            // that merely set the corrupt factor use it, and so do the two charm recipes,
            // which have a single guaranteed outcome and no band to climb.
            int op = Array.IndexOf(header, "op");

            // A recipe the mod has switched off. Thirty-eight corruption rows are: the
            // whole throwing-weapon set, the map tier shuffles, and two charm recipes -
            // one of which offers 107% Faster Run/Walk and is plainly why it was switched
            // off. A blank counts as off, which is how the game reads the column.
            int live = Array.IndexOf(header, "enabled");

            foreach (string line in lines.Skip(1))
            {
                string[] cells = line.Split('\t');
                if (cells.Length <= input)
                    continue;

                // "weap,nos,nor" and the rest: the first word is the item type, and the
                // others are the conditions the cube checks before the transmute. They do
                // not change what is granted, so the type is all that is read here.
                string[] parts = cells[input].Trim().Trim('"').Split(',');
                string type = parts[0].Trim();
                if (type.Length == 0)
                    continue;

                int? id = null;
                int setsRoll = 0;
                var granted = new List<CorruptionGrant>();

                foreach (var (code, low, high, spot) in slots)
                {
                    if (cells.Length <= high || cells[code].Trim() is not { Length: > 0 } what)
                        continue;

                    _ = int.TryParse(cells[low].Trim(), out int min);
                    _ = int.TryParse(cells[high].Trim(), out int max);

                    int param = spot >= 0 && cells.Length > spot
                        ? AffixRanges.ParameterOf(what, cells[spot].Trim())
                        : 0;

                    if (what.Equals("corrupt", StringComparison.OrdinalIgnoreCase))
                        id = min;
                    else if (what.Equals("corruptnum", StringComparison.OrdinalIgnoreCase))
                    {
                        // The recipe that sets the roll: its range is the roll's. A stamp is one number.
                        if (max > min)
                            setsRoll = max;
                    }
                    else if (!what.Equals("corruptnum", StringComparison.OrdinalIgnoreCase))
                    {
                        var (grantMin, grantMax) = AffixRanges.Ordered(what, min, max);
                        granted.Add(new CorruptionGrant(what, grantMin, grantMax, param));
                    }
                }

                // Rows carrying no corrupt id are the rest of the cube - every other recipe
                // in the game is in this table too.
                // A row that grants nothing still takes up room in the roll: turning the
                // item into a random rare is an outcome like any other, and the mod after
                // one must not be credited with its share. Anything transmuted with a
                // Worldstone Shard belongs to the sequence, so it is kept as a bare band
                // and dropped once the widths are worked out.
                // The plain Worldstone Shard, which is the corruption everyone means, and the
                // Corrupted one - `cwss`, Annihilus's own five rows. `iwss` shares the ending
                // and is the map event shard, not a corruption; a substring test took all
                // three for one. Compared whole.
                string shardCode = second >= 0 && cells.Length > second
                    ? cells[second].Trim().Trim('"').Split(',')[0].Trim()
                    : "";
                bool shard = shardCode.Equals(PlainShard, StringComparison.OrdinalIgnoreCase)
                             || shardCode.Equals(AnnihilusShard, StringComparison.OrdinalIgnoreCase);

                // A row that makes the item into something else - Annihilus "destroyed" into a
                // magic small charm - is an outcome of the roll but not a corruption to offer:
                // it keeps its band and is dropped with the rest that grant nothing.
                bool transforms = output >= 0 && cells.Length > output
                    && !cells[output].Trim().Trim('"').Equals("useitem", StringComparison.OrdinalIgnoreCase);

                // PD2 stamps one id on more than one outcome - 16 on a weapon is both "10%
                // faster cast rate" and "20%" - so the id alone says only that it was one of
                // them, and the grants are widened to cover each. The last row winning read a
                // weapon corrupted with 10 as one that should hold 20: 240 of 11,970 corrupted
                // uniques flagged. A code only one of them grants may be
                // absent, so it starts from nothing.
                if (id is { } which)
                {
                    found[(which, type)] = found.TryGetValue((which, type), out var earlier)
                        ? Widen(earlier, granted)
                        : [.. granted];
                }

                // Everything a Worldstone Shard can do to this kind of item, in the order
                // the roll walks it. The rows granting nothing are kept: turning the item
                // into a random rare is a quarter of the roll, and the outcome after it
                // must not inherit that share. What marks a row as an outcome at all is the
                // corrupt id it stamps - the bookkeeping rows carry none.
                bool rolled = op >= 0 && cells.Length > op && cells[op].Trim() == "16";
                bool on = live < 0 || (cells.Length > live && cells[live].Trim() == "1");

                // Two rows of the sequence that are no outcome: the one that sets the roll, and the one
                // that turns an item away.
                if (shard && on && setsRoll > 0)
                    heights.TryAdd(type, setsRoll);

                if (shard && on && op >= 0 && cells.Length > op && cells[op].Trim() == "30")
                {
                    refused.Add(new CorruptionOption(
                        NotACorruption, 0, type,
                        [.. parts.Skip(1).Select(part => part.Trim()).Where(part => part.Length > 0)], []));
                    continue;
                }

                if (!shard || (id is null && !rolled))
                    continue;

                if (!on)
                    continue;

                int top = Thousand;
                if (band >= 0 && cells.Length > band
                    && int.TryParse(cells[band].Trim(), out int edge))
                {
                    top = edge;
                }

                ordered.Add(new CorruptionOption(
                    transforms ? NotACorruption : id ?? NotACorruption,

                    // An unrolled outcome is the whole ladder on its own, so it stands at
                    // the top of it rather than at the value-0 its guard is written with.
                    rolled ? top : Thousand,
                    type,
                    [.. parts.Skip(1).Select(part => part.Trim()).Where(part => part.Length > 0)],
                    [.. granted]) { Shard = shardCode.ToLowerInvariant() });
            }
        }
        catch (Exception problem)
        {
            // Nothing marked rather than the wrong thing marked.
            Log.Caught(problem);
            found.Clear();
            ordered.Clear();
            heights.Clear();
            refused.Clear();
        }

        // Put in place however the reading ended - the early returns too: a load whose cubemain is missing or renamed
        // its columns kept the last tables' ladder, as MapRecipes did.
        return new Parsed(found, ordered, heights, refused);
    }
}
