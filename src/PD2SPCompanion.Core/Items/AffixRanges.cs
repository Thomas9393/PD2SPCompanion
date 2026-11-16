using System.Text;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>What an affix can actually roll for a stat.</summary>
/// <param name="Sources">
/// How many affix mods were added together to reach this. More than one means the item
/// stores a single stat line for both, which is why the range looks wider than any one
/// affix could give - the file collapses them and records no split.
/// </param>
public sealed record RollRange(int Min, int Max, int Sources = 1)
{
    public bool Holds(int value) => value >= Min && value <= Max;

    public override string ToString() => Min == Max ? $"{Min}" : $"{Min}-{Max}";
}

/// <summary>
/// The legitimate range of a stat on an item, from the affixes that put it there.
/// <para>
/// This is a different question from what the file can hold. <c>itemstatcost.txt</c> says a
/// stat fits in so many bits, and past that it corrupts; the affix tables say what the game
/// would ever roll, and past that you simply have an item the game would not have made. The
/// first is a wall, the second is a fact worth knowing.
/// </para>
/// <para>
/// The affix tables name a <b>property</b> - <c>hp</c>, <c>att</c>, <c>res-pois</c> - not a
/// stat, so <c>properties.txt</c> maps between them. Not every property names a stat there:
/// the damage ones are computed by function instead, and those simply have no range here
/// rather than a guessed one.
/// </para>
/// </summary>
public static class AffixRanges
{
    /// <param name="HasRange">
    /// False where the row leaves min and max blank, which happens when the property takes
    /// its value from the parameter instead. The mod is kept because it still names what
    /// the affix wrote; it simply has no range to offer.
    /// </param>
    /// <param name="Param">
    /// The row's parameter. Some parts of a property are written from it rather than from
    /// the roll - the length of a cold or poison effect, above all - so a mod that does not
    /// carry it cannot write the property whole.
    /// </param>
    private sealed record Mod(string Code, int Min, int Max, bool HasRange = true, int Param = 0);

    /// <summary>
    /// One craft recipe: the family it makes, and what it grants.
    /// <para>
    /// The family is the word the table's own description ends on - "magic belt + jewel +
    /// rune 02 + perfect skull -&gt; vampiric belt" is the vampiric family. There are seven
    /// of them across every item type: blood, bountiful, brilliant, caster, hitpower,
    /// safety and vampiric.
    /// </para>
    /// </summary>
    private sealed record Recipe(string Family, List<Mod> Mods);

    /// <summary>
    /// One stat a property writes, and where its value comes from.
    /// <para>
    /// Three of the property functions do not roll at all - they copy a field. Func 15
    /// takes the row's <c>min</c>, func 16 takes its <c>max</c>, and func 17 takes its
    /// <c>param</c>. <c>dmg-fire</c> is func 15 then func 16: min 3 / max 6 is not a roll
    /// of 3 to 6, it is <b>3 minimum fire damage and 6 maximum</b>, both fixed. The game
    /// agrees - it prints its green range bracket on the rolled properties of an item and
    /// prints none on "Adds 3-6 fire damage".
    /// </para>
    /// </summary>
    private sealed record Slot(
        string Stat, bool TakesMin, bool TakesMax, bool TakesParam = false, string Func = "", int Val = 0)
    {
        /// <summary>
        /// Whether this part is a whole property packed into one stat: a chance to cast,
        /// or a charged skill. Three numbers in three columns, none of them a range.
        /// </summary>
        public bool IsPacked => Func is PackedCast or PackedCharges;

        /// <summary>
        /// Whether this part takes its number from the parameter even though it leads the
        /// row - which is what a func-17 property with no range at all means.
        /// <para>
        /// <c>of Self-Repair</c> is written <c>rep-dur param 3</c> with min and max blank.
        /// Read as a roll between nothing and nothing it repaired nothing: "Repairs 0
        /// durability per second". A <c>/lvl</c> property is the same function with its range
        /// filled in, and still rolls - so it is the blank range that decides, not the
        /// position in the row.
        /// </para>
        /// </summary>
        public bool Parameterised(Mod mod) =>
            Func is FromParameter or SocketsFunction && mod.Min == 0 && mod.Max == 0 && mod.Param > 0;

        /// <summary>
        /// Whether this slot can say anything about its stat at all.
        /// <para>
        /// A slot that takes its value from the parameter has nothing to go on when the
        /// row leaves the parameter blank, and <b>rows do</b>: Lawbringer grants
        /// <c>dmg-cold 130-180</c> with no parameter, and <c>dmg-cold</c>'s third slot is
        /// the cold length, read from exactly that parameter. Answered as a range it came
        /// out 0 to 0, and every real Lawbringer was then carrying a cold length outside
        /// what its own runeword could grant.
        /// </para>
        /// <para>
        /// The honest answer is that the editor does not know - the game has a default it
        /// does not write down. So the stat stays accounted for and keeps whatever number
        /// it holds, which is what happens to every other property with nothing to roll
        /// inside.
        /// </para>
        /// </summary>
        public bool Unknowable(Mod mod) => TakesParam && mod.Param == 0;

        /// <summary>
        /// The parameter in the form the <b>item</b> keeps it, which is what a key has to be
        /// built from. A skill tree is written flat in the row, 0 to 20, and kept in the stat
        /// as <c>class * 8 + tab</c> - so keying a range by the row's own number and the
        /// item's by the stat's would never agree, and every tree would read as the wrong one.
        /// </summary>
        public int StoredParam(Mod mod) =>
            Func == TreeIndex ? mod.Param / 3 * 8 + mod.Param % 3

            // Func 12 is laid out the other way round: `par` is the **level** granted and
            // min/max are the skill ids to draw between. Spiritkeeper reads
            // `skill-rand par=2 min=221 max=250` and real ones hold ids 222, 224, 249 and
            // 250, so the row's parameter is not a skill at all - keyed by it, 35 Spirit
            // Keepers read as carrying a skill their own source does not grant. The id is
            // rolled and the row cannot name it, so the source answers to the bare stat.
            : Func == SkillFromRange ? 0

            // Func 21 keeps its subject in properties.txt's val1, never in the row: the seven
            // classes (`ama` 0 ... `ass` 6) and the five elements (`fireskill` 1 ... `poisskill`
            // 5). Without this a source offers parameter 0 and cannot be matched to the item's
            // own, which holds the class or the element.
            : Func == FromCode ? Val
            : mod.Param;

        public RollRange RangeOf(Mod mod) =>
            IsPacked ? new RollRange(Math.Abs(mod.Min), Math.Abs(mod.Min))
            : Func == RandomClass ? new RollRange(Val, Val)

            // Func 12's levels are the row's `par`; its min and max are the skill ids drawn between
            // (see SkillFromRange, and DrawnFor for the ids). Read as the range, Spirit Keeper's +2
            // Twister was measured against 221-250 on the item panel and marked out of it, and its
            // die rolled two hundred levels.
            : Func == SkillFromRange ? new RollRange(mod.Param, mod.Param)
            : Parameterised(mod) ? new RollRange(mod.Param, mod.Param)
            : TakesParam ? new RollRange(mod.Param, mod.Param)
            : TakesMin ? new RollRange(mod.Min, mod.Min)
            : TakesMax ? new RollRange(mod.Max, mod.Max)
            : new RollRange(mod.Min, mod.Max);
    }

    /// <summary>
    /// The two property functions that are three fields wearing one column each.
    /// <para>
    /// <c>func 11</c> is a chance to cast: the parameter names the skill, <c>min</c> is how
    /// often it fires and <c>max</c> is the level it casts at. <c>of Nova</c> reads
    /// <c>param 48, min 10, max 3</c> and the game says "10% Chance to cast level 3 Nova".
    /// </para>
    /// <para>
    /// <c>func 19</c> is a charged skill, the same shape with charges in place of the
    /// chance: Blackhand Key holds <c>param 150, min 30, max 7</c> and the wiki prints
    /// "Level 7 Grim Ward (30 Charges)".
    /// </para>
    /// <para>
    /// Read as an ordinary range, <c>min</c> and <c>max</c> get sorted and rolled between,
    /// so an affix meant to cast level 3 Nova a tenth of the time wrote a chance of six and
    /// no skill at all - the property reading "6% Chance to cast level 0 (none)".
    /// </para>
    /// </summary>
    private const string PackedCast = "11";
    private const string PackedCharges = "19";

    /// <summary>
    /// A skill tree, whose parameter the row writes <b>flat</b>: 0 to 20, three per class in
    /// the order a save numbers classes. The stat keeps it differently - the class three
    /// bits above the tree - so the two have to be converted between, which is what this
    /// function is for. <c>Expert's</c> is written 12 and the item holds 32.
    /// </summary>
    private const string TreeIndex = "10";

    /// <summary>A skill id, straight from the row's parameter.</summary>
    private const string SkillParam = "22";

    /// <summary>
    /// The subject straight from the row's parameter, the roll its number: <c>reanimate</c>, the monster raised
    /// and the chance of it - Demon Machine's <c>par 492, 6-6</c> is "6% Reanimate as: [monster 492]" - and
    /// <c>state</c>, the state an item puts on its wearer. Without it the parameter was dropped and every forged
    /// reanimate raised monster 0, a Skeleton.
    /// </summary>
    private const string SubjectParam = "24";

    /// <summary>
    /// Skills of a class drawn at random: the row's min and max are the classes to draw between,
    /// and the levels granted are the property's own <c>val</c> - <c>randclassskill1</c> 2,
    /// <c>randclassskill3</c> 3. Hellfire Torch reads <c>randclassskill1 min 0 max 6</c>, and read
    /// as a roll its line carried a bracket of "[0 - 6]" and a forged one granted six levels of
    /// no class. A real one holds <c>item_addclassskills 2</c> with the class as its parameter.
    /// </summary>
    private const string RandomClass = "36";

    /// <summary>
    /// A skill drawn at random from a range, whose row is laid out <b>the other way round</b>
    /// from every other skill property: <c>par</c> holds the <b>level</b> granted and
    /// <c>min</c>/<c>max</c> hold the <b>skill ids</b> to choose between.
    /// <para>
    /// Spirit Keeper reads <c>skill-rand par=2 min=221 max=250</c>, and the four real ones in
    /// the item pack all carry <c>item_singleskill</c> at <b>value 2</b> with parameters 222,
    /// 224, 249 and 250 - matching what the game prints, "+2 to [Random Druid Skill]".
    /// </para>
    /// <para>
    /// Read as an ordinary range it wrote the skill id as the value: <c>item_singleskill
    /// 250</c>, which its seven-bit field cannot even hold, so it came back off disk as 122.
    /// The roll is used as the id, which is what the caller picked out of min..max anyway.
    /// </para>
    /// </summary>
    private const string SkillFromRange = "12";

    /// <summary>
    /// The parameter is not in the item's row at all: the property's own <c>val1</c> in
    /// properties.txt says it. One property per class - <c>ama</c> 0, <c>sor</c> 1, <c>nec</c>
    /// 2, <c>pal</c> 3, <c>bar</c> 4, <c>dru</c> 5, <c>ass</c> 6, the order a save numbers them
    /// - and one per element for <c>item_elemskill</c>: <c>fireskill</c> 1, <c>ltngskill</c> 2,
    /// <c>magskill</c> 3, <c>coldskill</c> 4, <c>poisskill</c> 5. Fire measured on a Leaf, poison
    /// on a corpus item. Written with parameter 0 an element grants nothing: a "+N to Fire
    /// Skills" so made prints its line (the partner stat) and gives no levels.
    /// </summary>
    private const string FromCode = "21";

    /// <summary>
    /// One stat a property writes, with the fields that belong to it where it packs more
    /// than a number.
    /// </summary>
    public sealed record Wrote(
        string Stat, int Value, int? SkillId = null, int? SkillLevel = null,
        int? MaxCharges = null, int? Param = null);

    private static Dictionary<string, Slot[]> _properties = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// <c>item_elemskill</c>'s element to the stat that words it, as properties.txt pairs them: each property
    /// whose first stat is <c>item_elemskill</c>, its element in val1 (<see cref="FromCode"/>), writes the wording
    /// stat after it - <c>fireskill</c> (1) <c>item_elemskill_fire</c>, <c>magskill</c> (3)
    /// <c>item_elemskill_magic</c>. See ElementSkills.
    /// </summary>
    public static IReadOnlyDictionary<int, string> ElementLabels()
    {
        if (!ReferenceEquals(_labelsFrom, _properties))
        {
            var labels = new Dictionary<int, string>();
            foreach (var slots in _properties.Values)
            {
                if (slots.Length > 1 && slots[0].Func == FromCode && slots[0].Val > 0
                    && slots[0].Stat.Equals(ElementSkills.Grant, StringComparison.OrdinalIgnoreCase))
                    labels.TryAdd(slots[0].Val, slots[1].Stat);
            }
            _elementLabels = labels;
            _labelsFrom = _properties;
        }
        return _elementLabels;
    }

    private static Dictionary<string, Slot[]>? _labelsFrom;
    private static IReadOnlyDictionary<int, string> _elementLabels = new Dictionary<int, string>();
    private static Dictionary<int, List<Mod>> _prefixes = [];
    private static Dictionary<int, List<Mod>> _suffixes = [];
    private static Dictionary<int, List<Mod>> _automagic = [];

    /// <summary>Each automagic row's group, by its one-based id - what a base's "auto prefix" names.</summary>
    private static Dictionary<int, string> _automagicGroup = [];

    /// <summary>The group an automagic row belongs to - 312 for Staff Tier 1 - or null.</summary>
    public static string? AutomagicGroupOf(int id) => _automagicGroup.GetValueOrDefault(id);

    /// <summary>Each automagic row's item levels, from its <c>level</c> up to its <c>maxlevel</c> (0: no ceiling).</summary>
    private static Dictionary<int, (int Least, int Most)> _automagicLevels = [];

    /// <summary>Each automagic row's <c>frequency</c>, by id: how often the game draws it among its group's.</summary>
    private static Dictionary<int, int> _automagicFrequency = [];

    /// <summary>
    /// A unique or set item's properties, by the same row number its name resolves through.
    /// These are not affixes at all - the list is fixed and only the numbers roll - but they
    /// are the only range such an item has, and without them a unique showed none.
    /// </summary>
    private static Dictionary<int, List<Mod>> _uniques = [];
    private static Dictionary<int, List<Mod>> _setItems = [];

    /// <summary>
    /// The green bonuses a set item gains from the rest of its set, by the same row number
    /// its own properties are found by.
    /// <para>
    /// <b>These are stored on the item</b>, in a property list of their own - the same shape
    /// a runeword uses. A set belt off the character carries <c>item_armor_perlevel 20</c> in
    /// its second list and nothing accounted for it, because the <c>aprop</c> columns were
    /// being skipped as something the game granted while the set was worn.
    /// </para>
    /// <para>
    /// Every tier is counted, <c>a</c> and <c>b</c> alike. They are granted as more of the
    /// set is worn, so all of them together is the most an item can legitimately show, and
    /// for deciding whether a property is accounted for that is the right way round.
    /// </para>
    /// </summary>
    private static Dictionary<int, List<SetBonus>> _setBonuses = [];

    /// <summary>
    /// One green bonus and how much of the set has to be worn for it: <c>aprop1</c> is two
    /// pieces, <c>aprop2</c> three, and so on.
    /// </summary>
    private sealed record SetBonus(int Pieces, Mod Mod);

    /// <summary>
    /// How many of the set have to be worn before this property appears, or null where the
    /// set does not grant it at all. What the wiki prints beside a green line as "(2 Items)".
    /// </summary>
    public static int? SetPiecesFor(Item item, string stat)
    {
        if (item.Quality is not ItemQuality.Set
            || !_setBonuses.TryGetValue((int)item.FileIndex, out var green))
        {
            return null;
        }

        foreach (var bonus in green)
        {
            if (StatsOf(bonus.Mod.Code).Any(each =>
                    string.Equals(each, stat, StringComparison.OrdinalIgnoreCase)))
            {
                return bonus.Pieces;
            }
        }

        return null;
    }

    /// <summary>
    /// The same for a line of the item's list number <paramref name="list"/>: a bonus can stand on several tiers
    /// (Aldur's Gauntlet's +15 Strength on 2, 3 and 4 pieces), and the list is what tells them apart.
    /// </summary>
    public static int? SetPiecesFor(Item item, int list, string stat) =>
        SetPiecesFor(item, stat) is not { } first ? null : SetTierOfList(item, list) is { } tier ? tier + 1 : first;

    /// <summary>The tier of a set piece's list number: the list's place among the set bits of its mask, bit 0 being <c>aprop1</c>. Null where the mask has no such list.</summary>
    private static int? SetTierOfList(Item item, int list)
    {
        if (item.Quality is not ItemQuality.Set || list < 1)
            return null;

        int seen = 0;
        for (int tier = 1; tier <= 5; tier++)
        {
            if ((item.SetItemMask & (1 << (tier - 1))) != 0 && ++seen == list)
                return tier;
        }

        return null;
    }

    public static void Load(GameData game)
    {
        _properties = LoadProperties(game);
        _prefixes = LoadAffixes(game, "magicprefix");
        _suffixes = LoadAffixes(game, "magicsuffix");

        // Auto prefixes, which the game puts on an item without it being magic. This is
        // what gives PD2's white weapons their real properties, and the save records which
        // one was used, so it resolves exactly rather than as a guess across a group.
        // One-based, unlike the two above. See LoadAffixes.
        _automagic = LoadAffixes(game, "automagic", oneBased: true);
        _automagicGroup = Groups(game, "automagic");
        _automagicLevels = Levels(game, "automagic");
        _automagicFrequency = Numbers(game, "automagic", "frequency");

        // Twelve property slots on a unique, nine on a set item. A set item's aprop
        // columns are its green set bonuses: stored on the item, a list of their own for
        // each tier, turned on by wearing the set - so they are read apart from its own
        // properties, into _setBonuses below. (Nine real set pieces
        // show they are stored - see ItemForge.AddSetBonuses.)
        _uniques = LoadFixedItems(game, "uniqueitems", 12);
        _setItems = LoadFixedItems(game, "setitems", 9);
        _setBonuses = LoadSetBonuses(game);
        _crafts = LoadCrafts(game);

        // Which properties anything grants, and - worked out from them when first asked - which stats
        // are never written apart (NeverApart).
        _granted = LoadGranted(game);
        _neverApart = null;
    }

    /// <summary>
    /// The tables that hand properties out, and the columns that name one in each: affixes and automagic,
    /// uniques and set pieces with their bonuses, sets, runewords, gems and runes, the cube's recipes,
    /// superior quality.
    /// </summary>
    private static readonly (string Table, System.Text.RegularExpressions.Regex Columns)[] Granting =
    [
        ("magicprefix", new(@"^mod\dcode$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
        ("magicsuffix", new(@"^mod\dcode$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
        ("automagic", new(@"^mod\dcode$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
        ("qualityitems", new(@"^mod\dcode$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
        ("uniqueitems", new(@"^prop\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
        ("setitems", new(@"^a?prop\d+[ab]?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
        ("sets", new(@"^[PF]Code\d+[ab]?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
        ("runes", new(@"^T1Code\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
        ("gems", new(@"^(weapon|helm|shield)Mod\dCode$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
        ("cubemain", new(@"^([bc] )?mod \d$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
    ];

    private static HashSet<string> _granted = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Every property code some table grants. <c>properties.txt</c> defines 450 and 329 are handed out by
    /// anything (S13): <c>dmg-throw</c>, which could write a thrown damage alone, is one nothing grants -
    /// so what a property could do in principle is weighed only where an item can come by it.
    /// </summary>
    private static HashSet<string> LoadGranted(GameData game)
    {
        var granted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (table, columns) in Granting)
        {
            try
            {
                var lines = Rows(game, table);
                if (lines.Count == 0)
                    continue;

                int[] named = [.. lines[0].Select((header, at) => columns.IsMatch(header.Trim()) ? at : -1).Where(at => at >= 0)];
                foreach (string[] cells in lines.Skip(1))
                {
                    foreach (int at in named)
                    {
                        if (at < cells.Length && cells[at].Trim() is { Length: > 0 } code)
                            granted.Add(code);
                    }
                }
            }
            catch (Exception)
            {
                // A table that will not read grants nothing here; the rest still count.
            }
        }
        return granted;
    }

    private static IReadOnlyList<IReadOnlySet<string>>? _neverApart;

    /// <summary>
    /// The stats no property ever writes apart: wherever a granted property writes one of a group it
    /// writes them all, and the same number on each. On an item they are one number, whatever granted it
    /// and however many sources feed it - Enhanced Damage's maximum and minimum (only <c>dmg%</c> writes
    /// either), a flat damage's one-hand, two-hand and thrown numbers, a map's paired monster and player
    /// lines. The item panel keeps their rows alike in every play mode,
    /// manual input too.
    /// <para>
    /// Worked out from <c>properties.txt</c> rather than listed, so a season that adds such a property is
    /// followed: for each stat, the granted properties that write it and how - the roll, the row's
    /// minimum, its maximum, its parameter; two stats are together where every property writing either
    /// writes both the same way. All Resistances and All Attributes are not here - a single resistance
    /// has affixes of its own - and move together only where one source wrote them
    /// (<see cref="RolledTogether"/>). Checked on 273 Enhanced Damage pairs and 162 flat damage sets of
    /// the corpus saves: never unequal; a flat damage stat can be absent (a two-handed weapon stores only
    /// its own), so the rows that are there are the ones kept alike.
    /// </para>
    /// </summary>
    public static IReadOnlyList<IReadOnlySet<string>> NeverApart() => _neverApart ??= FindNeverApart();

    /// <summary>
    /// The stats that go on an item, and come off it, with this one: its never-apart group, as far as the
    /// item holds it - a two-handed weapon none of the one-handed and thrown damage (<see cref="DamageKinds"/>).
    /// The stat itself is not among them; empty for a stat written alone.
    /// </summary>
    public static IReadOnlyList<string> WrittenWith(Item item, string stat) =>
        NeverApart().FirstOrDefault(group => group.Contains(stat)) is { } group
            ? [.. group.Where(other => !other.Equals(stat, StringComparison.OrdinalIgnoreCase) && DamageKinds.Holds(item, other))
                       .OrderBy(other => StatCatalogue.IdOf(other) ?? ushort.MaxValue)]
            : [];

    private static IReadOnlyList<IReadOnlySet<string>> FindNeverApart()
    {
        // What each granted property writes, and how.
        var writes = new Dictionary<string, List<(string Stat, string How)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, slots) in _properties)
        {
            if (_granted.Count > 0 && !_granted.Contains(code))
                continue;

            var parts = new List<(string Stat, string How)>();
            foreach (var slot in slots)
            {
                parts.Add((slot.Stat, slot.TakesMin ? "min" : slot.TakesMax ? "max" : slot.TakesParam ? "param"
                                      : slot.IsPacked ? "packed" : "roll"));
            }

            // The partners a damage property writes without naming them, as their leader is written.
            foreach (string partner in FannedOut(code))
            {
                if (parts.Any(part => part.Stat.Equals(partner, StringComparison.OrdinalIgnoreCase)))
                    continue;

                string leader = partner.Contains("min", StringComparison.OrdinalIgnoreCase) ? "mindamage" : "maxdamage";
                foreach (var led in parts.Where(part => part.Stat.Equals(leader, StringComparison.OrdinalIgnoreCase)).ToList())
                    parts.Add((partner, led.How));
            }

            writes[code] = parts;
        }

        var writers = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, parts) in writes)
        {
            foreach (var (stat, _) in parts)
            {
                if (!writers.TryGetValue(stat, out var codes))
                    writers[stat] = codes = new(StringComparer.OrdinalIgnoreCase);
                codes.Add(code);
            }
        }

        bool Together(string one, string other)
        {
            foreach (string code in writers[one].Union(writers[other], StringComparer.OrdinalIgnoreCase))
            {
                var first = writes[code].Where(part => part.Stat.Equals(one, StringComparison.OrdinalIgnoreCase)).Select(part => part.How).ToHashSet();
                var second = writes[code].Where(part => part.Stat.Equals(other, StringComparison.OrdinalIgnoreCase)).Select(part => part.How).ToHashSet();
                if (first.Count == 0 || second.Count == 0 || !first.SetEquals(second))
                    return false;
            }
            return true;
        }

        var groups = new List<IReadOnlySet<string>>();
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string stat in writers.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            if (placed.Contains(stat))
                continue;

            var group = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { stat };
            foreach (string other in writers.Keys)
            {
                if (!placed.Contains(other) && !group.Contains(other) && group.All(member => Together(member, other)))
                    group.Add(other);
            }

            if (group.Count > 1)
            {
                placed.UnionWith(group);
                groups.Add(group);
            }
        }

        return groups;
    }

    /// <summary>
    /// The ranges for what was added to an item rather than rolled on it: a runeword's
    /// properties, or a set's bonuses. Both live in property lists of their own, after the
    /// item's, which is how a row knows which of these to answer to.
    /// </summary>
    private static void AddedOnly(
        Item item, Dictionary<string, RollRange> found, Action<string, RollRange> gain, int? setTier = null,
        Drawing? drawn = null)
    {
        var mods = new List<Mod>();

        if (RunewordOn(item) is { } word)
            mods.AddRange(word);

        // A set piece keeps each tier's bonuses in a list of its own, so a list asks only its
        // own tier: Aldur's Gauntlet gives +15 Strength at 2, 3 and 4 pieces, three lists of 15,
        // and summed across the tiers each line was measured against 45.
        if (item.Quality is ItemQuality.Set
            && _setBonuses.TryGetValue((int)item.FileIndex, out var green))
        {
            mods.AddRange(green.Where(bonus => setTier is null || bonus.Pieces == setTier + 1).Select(bonus => bonus.Mod));
        }

        foreach (var mod in mods)
        {
            if (!mod.HasRange || !_properties.TryGetValue(mod.Code, out var slots))
                continue;

            foreach (var slot in slots)
            {
                if (slot.Unknowable(mod))
                    continue;

                drawn?.Note(slot, mod);

                gain(KeyOf(slot.Stat, slot.StoredParam(mod)), slot.RangeOf(mod));
            }
        }
    }

    /// <summary>
    /// What the runeword in this item grants, in the shape the rest of this file works in.
    /// The table itself lives in <see cref="Runewords"/>, which the names read too.
    /// </summary>
    private static List<Mod>? RunewordOn(Item item) =>
        Runewords.On(item) is { } word
            ? [.. word.Mods.Select(mod =>
            {
                var (first, second) = Ordered(mod.Code, mod.Min, mod.Max);

                return new Mod(mod.Code, first, second, mod.HasRange, mod.Param);
            })]
            : null;

    /// <summary>
    /// What a craft recipe grants, by the item type it takes. Several per type - PD2 has
    /// five families, and the same recipe is written twice, once for a magic input and once
    /// for a rare one - so a type's entry is a list of alternatives, not a list of mods.
    /// </summary>
    private static Dictionary<string, List<Recipe>> _crafts =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The recipes that could have made this crafted item.
    /// <para>
    /// No recipe id is recorded - the item keeps its quality and its affixes and nothing
    /// about the jewel, rune and gem that went in with it. But the recipe is not therefore
    /// unknowable: each one grants a fixed set of properties, and <b>every item type has
    /// exactly seven such sets, each unique within that type</b>. A belt granting
    /// manasteal, regen and res-pois-len came from the vampiric recipe and no other. See
    /// <see cref="RecipeBehind"/>, which is what callers should ask first.
    /// </para>
    /// </summary>
    private static IReadOnlyList<Recipe> CraftsFor(Item item) =>
        item.Quality is ItemQuality.Craft ? RecipesTaking(item) : [];

    /// <summary>
    /// Every recipe this item's <b>base</b> can go through, whatever the item is now.
    /// <para>
    /// Asked without regard to quality, unlike <see cref="CraftsFor"/>: what a base can be
    /// crafted into is a fact about the base, and a magic ring has to be able to answer it
    /// before it is crafted rather than after. Gating this on the item already being
    /// crafted made the question unanswerable for every item that could still be asked it.
    /// </para>
    /// </summary>
    private static IReadOnlyList<Recipe> RecipesTaking(Item item)
    {
        var found = new List<Recipe>();
        var lineage = ItemTypes.LineageOfBase(item.Code);

        // Circlets are helms to the tables - the helm recipes' input is the type, and circ
        // descends from helm - but PD2 refuses to craft them: the wiki's "Circlets are the
        // exception, and cannot be crafted with", confirmed in game. Not
        // in any table, so said here. The 670 circlets on the staged ladder: none crafted.
        if (lineage.Contains("circ", StringComparer.OrdinalIgnoreCase))
            return found;

        foreach (string step in lineage)
        {
            if (_crafts.TryGetValue(step, out var recipes))
                found.AddRange(recipes);
        }

        return found;
    }

    /// <summary>
    /// Recipes told rather than worked out, held beside the item - see <see cref="PinRecipe"/>.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Item, Recipe> _pinnedRecipes = new();

    /// <summary>
    /// Works out a crafted item's recipe now and keeps it, for a change that is about to take
    /// away what tells it. A recipe is told from the properties the item carries, and an affix
    /// can be what settles it: the market's "Wraith Dart" was one bow recipe only because an
    /// old affix gave deadly strike, and with the affixes rerolled away no recipe fitted alone -
    /// its ranges fell back to every bow recipe's and its reroll came back flagged (40 of 40).
    /// The reroll-all pins it first. Returns whether one was told.
    /// </summary>
    public static bool PinRecipe(Item item)
    {
        if (item.Quality is not ItemQuality.Craft || (RecipeBehind(item) ?? Likeliest(item)) is not { } recipe)
            return false;

        _pinnedRecipes.AddOrUpdate(item, recipe);
        return true;
    }

    /// <summary>
    /// Where more than one recipe fits, the crafting family inferred: the recipe
    /// that <b>leaves the fewest of the item's properties unexplained</b> by everything else on it
    /// - its affixes, automagic, corruption, desecration - and, among those, the one explaining
    /// the most. "Wraith Dart" fits seven families on stats alone; Blood (life stolen per hit,
    /// +life) and Brilliant (IAS, attack rating) explain two each, but its affixes - of Swiftness,
    /// of Accuracy - already account for Brilliant's pair and nothing on it gives life steal or
    /// life, so Blood is the only one that leaves nothing over. A tie is not guessed: null.
    /// </summary>
    private static Recipe? Likeliest(Item item)
    {
        var carried = new HashSet<string>(
            ItemStats.Every(item).Select(stat => stat.Stat ?? "").Where(stat => stat.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        var elsewhere = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sources = new List<(IEnumerable<int> Ids, Dictionary<int, List<Mod>> Table)>
        {
            (item.MagicPrefixIds?.Select(id => (int)id) ?? [], _prefixes),
            (item.MagicSuffixIds?.Select(id => (int)id) ?? [], _suffixes),
        };
        if (item.IsAutoAffix && item.AutoAffixId > 0)
            sources.Add(([item.AutoAffixId], _automagic));

        foreach (var (ids, table) in sources)
        {
            foreach (int id in ids)
            {
                if (id > 0 && table.TryGetValue(id, out var mods))
                    elsewhere.UnionWith(mods.SelectMany(mod => StatsWritten(mod.Code)));
            }
        }

        elsewhere.UnionWith(Corruptions.StatsOn(item));
        elsewhere.UnionWith(Desecrations.StatsOn(item));
        elsewhere.UnionWith(BaseUpgrades.Grants(item).SelectMany(grant => StatsWritten(grant.Code)));
        elsewhere.UnionWith(MapRecipes.Grants(item).SelectMany(grant => StatsWritten(grant.Code)));

        var fits = CraftsFor(item)
            .Where(recipe => Grants(recipe.Mods, carried))
            .Select(recipe =>
            {
                var grants = recipe.Mods.SelectMany(mod => StatsWritten(mod.Code)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                return (Recipe: recipe,
                        Left: carried.Count(stat => !elsewhere.Contains(stat) && !grants.Contains(stat)),
                        Explained: grants.Count(carried.Contains));
            })
            .OrderBy(fit => fit.Left)
            .ThenByDescending(fit => fit.Explained)
            .ToList();

        if (fits.Count == 0)
            return null;

        // The best, unless another is as good and is a different recipe - the same one appears
        // once per base quality and once for ethereal.
        var best = fits[0];
        return fits.Skip(1).Any(other => other.Left == best.Left && other.Explained == best.Explained
                                         && !Same(other.Recipe.Mods, best.Recipe.Mods))
            ? null
            : best.Recipe;
    }

    /// <summary>The recipe pinned on an item let go, its lines having changed family: they tell it again.</summary>
    public static void ForgetPinnedRecipe(Item item) => _pinnedRecipes.Remove(item);

    /// <summary>Whether a recipe was told for this item, which is in none of its bytes.</summary>
    public static bool HasPinnedRecipe(Item item) => _pinnedRecipes.TryGetValue(item, out _);

    /// <summary>A pinned recipe carried onto a copy of the item, which is a new object.</summary>
    public static void CarryPinnedRecipe(Item from, Item to)
    {
        if (_pinnedRecipes.TryGetValue(from, out var recipe))
            _pinnedRecipes.AddOrUpdate(to, recipe);
    }

    /// <summary>
    /// The recipe this crafted item was actually made by, or null where it cannot be told.
    /// <para>
    /// Found by what the item carries: a recipe grants a fixed set of properties, so the
    /// recipe whose every property is on the item is the one that made it. Whole sets are
    /// matched rather than single properties, because one property on its own proves
    /// nothing - a crafted belt can carry <c>ac%</c> from a Sturdy prefix without having
    /// come from any recipe that grants it.
    /// </para>
    /// <para>
    /// Where two recipes both fit, which affixes can contrive, the likeliest (<see cref="Likeliest"/>): the one leaving
    /// the fewest of the item's properties unexplained by its affixes and the rest. A Hitpower spear with Master's
    /// (attack rating) and of Quickness (attack speed) fits Brilliant as well, and was told as no recipe at all -
    /// "Crafting: Unknown", before it was made Blood and after (a Rune Prod). Null where they
    /// tie even so, or where none fits - an item the editor itself has had properties taken off, say. The caller then
    /// falls back to the widest of them, which is wider than the truth and never narrower.
    /// </para>
    /// </summary>
    private static Recipe? RecipeBehind(Item item)
    {
        if (item.Quality is ItemQuality.Craft && _pinnedRecipes.TryGetValue(item, out var pinned))
            return pinned;

        var recipes = CraftsFor(item);
        if (recipes.Count == 0)
            return null;

        var carried = new HashSet<string>(
            ItemStats.Every(item)
                .Select(stat => stat.Stat ?? "")
                .Where(stat => stat.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        Recipe? only = null;

        foreach (var recipe in recipes)
        {
            if (!Grants(recipe.Mods, carried))
                continue;

            // The same recipe appears several times over - once per base quality it takes
            // and once for ethereal - so a second sighting of the same set is not two
            // recipes fitting. Two different sets fitting is: the likeliest of them settles it.
            if (only is not null && !Same(only.Mods, recipe.Mods))
                return item.Quality is ItemQuality.Craft ? Likeliest(item) : null;

            only ??= recipe;
        }

        return only;
    }

    /// <summary>
    /// Which craft family made this item - "Vampiric", "Blood" - or null where it cannot
    /// be told, which is what an item edited since it was crafted looks like.
    /// </summary>
    public static string? CraftFamilyOf(Item item) => RecipeBehind(item)?.Family;

    /// <summary>One property a craft recipe grants, and the range it rolls inside.</summary>
    public sealed record CraftGrant(string Code, int Min, int Max, int Param = 0);

    /// <summary>A craft family, and everything it puts on an item.</summary>
    public sealed record CraftRecipe(string Family, IReadOnlyList<CraftGrant> Grants);

    /// <summary>The recipe that made this item, named and with its ranges, or null.</summary>
    public static CraftRecipe? CraftOn(Item item) =>
        RecipeBehind(item) is { } made ? Shown(made) : null;

    /// <summary>
    /// The recipe that made this crafted item, or - where more than one fits - the likeliest (<see cref="Likeliest"/>), as
    /// the reroll-all pins it: for a change that must take the old family's properties off. A Bountiful staff with a
    /// +mana prefix fits Caster as well (all skills, mana, the staff's cast rate), told no recipe, and its family changed
    /// to Brilliant kept Bountiful's all skills, light radius and magic find (the Forge's sweep).
    /// </summary>
    public static CraftRecipe? CraftLikelyOn(Item item) =>
        (RecipeBehind(item) ?? (item.Quality is ItemQuality.Craft ? Likeliest(item) : null)) is { } made ? Shown(made) : null;

    /// <summary>
    /// Every craft family this item's base could be made into, one per family.
    /// <para>
    /// The table holds each of them several times over - once per base quality it takes and
    /// once for ethereal - so they are folded down to one apiece, kept in table order. The
    /// ethereal copies differ only by a mod that writes no property, so which copy is kept
    /// makes no difference to what is granted.
    /// </para>
    /// </summary>
    public static IReadOnlyList<CraftRecipe> CraftFamilies(Item item)
    {
        var shown = new List<CraftRecipe>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var recipe in RecipesTaking(item))
        {
            if (recipe.Family.Length > 0 && seen.Add(recipe.Family))
                shown.Add(Shown(recipe));
        }

        return shown;
    }

    /// <summary>
    /// The craft families whose every property this crafted item carries, one per family - more than one where its affixes
    /// could stand in for a recipe's lines. Told before its affixes are: what an affix search weighs one by one, where
    /// <see cref="CraftOn"/> would answer the likeliest of them by counting lines, and so before the affixes that settle
    /// it are known. A level 92 Druid's Caster Truncheon carries Brilliant's three too (Savage, Meteoric and an attack speed
    /// suffix), and was searched as Brilliant alone (the S14 ladder). A recipe pinned is the one.
    /// </summary>
    public static IReadOnlyList<CraftRecipe> CraftsFitting(Item item)
    {
        if (_pinnedRecipes.TryGetValue(item, out var pinned))
            return [Shown(pinned)];

        var carried = new HashSet<string>(
            ItemStats.Every(item).Select(stat => stat.Stat ?? "").Where(stat => stat.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        var fitting = new List<Recipe>();
        foreach (var recipe in CraftsFor(item))
        {
            if (Grants(recipe.Mods, carried) && !fitting.Any(other => Same(other.Mods, recipe.Mods)))
                fitting.Add(recipe);
        }

        return [.. fitting.Select(Shown)];
    }

    private static CraftRecipe Shown(Recipe recipe) =>
        new(recipe.Family,
            [.. recipe.Mods
                .Where(mod => mod.HasRange && _properties.ContainsKey(mod.Code))
                .Select(mod => new CraftGrant(mod.Code, mod.Min, mod.Max, mod.Param))]);

    /// <summary>Whether every property this recipe grants is on the item.</summary>
    private static bool Grants(List<Mod> recipe, HashSet<string> carried)
    {
        bool any = false;

        foreach (var mod in recipe)
        {
            if (!_properties.TryGetValue(mod.Code, out var slots))
                continue;

            foreach (var slot in slots)
            {
                // Ethereal is not a property an item keeps in its list, so a recipe is
                // matched on the ones that are.
                if (slot.Stat.Length == 0)
                    continue;

                if (!carried.Contains(slot.Stat))
                    return false;

                any = true;
            }
        }

        return any;
    }

    /// <summary>
    /// Whether two recipes grant the same thing - compared on the stats they write, not on
    /// the mods they list.
    /// <para>
    /// Every recipe is in the table twice, once for an ethereal base and once not, and the
    /// ethereal one carries an extra <c>ethereal</c> mod that writes no stat at all. On
    /// mod codes those two read as different recipes, and a family matching itself in both
    /// forms then looked like two families matching - which is exactly the case this is
    /// here to rule out.
    /// </para>
    /// </summary>
    private static bool Same(List<Mod> one, List<Mod> other) =>
        StatsOf(one).SetEquals(StatsOf(other));

    private static HashSet<string> StatsOf(List<Mod> recipe)
    {
        var stats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in recipe)
        {
            if (!_properties.TryGetValue(mod.Code, out var slots))
                continue;

            foreach (var slot in slots)
                if (slot.Stat.Length > 0)
                    stats.Add(slot.Stat);
        }

        return stats;
    }

    /// <summary>
    /// What a stat is, for the purpose of matching it to a source: the stat name, and for
    /// the shapes where the parameter names <b>what</b> was granted, the parameter too.
    /// <para>
    /// Most properties appear once, so the stat's own name is the whole of it. A property
    /// that names a skill does not: a Leaf staff carries three <c>item_singleskill</c> lines
    /// - Fire Bolt, Warmth, Inferno - and they are three properties, not one property three
    /// times. Keyed by name alone their ranges were added together, and each line was then
    /// judged against the sum of all three: the staff read "+3 to Fire Bolt, affix stat
    /// range 8-11", which called a runeword the game itself made illegitimate.
    /// </para>
    /// <para>
    /// <c>Class</c>, <c>Monster</c> and <c>State</c> <b>are keyed</b>. Three
    /// causes had to go first. <see cref="LoadFixedItems"/> was reading the parameter from a
    /// column (<c>mod{n}param</c>) that <c>uniqueitems.txt</c> and <c>setitems.txt</c> do not
    /// have, zeroing 769 of them; <see cref="Slot.StoredParam"/> did not map func 21's
    /// class-from-code; and <b>some sources cannot name their subject at all</b>.
    /// </para>
    /// <para>
    /// <b>A rolled subject registers under the bare stat name.</b> <c>randclassskill</c>
    /// (func 36) grants <c>item_addclassskills</c> for a class drawn between 0 and 6, and
    /// <c>skill-rand</c> (func 12) picks a skill out of a range; neither row can say which, so
    /// neither gets a <c>#</c>. A reader must therefore try the key and <b>then the bare
    /// name</b>. That fallback is not a loophole: it means "a source grants this stat for a
    /// subject it could not name", which is exactly true, and a source that <i>can</i> name its
    /// subject is still held to it - an Amazon spear carrying a Sorceress skill tree is still
    /// caught. That fallback is in <see cref="Legitimacy"/>.
    /// </para>
    /// <para>
    /// <b>What it buys, and the measurement that shipped it.</b> A source row names its
    /// subject, so two rows granting the same stat for different subjects must not share one
    /// range. Sacred Totem is the specimen: <c>par7=19 min7=4 max7=6</c> (Fallen) and
    /// <c>par8=58 min8=2 max8=3</c> (Fallen Shaman). Unkeyed they merge into <c>6-9</c>, and
    /// the Fallen Shaman line - a legitimate 3 - reads as out of range. Keyed, each line
    /// answers to its own row.
    /// </para>
    /// <para>
    /// Measured over <b>10,931</b> items, both packs carried to this season:
    /// silent 71.1% and 68.2%, unrolled 1837 and 2122, unaccounted 894 and 936 - <b>identical
    /// with keying on and off</b>, with one group's wording improved from "is outside 5, which
    /// is what its sources could roll" to "is not one of the monsters this item's sources
    /// grant; they grant 7% Reanimate as: Oblivion Knight". SpearZon stays 100% silent.
    /// </para>
    /// <para>
    /// <b>An earlier note here claimed a net 2256 -&gt; 2323 regression and two blocking
    /// groups</b> - 170 <c>item_nonclassskill</c> against a runeword's <c>oskill</c> level, and
    /// ~60 skill tabs on Superior charms. Neither reproduces on the corpora above, which are
    /// the packs read through <c>migrate --from</c> rather than under the installed tables.
    /// <b>So the two groups are not disproved, only unmeasured here</b> - if they return, they
    /// are about runeword levels and hand-made items, not about this key.
    /// </para>
    /// <para>
    /// <c>Element</c> stays unkeyed on purpose: which number the game gives each element is
    /// unknown, and its stat is obsolete in PD2 besides.
    /// </para>
    /// </summary>
    public static string KeyOf(string stat, int param) =>
        param > 0 && StatShapes.Of(stat) is StatShape.Skill or StatShape.Chance
                     or StatShape.Charges or StatShape.SkillTree
                     or StatShape.Class or StatShape.Monster or StatShape.State
            ? $"{stat}#{param}"
            : stat;

    /// <summary>The same, for a property already on an item.</summary>
    public static string KeyOf(ItemStat stat) =>
        KeyOf(stat.Stat ?? "", ItemStats.SkillOrParam(stat));

    /// <summary>The same, for what a property writes.</summary>
    public static string KeyOf(Wrote wrote) =>
        KeyOf(wrote.Stat, ItemStats.SkillOrParam(wrote));

    /// <summary>
    /// Every stat this item's affixes can account for, and the range each could have
    /// rolled. Stats from anywhere else - a base item's own properties, for instance - are
    /// simply absent.
    /// </summary>
    public static IReadOnlyDictionary<string, RollRange> For(Item item) =>
        Build(item, own: true, added: true);

    /// <summary>
    /// What this item's sources grant for a subject <b>drawn at random</b>, where the row cannot
    /// name it - so the subject is a roll like a number is, not a choice the item made, and the
    /// item panel offers a die on it:
    /// <list type="bullet">
    /// <item><b>Classes</b>: the stats granted for a class drawn from the seven - Hellfire Torch's
    /// <c>randclassskill</c> (func 36, see <see cref="RandomClass"/>).</item>
    /// <item><b>Skills</b>: the stats granted for a skill drawn from a range of ids, and the range -
    /// Spirit Keeper's <c>skill-rand par=2 min=221 max=250</c> (func 12, see
    /// <see cref="SkillFromRange"/>).</item>
    /// </list>
    /// </summary>
    public static DrawnSubjects DrawnFor(Item item)
    {
        var drawn = new Drawing();
        Build(item, own: true, added: true, drawn: drawn);
        return new DrawnSubjects(drawn.Classes, drawn.Skills);
    }

    /// <summary>What <see cref="DrawnFor"/> answers: the stats whose class is drawn, and the skill ids each drawn skill comes from.</summary>
    public sealed record DrawnSubjects(IReadOnlySet<string> Classes, IReadOnlyDictionary<string, RollRange> Skills);

    /// <summary>Gathered while <see cref="Build"/> walks the sources.</summary>
    private sealed class Drawing
    {
        public HashSet<string> Classes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, RollRange> Skills { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Note(Slot slot, Mod mod)
        {
            if (slot.Func == RandomClass)
                Classes.Add(slot.Stat);
            else if (slot.Func == SkillFromRange)
            {
                // Two such rows granting one stat draw from either: the union of their ranges.
                int low = Math.Min(mod.Min, mod.Max), high = Math.Max(mod.Min, mod.Max);
                Skills[slot.Stat] = Skills.TryGetValue(slot.Stat, out var had)
                    ? new RollRange(Math.Min(had.Min, low), Math.Max(had.Max, high))
                    : new RollRange(low, high);
            }
        }
    }

    /// <summary>
    /// What the item's <b>own</b> sources could have rolled - its affixes, its base, a
    /// corruption - leaving out what a runeword or a set added to it.
    /// <para>
    /// The two are worth asking apart because the file keeps them apart. A Leaf staff has
    /// Fire Bolt twice, once from its own staffmod and once from the runeword, and added
    /// together they make a range of 4 to 6 that either line could sit anywhere inside -
    /// so a staffmod of +6, which no staff rolls, passed for legitimate. Ranged against
    /// the source that granted it, the staffmod line answers to 1 to 3 and nothing else.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<string, RollRange> OwnOf(Item item) =>
        Build(item, own: true, added: false);

    /// <summary>What the runeword or the set added, and only that.</summary>
    public static IReadOnlyDictionary<string, RollRange> AddedTo(Item item) =>
        Build(item, own: false, added: true);

    /// <summary>
    /// What added the properties of the item's list number <paramref name="list"/>: a set
    /// piece's green list is its own tier alone - the list's place among the set bits of the
    /// item's mask, bit 0 being <c>aprop1</c> - and anything else asks <see cref="AddedTo(Item)"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, RollRange> AddedTo(Item item, int list) =>
        SetTierOfList(item, list) is { } tier ? Build(item, own: false, added: true, setTier: tier) : AddedTo(item);

    /// <summary>
    /// The range the <b>game</b> prints in its green bracket, which is a different question
    /// from what this item's own affixes could have rolled.
    /// <para>
    /// PD2 does not bracket the row that granted the stat. It brackets the <b>union</b> over
    /// every affix the base could have rolled at its item level that writes that stat -
    /// see <see cref="MagicAffixes.ReachableBy"/> for the pool.
    /// So a boot whose Fire Resist rolled <c>Crimson 5-10</c> is shown <c>[5 - 30]</c>,
    /// because <c>Russet</c> and <c>Garnet</c> were also within its reach.
    /// </para>
    /// <para>
    /// The union is <b>by stat</b>, not by property code: a helm's Fire Resist bracket starts
    /// at 3 because <c>Shimmering</c> writes <c>res-all</c>, which is a different property
    /// reaching the same stat.
    /// </para>
    /// <para>
    /// <b>This is not a bound on what the item may hold, and must never be used as one.</b>
    /// Where two affixes wrote one stat the file keeps a single line and the game does not
    /// add the brackets to match: a helm carrying 144 Attack Rating from <c>Gold</c>
    /// (81-100) and <c>of Radiance</c> (30-60) is printed <c>[10 - 120]</c>. Judging that
    /// item against its own tooltip calls a real drop a forgery. <see cref="For"/> is the
    /// bound; this is only what the game chose to draw.
    /// </para>
    /// <para>
    /// Stats no affix in reach writes keep the range of whatever granted them - a unique's
    /// own row, a runeword - which is why a unique's brackets have always matched line for line.
    /// </para>
    /// <para>
    /// A corruption's or a desecration's numbers are in no bracket, its red mark says the line is touched: "+40 to Life*"
    /// bare on a Stone of Jordan, "Cold Resist +87%* [40 - 50]" on a Stormshield, the shield's own range, and "+30 to Life*
    /// [1 - 40]" on a rare, the union of what its base could roll (screenshots); a Cat's Eye's
    /// "+79% Enhanced Damage**". A desecration's own lines are that one's, the rest by likeness.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<string, RollRange> DisplayedFor(Item item)
    {
        var shown = new Dictionary<string, RollRange>(Build(item, own: true, added: true, marks: false), StringComparer.OrdinalIgnoreCase);
        var reached = MagicAffixes.InBracketOf(item);

        if (reached.Count == 0)
            return shown;

        // What a corruption wrote is on the item, so it has a union to print though nothing else gave the line.
        var marked = Corruptions.StatsOn(item).Concat(Desecrations.StatsOn(item)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var union = new Dictionary<string, RollRange>(StringComparer.OrdinalIgnoreCase);
        var bare = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var option in reached)
        {
            foreach (var (code, param, min, max) in option.Grants)
            {
                // A row with nothing in any of the three columns has no range to offer, and
                // read as one it widens every bracket down to zero. The parameterised rows -
                // of Self-Repair and its kind - keep their number and are handled by the slot.
                if (min == 0 && max == 0 && param == 0)
                    continue;

                if (!_properties.TryGetValue(code, out var slots))
                    continue;

                var mod = new Mod(code, min, max, HasRange: true, Param: param);

                foreach (var slot in slots)
                {
                    if (slot.Unknowable(mod))
                        continue;

                    // A packed property is never bracketed. The game prints "14% Chance to
                    // cast level 5 Nova when struck" bare, on an ilvl 62 glove that could
                    // have rolled all three of Nova Shield's tiers - 10, 12 and 14 - so it
                    // had a union to draw and drew none. Measured on two items.
                    // It reads sensibly: the line is two numbers already, and a bracket
                    // after it would not say which one it belonged to.
                    if (slot.IsPacked)
                        continue;

                    string key = KeyOf(slot.Stat, slot.StoredParam(mod));
                    var range = slot.RangeOf(mod);

                    // A plain number whose row carries a parameter the stat has nowhere to keep is
                    // drawn bare: a map's "+141% of Physical Damage as Extra Poison Damage over 5
                    // Seconds" has no bracket in game, where every line round it has
                    // (a screenshot). Its row is written with a length, 125, and the stat
                    // stores none - the game's union is kept by stat and parameter, and the line's
                    // own answers to no entry of it. Of every spawnable affix only two properties
                    // are of this shape, both maps': extra poison (125) and extra cold (25).
                    if (slot.Func == "1" && mod.Param != 0 && StatFields.ParamMaximumOf(slot.Stat) is null)
                    {
                        bare.Add(key);
                        continue;
                    }

                    // An added element's maximum is the highest prefix's, its foot and its top, no suffix in it: Death Eye's fire
                    // reads [1 - 306 to 325 - 432] and Grim Eye's lightning [1 - 6 to 433 - 576], Scorching's and Shocking's
                    // columns; arrows' cold [1 - 118 to 226 - 348], Hibernal's, beside the suffix of Hypothermia's 238 - 365,
                    // whose 118 the minimum does take (screenshots).
                    bool elementTop = slot.Stat is "firemaxdam" or "lightmaxdam" or "coldmaxdam";
                    if (elementTop && !option.IsPrefix)
                        continue;

                    union[key] = union.TryGetValue(key, out var already)
                        ? new RollRange(
                            elementTop ? Math.Max(already.Min, range.Min) : Math.Min(already.Min, range.Min),
                            Math.Max(already.Max, range.Max))
                        : range;
                }
            }
        }

        // Only stats the item actually carries a source for. A reachable affix the item did
        // not roll has nothing on the item to bracket, and adding it would answer for stats
        // this item does not have.
        //
        // A union that lies wholly below zero is drawn up to nought: a tier-3 map's "Players have -19%
        // Chance to Block" reads [-40 - 0] in game, where its three rows run -40 to -10 and nothing that
        // writes the stat reaches nought (a screenshot - the other eight lines of
        // that map agreed as they stood). As a largest-of that starts from nought would. One line, one
        // item: the other all-negative lines - a map's life drain, resistances, defence - are to be looked at.
        foreach (var (key, range) in union)
        {
            if (shown.ContainsKey(key) || marked.Contains(key.Split('#')[0]))
                shown[key] = range.Max < 0 ? new RollRange(range.Min, 0) : range;
        }

        foreach (string key in bare)
            shown.Remove(key);

        // A line that does not print its number has no bracket after it. Not a clamp to the field - the game
        // draws Monster Rarity's [30 - 60] over a five-bit stat that holds 31 - but the wording: a dungeon's
        // "Monsters have Cannot Be Frozen", written by the Scarab's 10-20 beside the monsters' life, and a
        // staff's "Repairs 1 durability in 33 seconds" both read bare in game (screenshots)
        // where the editor drew [10 - 20] and [3 - 5]. See StatText.PrintsItsValue.
        foreach (string key in shown.Keys.ToList())
        {
            if (!StatText.PrintsItsValue(key.Split('#')[0]))
                shown.Remove(key);
        }

        return shown;
    }

    private static Dictionary<string, RollRange> Build(
        Item item, bool own, bool added, int? setTier = null, Drawing? drawn = null, bool marks = true)
    {
        var found = new Dictionary<string, RollRange>(StringComparer.OrdinalIgnoreCase);

        var sources = new List<(IEnumerable<int> Ids, Dictionary<int, List<Mod>> Table)>
        {
            (item.MagicPrefixIds?.Select(id => (int)id) ?? [], _prefixes),
            (item.MagicSuffixIds?.Select(id => (int)id) ?? [], _suffixes),
        };

        // Asked for the added sources alone, nothing the item rolled for itself counts -
        // and that has to be decided here, above every merge below rather than after them.
        // Below the staffmod merge, a Leaf staff reported its runeword as granting 4-6 Fire
        // Bolt: the staffmod's own 1-3 had been folded into the answer, and the item was
        // then a forgery by its own editor twice over.
        if (!own)
        {
            AddedOnly(item, found, Gain, setTier, drawn);
            return found;
        }

        // An auto prefix is not a magic affix and is stored separately, but it grants
        // properties the same way and its range is as real.
        if (item.IsAutoAffix && item.AutoAffixId > 0)
            sources.Add(([item.AutoAffixId], _automagic));

        // The one source that is not a table row. A class-specific base rolls its own
        // skill and no affix grants it, so without this the property carries no range at
        // all - which is what it did.
        //
        // Registered against the skills the item's <b>own</b> property list names, which is
        // where a staffmod lives - a runeword's are in the list after it. Registered against
        // the bare stat name instead, a Leaf staff's own +2 Fire Bolt was ranged against the
        // runeword's +3 Fire Bolt and marked as a number the game would not have rolled.
        if (StaffMods.Rolls(item))
        {
            foreach (var rolled in item.StatLists.FirstOrDefault()?.Stats ?? [])
            {
                if (string.Equals(rolled.Stat, StaffMods.Stat, StringComparison.OrdinalIgnoreCase))
                    Gain(KeyOf(rolled), StaffMods.Levels);
            }
        }

        // Two sources granting the same stat add up in game, so their ranges add up too -
        // but only where it really is the same stat. See KeyOf.
        void Gain(string key, RollRange range)
        {
            found[key] = found.TryGetValue(key, out var already)
                ? new RollRange(
                    already.Min + range.Min,
                    already.Max + range.Max,
                    already.Sources + 1)
                : range;
        }

        void Merge(List<Mod> mods)
        {
            foreach (var mod in mods)
            {
                if (!_properties.TryGetValue(mod.Code, out var slots))
                    continue;

                foreach (var slot in slots)
                {
                    // A row with blank min and max is kept but has no range - except where the
                    // slot takes its number from the parameter, which is then the number stored:
                    // Spike Thorn's thorns/lvl par 240 holds 240. Skipped, a corruption adding
                    // 32-48 to it was the only range left, and the sum read as out of it.
                    if (!mod.HasRange && !slot.Parameterised(mod))
                        continue;

                    if (slot.Unknowable(mod))
                        continue;

                    drawn?.Note(slot, mod);

                    Gain(KeyOf(slot.Stat, slot.StoredParam(mod)), DerivedCharges(item, slot, mod) ?? slot.RangeOf(mod));
                }
            }
        }

        // Asked for one part of the item only: what a runeword or a set added, and nothing
        // the item rolled for itself.
        // Superior is a source too, and one with no affix to point at: the bonus comes
        // from qualityitems.txt and is otherwise a property nothing explains.
        foreach (var (code, min, max) in QualityItems.For(item))
            Merge([new Mod(code, min, max)]);

        // A corruption is a source like any other, and one that can land on a property the
        // item already had: two gloves with identical affixes, one corrupted, read
        // item_armor_percent 16 and 67, and the corruption's own range is 50-80. The
        // honest range for the corrupted one is the two added, 60-100, which 67 sits
        // inside. Ranging it against the affix alone calls a real item impossible.
        foreach (var grant in marks ? Corruptions.On(item) : [])
        {
            Merge([new Mod(grant.Code, grant.Min, grant.Max, Param: grant.Param)]);
        }

        // A desecration too, once its mod is told - it is stored nowhere, see Desecrations.
        foreach (var grant in marks ? Desecrations.On(item) : [])
            Merge([new Mod(grant.Code, grant.Min, grant.Max, Param: grant.Param)]);

        // And a Horadric Cube upgrade's mod, for each tier climbed: a staff's +20% cast rate
        // on top of the automagic it dropped with. See BaseUpgrades.
        foreach (var grant in BaseUpgrades.Grants(item))
            Merge([new Mod(grant.Code, grant.Min, grant.Max, Param: grant.Param)]);

        // What the cube did to a map besides corrupting it - heroic, fortified, an event, an ear - and
        // what a dungeon is made with. See MapRecipes.
        foreach (var grant in MapRecipes.Grants(item))
            Merge([new Mod(grant.Code, grant.Min, grant.Max, Param: grant.Param)]);

        // A unique or set item carries no affix at all - its properties come from its own
        // row, found by the same FileIndex its name resolves through. Row zero is a real
        // row here, unlike an affix id, so this is asked outside the loop below.
        if (FixedRowsOf(item) is { } rows)
            Merge(rows);

        // What a runeword or a set added, where this was asked for the whole item.
        if (added)
            AddedOnly(item, found, Gain, drawn: drawn);

        // The affixes go in before the craft below, not after: a craft range is decided
        // against what the affixes already account for, and asking that of an empty map
        // put the two together whichever way the craft was merged.
        foreach (var (ids, table) in sources)
        {
            foreach (int id in ids)
            {
                if (id > 0 && table.TryGetValue(id, out var mods))
                    Merge(mods);
            }
        }

        // A crafted item's own properties come from one recipe. Where that recipe can be
        // told from what the item carries, only it counts - and its ranges add to the
        // affixes', because both really did contribute to the number stored.
        if (RecipeBehind(item) is { } recipe)
        {
            foreach (var mod in recipe.Mods)
            {
                if (!mod.HasRange || !_properties.TryGetValue(mod.Code, out var slots))
                    continue;

                foreach (var slot in slots)
                {
                    if (slot.Unknowable(mod))
                        continue;

                    Gain(slot.Stat, slot.RangeOf(mod));
                }
            }
        }
        else if (CraftsFor(item) is { Count: > 0 } recipes)
        {
            // Nothing identified the recipe, so the honest range is the widest any of them
            // could have granted. Added to the affix range it would put the floor above
            // values the game rolls - a Sturdy belt's 24% armour against a floor of 41,
            // because another recipe grants armour too and neither made this belt. So the
            // floor stays where the affixes put it and only the ceiling opens up: the
            // property is either the affix alone or the affix and a craft together.
            var widest = new Dictionary<string, RollRange>(StringComparer.OrdinalIgnoreCase);

            foreach (var mod in recipes.SelectMany(each => each.Mods))
            {
                if (!mod.HasRange || !_properties.TryGetValue(mod.Code, out var slots))
                    continue;

                foreach (var slot in slots)
                {
                    if (slot.Unknowable(mod))
                        continue;

                    var span = slot.RangeOf(mod);

                    widest[slot.Stat] = widest.TryGetValue(slot.Stat, out var already)
                        ? new RollRange(
                            Math.Min(already.Min, span.Min),
                            Math.Max(already.Max, span.Max))
                        : span;
                }
            }

            foreach (var (stat, span) in widest)
            {
                found[stat] = found.TryGetValue(stat, out var had)
                    ? new RollRange(had.Min, had.Max + span.Max, had.Sources + 1)
                    : span;
            }
        }

        return found;
    }

    /// <summary>
    /// The function that takes its value from the affix's <b>parameter</b> rather than from
    /// its min and max.
    /// <para>
    /// Fifty-one stats are written this way - every <c>/lvl</c> stat, and the poison and
    /// cold lengths. <c>dmg-pois</c> names three: <c>poisonmindam</c> and
    /// <c>poisonmaxdam</c> take the roll, and <c>poisonlength</c> takes the frame count
    /// from the param. Giving all three the same range marked a perfectly ordinary rare
    /// knife, whose <c>of Blight</c> rolled 8 damage over 50 frames: 8 is inside 8-8, and
    /// 50 is not.
    /// </para>
    /// </summary>
    private const string FromParameter = "17";

    /// <summary>
    /// <c>sock</c>, the socket property. Its row gives the count as a range (Mechanist's 1-2,
    /// Witchwild String 2-4) or, as vanilla does, as the parameter with no range at all
    /// (Artificer's 3, Jeweler's 4, Hone Sundan 3) - the same "no range, so the parameter" rule as
    /// <see cref="FromParameter"/>. Read as a range, the parameter form was a count of nought.
    /// </summary>
    private const string SocketsFunction = "14";

    /// <summary>
    /// Func 17 is two different things, told apart by <b>where in the row it sits</b>.
    /// <para>
    /// On the first stat it is the whole of the property, and the value is the roll: the
    /// forty-five <c>/lvl</c> properties are all this shape, and <c>thorns/lvl 32-48</c>
    /// means four to six per level, the table keeping per-level figures in eighths. The
    /// wiki prints it as "Attacker Takes Damage of [4-594] ([4-6] per Level)", which is
    /// 32 and 48 over eight.
    /// </para>
    /// <para>
    /// On a later stat it is the tail of a multi-part damage property - the length of a
    /// cold or poison effect - and that one does come from the parameter. Treating every
    /// func 17 as a parameter, which is what this did, meant no <c>/lvl</c> property wrote
    /// anything at all: the thorns corruption marked an item corrupted and granted it
    /// nothing.
    /// </para>
    /// </summary>

    /// <summary>The two functions that copy a field rather than roll between them.</summary>
    private const string FromMinimum = "15";
    private const string FromMaximum = "16";

    /// <summary>
    /// The stat a property writes when its row names none - <b>for wording only</b>.
    /// <para>
    /// <c>dmg-max</c> is <c>func1=6</c> and nothing else, so nothing in the row says what a
    /// player would be shown. PD2's own affix list words it "+[X] to Maximum Damage" and
    /// "+[X-Y]% Enhanced Damage", which are the descriptions itemstatcost carries for
    /// <c>maxdamage</c> and <c>item_maxdamage_percent</c> - so the mapping is sound enough
    /// to read an affix by.
    /// </para>
    /// <para>
    /// <b>Deliberately not used for ranges.</b> A rare Throwing Knife whose only max-damage
    /// affix is of Quality at 2-2 carries <c>maxdamage 3</c>, and its Jagged at 10-20%
    /// leaves no enhanced damage on the item at all. Something about how a throwing weapon
    /// takes a damage affix is not understood, and until it is, these name a stat without
    /// claiming to bound it - see <see cref="For"/>, which does not consult this.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> WordedByFunction = new()
    {
        ["5"] = "mindamage",
        ["6"] = "maxdamage",
        ["7"] = "item_maxdamage_percent",

        // Indestructible: the property names no stat, and unworded a runeword's line read "indestruct 1"
        // (the rune wizard's tooltips).
        ["20"] = "item_indesctructible",
    };

    private static Dictionary<string, string> _functionOf = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The stat to word a property by when its row names none - the damage ones, which are
    /// written by function. <b>For wording only</b>, never for a range: see the note on
    /// <see cref="WordedByFunction"/>. Without it a corruption list reads "dmg%" where the
    /// game reads "+40-80% Enhanced Damage".
    /// </summary>
    public static string? WordingFor(string code) =>
        _functionOf.TryGetValue(code, out string? func)
        && WordedByFunction.TryGetValue(func, out string? worded)
            ? worded
            : null;

    /// <summary>
    /// The parameter one stat of a property is kept with on an item, given the row's own - the class a
    /// class-skill property grants to, which is the property's and in no row; a skill tree in the item's form.
    /// What a line of it is worded by: read with the row's parameter, every class's skills were the Amazon's
    /// and every tree her first (the rune wizard's tooltips: Bone "to Amazon Skill Levels").
    /// </summary>
    public static int StoredParamFor(string code, string stat, int param) =>
        _properties.TryGetValue(code, out var slots)
        && slots.FirstOrDefault(each => string.Equals(each.Stat, stat, StringComparison.OrdinalIgnoreCase)) is { } slot
            ? slot.StoredParam(new Mod(code, 0, 0, true, param))
            : param;

    /// <summary>
    /// The range one stat of a property stands for, given the row's numbers - which is often
    /// not the row's own min and max.
    /// <para>
    /// The row says 3 and 6; <c>dmg-fire</c> gives 3 to <c>firemindam</c> and 6 to
    /// <c>firemaxdam</c>, neither of which rolls, and <c>rep-dur</c> gives neither because
    /// its number is in the parameter. Wording a part with the row's pair said a minimum
    /// fire damage of "3-6" and a self-repair of nothing at all.
    /// </para>
    /// </summary>
    public static RollRange RangeFor(string code, string stat, int min, int max, int param)
    {
        if (!_properties.TryGetValue(code, out var slots))
            return new RollRange(min, max);

        var slot = slots.FirstOrDefault(each =>
            string.Equals(each.Stat, stat, StringComparison.OrdinalIgnoreCase));

        if (slot is null)
            return new RollRange(min, max);

        // Said as the item will hold it: inside what the stat's field goes to, both ends (see Held).
        var range = slot.RangeOf(new Mod(code, min, max, true, param));
        return !slot.IsPacked && StatFields.RangeOf(stat) is { } field
            ? new RollRange(Math.Clamp(range.Min, field.Min, field.Max), Math.Clamp(range.Max, field.Min, field.Max))
            : range;
    }

    /// <summary>
    /// Whether a property's two numbers are two different fields rather than the ends of a
    /// range: a chance and a level, or charges and a level.
    /// </summary>
    public static bool IsPackedCode(string code) =>
        _properties.TryGetValue(code, out var slots) && slots.Length > 0 && slots[0].IsPacked;

    /// <summary>
    /// A property's two numbers in the order the property reads them: sorted where they are
    /// the ends of a range, left exactly as the row wrote them where they are not.
    /// <para>
    /// Sorting is right for every other property and wrong for these. <c>of Charged Shield</c>
    /// is written min 10, max 3 - a tenth of the time, level three - and sorted into 3 and 10
    /// it became a three percent chance to cast level ten, which is not an affix the game
    /// rolls. Settled against an item off the character rather than by reasoning: the cap
    /// holds <c>item_skillongethit</c> at 10 with level 3 beside it.
    /// </para>
    /// </summary>
    public static (int Min, int Max) Ordered(string code, int min, int max) =>
        IsPackedCode(code) ? (min, max) : (Math.Min(min, max), Math.Max(min, max));

    /// <summary>
    /// What a property writes when it is granted with this range and this roll: the stats
    /// it names, each with the number that lands on the item.
    /// <para>
    /// A property that rolls writes <b>the same roll to every stat it names</b> - a
    /// <c>dmg%</c> of 13 is <c>item_maxdamage_percent 13</c> and
    /// <c>item_mindamage_percent 13</c>, seen on a real quiver. The two functions that copy
    /// a field take theirs instead: func 15 the minimum, func 16 the maximum, which is how
    /// <c>dmg-fire 3-6</c> becomes 3 minimum and 6 maximum fire damage.
    /// </para>
    /// </summary>
    /// <param name="param">
    /// The affix's own parameter, which is where a cold or poison <b>length</b> comes from.
    /// Left at zero, such a property writes its damage and no duration - poison that never
    /// ticks.
    /// </param>
    public static IReadOnlyList<Wrote> Written(
        string code, int min, int max, int roll, int param = 0)
    {
        if (_properties.TryGetValue(code, out var slots))
            return [.. slots.Select(slot => Held(Fill(slot, min, max, roll, param)))];

        // A row that names no stat still writes one when its function says so, and those
        // rows are deliberately kept out of the map above so that nothing offers them a
        // roll range they do not have. That left them accountable but unwritable, which
        // only showed when an item was built from a row rather than read from a file:
        // Tyrael's Might came out without Indestructible, because `indestruct` is func 20
        // with no stat1 of its own.
        //
        // Only the flag-like functions are written here. Func 25 - Ormus' Robes' random skill and its
        // cast - is written by RandomSkillCast, which draws the skill: no roll here names one.
        if (_functionOf.TryGetValue(code, out string? func) && FlagFunctions.Contains(func))
            return [.. StatsOfFunction(func).Select(stat => new Wrote(stat, roll != 0 ? roll : 1))];

        return [];
    }

    /// <summary>
    /// Stat-less functions that write a plain flag, so they can be written from their row
    /// without a parameter or a roll.
    /// </summary>
    private static readonly HashSet<string> FlagFunctions =
        new(StringComparer.OrdinalIgnoreCase) { "20" };

    /// <summary>
    /// The function a property row carries, or null for one this season does not have.
    /// <para>
    /// A caller building an item from a row needs this for the handful of functions that
    /// change the item itself rather than adding a property to it -
    /// <see cref="EtherealFunction"/> being the one that matters.
    /// </para>
    /// </summary>
    public static string? FunctionOf(string code) =>
        _functionOf.TryGetValue(code, out string? func) ? func : null;

    /// <summary>
    /// How many sockets a unique's or set piece's own row bores - the most, where it rolls a range
    /// (Blade of Ali Baba 2-3) rather than naming a count (Hone Sundan 3, in <c>par</c>) - or null
    /// for a row that bores none.
    /// </summary>
    public static int? OwnSockets(Item item)
    {
        int most = (FixedRowsOf(item) ?? [])
            .Where(mod => mod.Code.Equals("sock", StringComparison.OrdinalIgnoreCase))
            .Select(mod => mod.Param > 0 ? mod.Param : Math.Max(mod.Min, mod.Max))
            .DefaultIfEmpty(0)
            .Max();

        return most > 0 ? most : null;
    }

    /// <summary>
    /// The sockets the item's own source rolls, where it rolls a range and not a count: a unique's or set
    /// piece's row (Blade of Ali Baba 2-3, Natalya's Shadow 1-3 - fourteen uniques and six set pieces in S13)
    /// or a socket affix (Mechanist's 1-2, the one that rolls). Both ends held to what the base takes at its
    /// item level (<see cref="ItemBases.MaximumSockets"/>), as the game holds them. Null where nothing rolls:
    /// a fixed count (Artificer's 3, in <c>par</c>), no socket source of its own, or a range the base's
    /// ceiling closes to one number.
    /// <para>
    /// The game rolls it once, when the item is made: fifty Demonic Cube rerolls of a Blade of Ali Baba left
    /// its sockets alone. The Edit page's die is the editor's own.
    /// </para>
    /// </summary>
    public static (int Least, int Most)? SocketRange(Item item)
    {
        if (SocketSpan(item) is not { } span)
            return null;

        int cap = ItemBases.MaximumSockets(item);
        int low = Math.Min(span.Least, cap), high = Math.Min(span.Most, cap);
        return low > 0 && low < high ? (low, high) : null;
    }

    /// <summary>
    /// The sockets the item's own source gives, as the table has them: a range (Blade of Ali Baba 2-3,
    /// Mechanist's 1-2) or a count named outright in <c>par</c>, which is a range of one (Artificer's 3-3). Not
    /// held to the base - <see cref="SocketRange"/> and <see cref="SocketRolls"/> do that. Null for an item
    /// with no socket source of its own.
    /// </summary>
    public static (int Least, int Most)? SocketSpan(Item item)
    {
        static (int Least, int Most)? Of(string code, int param, int min, int max)
        {
            if (!code.Equals("sock", StringComparison.OrdinalIgnoreCase))
                return null;

            var span = param > 0 ? (Least: param, Most: param) : (Least: Math.Min(min, max), Most: Math.Max(min, max));
            return span.Most > 0 ? span : null;
        }

        foreach (var mod in FixedRowsOf(item) ?? [])
        {
            if (Of(mod.Code, mod.Param, mod.Min, mod.Max) is { } span)
                return span;
        }

        foreach (bool prefix in new[] { true, false })
        {
            foreach (int id in (prefix ? item.MagicPrefixIds : item.MagicSuffixIds) ?? [])
            {
                if (id == 0 || MagicAffixes.Find(id, prefix) is not { } affix)
                    continue;

                foreach (var grant in affix.Grants)
                {
                    if (Of(grant.Code, grant.Param, grant.Min, grant.Max) is { } span)
                        return span;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// A unique's or set piece's own row, by the FileIndex it keeps - and Kadala's Heirloom's
    /// variant, whose row gives nothing (see <see cref="KadalasHeirloom"/>).
    /// </summary>
    private static List<Mod>? FixedRowsOf(Item item)
    {
        var table = item.Quality switch
        {
            ItemQuality.Unique => _uniques,
            ItemQuality.Set => _setItems,
            _ => null,
        };

        if (table is null)
            return null;
        if (table.TryGetValue((int)item.FileIndex, out var rows))
            return rows;
        if (!KadalasHeirloom.Is(item))
            return null;

        return
        [
            .. KadalasHeirloom.GrantsFor(item).Select(grant =>
            {
                var (first, second) = Ordered(grant.Code, grant.Min, grant.Max);
                return new Mod(grant.Code, first, second, true, grant.Param);
            }),
        ];
    }

    /// <summary>The stat <c>reanimate</c> writes.</summary>
    private const string ReanimateStat = "item_reanimate";

    /// <summary>
    /// Undoes what the PD2 site does to a source's two "Reanimate as" lines. The game writes two: a Sacred Totem the
    /// Demonic Cube remade in game holds "6% Reanimate as: Fallen" and "3% Reanimate as: Fallen Shaman"
    /// (the row's 19 at 4-6% and 58 at 2-3%). The site's armory and trade data give one line, its monster
    /// the two parameters' sum and its chance the first's - "Reanimate as: 77", Serpent Magus, on 42 of 42 ladder
    /// totems - which the editor took for the game's until that cube. Such a line on an item built from the site is
    /// split back: the first row's monster at the line's chance, each other at its row's top, which the site does not
    /// say.
    /// </summary>
    public static void SplitSiteReanimate(Item item)
    {
        var rows = ModsOn(item, withCorruption: false)
            .Where(mod => _properties.TryGetValue(mod.Code, out var slots)
                          && slots.Any(slot => string.Equals(slot.Stat, ReanimateStat, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (rows.Count < 2)
            return;

        int merged = rows.Sum(mod => mod.Param);
        foreach (var list in item.StatLists)
        {
            int at = list.Stats.FindIndex(stat => string.Equals(stat.Stat, ReanimateStat, StringComparison.OrdinalIgnoreCase) && stat.Param == merged);
            if (at < 0 || list.Stats.Any(stat => string.Equals(stat.Stat, ReanimateStat, StringComparison.OrdinalIgnoreCase) && stat.Param == rows[0].Param))
                continue;

            var line = list.Stats[at];
            line.Param = rows[0].Param;
            foreach (var other in rows.Skip(1))
                list.Stats.Insert(++at, new ItemStat { Id = line.Id, Stat = line.Stat, Param = other.Param, Value = Math.Max(other.Min, other.Max) });
            return;
        }
    }

    /// <summary>
    /// The function that makes an item ethereal. It names no stat because ethereal is not
    /// one: it is a flag on the item, and the defence it raises is folded into the stored
    /// number rather than applied at runtime.
    /// </summary>
    public const string EtherealFunction = "23";

    /// <summary>
    /// A plain number held at what its field holds, as the game holds it: a row may ask for more than
    /// the stat's bits can keep, and the item then carries the field's top. A map's Monster Rarity is five
    /// bits - 31 - and its rows ask for up to 90: the three Imperial Palaces on the market all read 31
    /// where their row says 50, as every Eaglehorn reads 63 ravens of 1250. A chance that
    /// packs a skill beside it is held the same way - its number is the stat's own field, 127 at most
    /// - but not charges, whose number is two fields in one.
    /// </summary>
    private static Wrote Held(Wrote wrote)
    {
        if (wrote.MaxCharges is not null || StatFields.RangeOf(wrote.Stat) is not { } field)
            return wrote;

        // Both ends: under the floor the writer's low bits wrap. A magic T3 map's "of Disintegration" rolls -75 to -60 on a
        // field whose Save Add is 60, so -70 read back as +16314%; the file cannot hold it.
        int held = Math.Clamp(wrote.Value, field.Min, field.Max);
        return held == wrote.Value ? wrote : wrote with { Value = held };
    }

    private static Wrote Fill(Slot slot, int min, int max, int roll, int param) =>
        slot.Func switch
        {
            // Flat in the row, split in the stat. See TreeIndex.
            TreeIndex => new Wrote(
                slot.Stat, roll, Param: param / 3 * 8 + param % 3),

            // Inverted: the level is in the row's parameter and the roll is the skill id.
            SkillFromRange => new Wrote(slot.Stat, param, Param: roll),

            // The parameter is the skill it grants; the value is how many levels of it.
            SkillParam => new Wrote(slot.Stat, roll, Param: param),

            // The parameter is the monster raised or the state put on; the value is the chance, or the state's 1.
            SubjectParam => new Wrote(slot.Stat, roll, Param: param),

            // The roll is the class; the levels are the property's own. See RandomClass.
            RandomClass => new Wrote(slot.Stat, slot.Val, Param: roll),

            // The class or the element is the property's val1. See FromCode.
            FromCode => new Wrote(slot.Stat, roll, Param: slot.Val),

            // How often it fires, the skill, and the level it casts at. A parameter of
            // nothing names no skill - a unique's row may give it as a name rather than an
            // id - and claiming skill zero would put Attack on an item that casts Nova, so
            // the number is written and the skill left alone.
            //
            // The magnitude, because an affix writes these negative. Of the rows granting a
            // charged skill, 200 are written with both numbers negative and 5 positive,
            // while every one of the 34 on uniques, sets and runewords is positive - so the
            // sign is the table's own convention and the number is what it says. Read
            // straight, "of Telekinesis" offered a range of -20 charges, the box took it,
            // and the game read it back as 236.
            PackedCast => param > 0
                ? new Wrote(slot.Stat, Math.Abs(min), param, Math.Abs(max))
                // No skill in the row still has a level: a real Fallen Gardens stores its
                // map-mon-splash (row 1 / 1, no parameter) as chance 1, skill 0, level 1, three
                // maps alike on the PD2 market. Written without it the level came out 0.
                : new Wrote(slot.Stat, Math.Abs(min), 0, Math.Abs(max)),

            // How many charges, the skill, and the level - and an item leaves the smith with
            // its charges full.
            PackedCharges => param > 0
                ? new Wrote(slot.Stat, Math.Abs(min), param, Math.Abs(max), Math.Abs(min))
                : new Wrote(slot.Stat, Math.Abs(min)),

            // A row with no range at all hands its number over in the parameter, whichever
            // slot the property sits in. See Slot.Parameterised.
            FromParameter or SocketsFunction when min == 0 && max == 0 && param > 0 => new Wrote(slot.Stat, param),

            _ => new Wrote(
                slot.Stat,
                slot.TakesParam ? param
                : slot.TakesMin ? min
                : slot.TakesMax ? max
                : roll),
        };

    /// <summary>
    /// Everything on this item that something accounts for, whether or not a range can be
    /// put to it.
    /// <para>
    /// Three kinds of property have a known source and no range. The damage functions name
    /// no stat at all and <b>fan out</b> - one <c>dmg-max</c> writes <c>maxdamage</c>,
    /// <c>secondary_maxdamage</c> and <c>item_throw_maxdamage</c>, all the same number,
    /// measured on a real amulet. A func-17 stat takes its value from the affix's parameter
    /// rather than from a roll, so <c>of Blight</c> writes a <c>poisonlength</c> its range
    /// cannot describe. And the two a corruption writes for its own bookkeeping are the
    /// corruption itself.
    /// </para>
    /// <para>
    /// Kept apart from the ranges on purpose: naming a source is not the same as claiming
    /// what it could have rolled, and the second is what marks an item forged.
    /// </para>
    /// </summary>
    public static IReadOnlySet<string> Accounted(Item item) => Accounted(item, For(item).Keys);

    /// <summary>
    /// <see cref="Accounted(Item)"/> given the stats the item's sources range - its own and what was added, which the
    /// caller has worked out already: the legitimacy check built them a third time for their names alone.
    /// </summary>
    public static IReadOnlySet<string> Accounted(Item item, IEnumerable<string> ranged)
    {
        var known = new HashSet<string>(ranged, StringComparer.OrdinalIgnoreCase)
        {
            Corruptions.IdStat,
            Corruptions.FlagStat,
            Desecrations.ValueStat,
            Desecrations.FlagStat,
        };

        // Lilith's Mirror: the recipe marks both the item and its copy `mirrored` and dyes them
        // black and bright white. A mirrored item carries both for that reason alone - the five
        // on the market were each called unaccounted for "Mirrored".
        if (Dyes.IsMirrored(item))
        {
            known.Add(Dyes.MirroredStat);
            known.Add(Dyes.Stat);
        }

        // A 5th Anniversary skin, which PD2 rolls in code on five uniques - no table grants it,
        // and all three skinned Shakos on the market were called unaccounted. See Skins.
        if (Skins.CanHave(item))
            known.Add(Skins.Stat);

        foreach (var code in CodesOn(item))
        {
            foreach (string stat in AllStatsOf(code))
                known.Add(stat);

            foreach (string stat in FannedOut(code))
                known.Add(stat);
        }

        // A superior weapon's damage bonus is rolled as a percentage and then written into
        // the item as a <b>flat</b> number. `qualityitems.txt` offers `dmg% 5-15`, and a
        // superior Stag Bow, whose two-handed maximum is 14, comes out of the game carrying
        // `secondary_maxdamage 1`. Nothing in the tables says the percentage becomes a flat
        // number, so nothing else here accounts for it.
        //
        // Measured before being believed: fifteen superior weapons in one stash, every one
        // holding a flat maximum inside 5-15% of its own base maximum, none outside - and
        // confirmed on a live S13 `Steel`, a superior Scimitar of base maximum 6 carrying
        // `maxdamage 1`.
        //
        // It went unnoticed because it only shows when nothing else on the item grants flat
        // damage. `Steel` looked accounted for all along, because its runeword writes
        // `dmg-norm` and that covers the same stats; `Zephyr`, whose runeword grants no flat
        // damage, had nowhere to hide it and read as unaccounted.
        //
        // Accounted rather than ranged: the source is certain, the arithmetic of the
        // rounding less so, and saying where a property came from is not the same as
        // claiming what it could have rolled. A range of floor(5%) to ceil(15%) of the
        // base's own maximum did fit all sixteen, if one is ever wanted.
        if (QualityItems.For(item).Any(bonus =>
                bonus.Code.Equals("dmg%", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (string stat in (string[])
                     [
                         "mindamage", "maxdamage",
                         "secondary_mindamage", "secondary_maxdamage",
                         "item_throw_mindamage", "item_throw_maxdamage",
                     ])
            {
                known.Add(stat);
            }
        }

        // A run's other members go where its leader goes. The save writes a run whole, so an
        // item holding coldmindam carries coldlength too, whether or not any row names it: a
        // real Odium, whose row grants cold-min and cold-max and never a length, stores colddam
        // [600, 900, 0] on the PD2 market. Read stat by stat, that length was "unaccounted" on
        // every Odium and Merman's Speed the forge made - and would have been on real ones.
        foreach (var line in ItemStats.Every(item))
        {
            if (line.Id is not { } id || line.Stat is not { Length: > 0 } name || !known.Contains(name))
                continue;

            foreach (int member in ItemStats.RunContaining(id))
            {
                if (StatCatalogue.NameOf(member) is { Length: > 0 } follower)
                    known.Add(follower);
            }
        }

        return known;
    }

    /// <summary>
    /// The stats a function writes when its property row names none at all. <b>Exactly six
    /// properties in the game are like this</b> - <c>dmg-min</c> (5), <c>dmg-max</c> (6),
    /// <c>dmg%</c> (7), <c>indestruct</c> (20), <c>ethereal</c> (23) and
    /// <c>sorc-skill-rand-ctc</c> (25) - and they are known only by what the function does.
    /// <para>
    /// Func 23 is deliberately absent: ethereal is a flag in the item's own header, not a
    /// property line, so there is nothing for it to write.
    /// </para>
    /// <para>
    /// One table, read by both the accounting and the ranges, because two copies of this is
    /// how a property ends up accounted for but unranged - which is exactly what Enhanced
    /// Damage was.
    /// </para>
    /// </summary>
    private static IReadOnlyList<string> StatsOfFunction(string func) => func switch
    {
        "5" => (string[])["mindamage", "secondary_mindamage", "item_throw_mindamage"],
        "6" => ["maxdamage", "secondary_maxdamage", "item_throw_maxdamage"],
        "7" => ["item_maxdamage_percent", "item_mindamage_percent"],

        // Indestructible, whose stat carries the game's own misspelling.
        "20" => ["item_indesctructible"],

        // A random skill of one class, granted and set to cast itself: `item_singleskill`
        // keeps the rolled skill in its parameter and `item_skilloncast` keeps the same
        // skill as the thing it casts.
        "25" => ["item_singleskill", "item_skilloncast"],

        _ => [],
    };

    /// <summary>
    /// The three of those that carry a genuine roll, so a range can be offered for them.
    /// <para>
    /// The damage functions take the granting row's own min and max: Zephyr's
    /// <c>dmg% 33-66</c> is a single roll written into <b>both</b> <c>item_maxdamage_percent</c>
    /// and <c>item_mindamage_percent</c>, which is why a real Zephyr reads 62 and 62 rather
    /// than two different numbers.
    /// </para>
    /// <para>
    /// The other two are left unranged on purpose. Indestructible is a flag, and func 25
    /// packs a skill, a level and a chance across columns that are not a range - answering
    /// either with <c>min</c> to <c>max</c> would invent a claim rather than report one.
    /// </para>
    /// </summary>
    private static readonly string[] RangeableFunctions = ["5", "6", "7"];

    /// <summary>
    /// The stats a property writes that its own row does not name.
    /// <para>
    /// Two different cases. The damage functions 5, 6 and 7 name no stat at all and are
    /// known only by what they do. And <b>a property that does name the ordinary damage
    /// stats writes the other two damage kinds as well</b>: a unique shield granting
    /// <c>dmg-norm 4-8</c> - one property, two stats named - comes out of the game carrying
    /// six, <c>mindamage</c> and <c>maxdamage</c> beside their <c>secondary_</c> and
    /// <c>item_throw_</c> partners. The table says nothing about it; the engine fans a
    /// normal-damage property across one-hand, two-hand and thrown.
    /// </para>
    /// <para>
    /// Keyed on the stat rather than the function, because the functions differ: 5 and 6
    /// reach it with no stat named, 15 and 16 reach it by naming one. What they have in
    /// common is the stat they land on.
    /// </para>
    /// <para>
    /// This only ever widens what counts as accounted for, so it cannot raise a false
    /// alarm - and a source that writes the plain stat without its partners, as a superior
    /// item's bonus does, is unaffected: nothing here requires the partners to be present.
    /// </para>
    /// </summary>
    private static IReadOnlyList<string> FannedOut(string code)
    {
        var found = new List<string>();

        if (_functionOf.TryGetValue(code, out string? func))
        {
            found.AddRange(StatsOfFunction(func));
        }

        foreach (string stat in AllStatsOf(code))
        {
            if (string.Equals(stat, "mindamage", StringComparison.OrdinalIgnoreCase))
                found.AddRange(["secondary_mindamage", "item_throw_mindamage"]);

            if (string.Equals(stat, "maxdamage", StringComparison.OrdinalIgnoreCase))
                found.AddRange(["secondary_maxdamage", "item_throw_maxdamage"]);
        }

        return found;
    }

    /// <summary>Every stat a property names, the parameter ones included.</summary>
    private static IReadOnlyList<string> AllStatsOf(string code) =>
        _everyStat.TryGetValue(code, out string[]? stats) ? stats : [];

    /// <summary>
    /// What a packed property's three fields should hold, taken from the row that granted it.
    /// <para>
    /// A chance to cast and a charged skill are the two properties that are three numbers in
    /// one stat, and the row is three columns to match: <c>par</c> is the <b>skill</b>,
    /// <c>min</c> is the <b>chance</b> (func 11) or the <b>max charges</b> (func 19), and
    /// <c>max</c> is the <b>level</b>. A <see cref="RollRange"/> cannot carry that - it keeps
    /// two numbers and sums them when two sources meet - so the fields come through here
    /// instead, and nothing about them is added or ranged.
    /// </para>
    /// <para>
    /// A key may have more than one answer where two sources write the same stat for the same
    /// skill. Any of them fitting is enough; the item holds one line and only one source put
    /// it there.
    /// </para>
    /// </summary>
    /// <param name="First">
    /// The row's <c>min</c>: the chance for a func 11, the <b>maximum</b> charges for a
    /// func 19. Not the current charges - those count down as the item is used.
    /// </param>
    public sealed record PackedFields(int Skill, int Level, int First);

    /// <summary>
    /// The level and the chance or charges a packed row gives on this item - which is the row's
    /// own numbers only where it writes them positive.
    /// <list type="bullet">
    /// <item><b>A charged row written negative</b> - the staff and wand mods, "of Teleportation"
    /// <c>charged 54 -30 -6</c> - derives both from the <b>item level</b> (not the affix level):
    /// level = (ilvl - the skill's <c>reqlevel</c>) / ((99 - reqlevel) / |max|), and charges =
    /// |min| + level x |min| / 8, all in whole numbers. A positive max is the level as it stands.
    /// Measured on 24 items in the two item packs, every one exact: Teleport at item
    /// level 95 holds level 5 with 48 charges, at 99 level 6 with 52, and a circlet at item level
    /// 63 whose affix level is 99 holds level 1.</item>
    /// <item><b>A chance to cast with level 0</b> - Snowclash's Blizzard, the only such row this
    /// season - is also levelled by item level: (ilvl - 20) / 4. Eleven Snowclashes at item level 85
    /// to 91 hold 16 or 17, and one at 90 holds 17, which (ilvl - 3) / 5 fits as
    /// well; the PD2 wiki's "Level [7-20]" decides it - 7 at the row's level 49, where the other gives
    /// 9 (deliberately closed on the wiki's data).</item>
    /// </list>
    /// </summary>
    public static IEnumerable<(int Level, int First)> PackedFrom(Item item, StatShape shape, int skill, int min, int max)
    {
        int ilvl = Math.Clamp((int)item.ItemLevel, 1, 99);

        if (shape == StatShape.Chance && max == 0)
        {
            yield return (Math.Max(1, (ilvl - 20) / 4), Math.Abs(min));
            yield break;
        }

        if (shape == StatShape.Charges && (min < 0 || max < 0))
        {
            int level = Math.Abs(max);
            if (max < 0)
            {
                int required = Math.Clamp(SkillNames.RequiredLevelOf(skill), 0, 98);
                int step = Math.Max(1, (99 - required) / Math.Max(1, -max));
                level = Math.Max(1, (ilvl - required) / step);
            }

            int charges = min < 0 ? -min + level * -min / 8 : min;
            yield return (level, charges);
            yield break;
        }

        // Absolute, the way RangeOf takes it: a row may write the pair negative and the stat
        // never is.
        yield return (Math.Abs(max), Math.Abs(min));
    }

    /// <summary>
    /// The charges a charged row written negative gives this item, as a range: what its item level gives
    /// (<see cref="PackedFrom"/>), where the row's own number is not it. A staff "of Teleportation" (<c>-30 -6</c>) at
    /// item level 99 holds 52 charges, and its row was held to 30 - the Edit page marked it "?" and said "affix 30"
    /// (an Amazon's). Null for any other row: its <see cref="Slot.RangeOf"/> stands.
    /// </summary>
    private static RollRange? DerivedCharges(Item item, Slot slot, Mod mod)
    {
        if (!slot.IsPacked || StatShapes.Of(slot.Stat) != StatShape.Charges || (mod.Min >= 0 && mod.Max >= 0))
            return null;

        var charges = PackedFrom(item, StatShape.Charges, mod.Param, mod.Min, mod.Max).Select(answer => answer.First).ToList();
        return charges.Count == 0 ? null : new RollRange(charges.Min(), charges.Max());
    }

    /// <summary>
    /// Puts right the packed lines a writer filled from the row's own numbers where the game
    /// works them out from the item level (<see cref="PackedFrom"/>): a staff's Teleport at
    /// level 6 with 30 charges becomes the level and charges its item level gives, and a
    /// forged Snowclash casts a level of Blizzard rather than level 0. A line already holding
    /// one of its sources' answers is left alone, and charges are left full.
    /// </summary>
    /// <param name="only">The stats the writer touched, or null for every line.</param>
    public static void DerivePacked(Item item, IReadOnlySet<string>? only = null)
    {
        var fields = PackedOn(item);

        foreach (var list in item.StatLists)
        {
            foreach (var stat in list.Stats)
            {
                var shape = StatShapes.Of(stat.Stat);
                if (shape is not (StatShape.Chance or StatShape.Charges)
                    || (only is not null && !only.Contains(stat.Stat ?? ""))
                    || !fields.TryGetValue(KeyOf(stat), out var wanted) || wanted.Count == 0)
                    continue;

                int level = stat.SkillLevel ?? 0;
                int carried = shape is StatShape.Chance ? stat.Value : stat.MaxCharges ?? 0;
                if (wanted.Any(want => want.Level == level && want.First == carried))
                    continue;

                var answer = wanted[0];
                stat.SkillLevel = answer.Level;
                if (shape is StatShape.Charges)
                {
                    stat.MaxCharges = answer.First;
                    stat.Value = answer.First;
                }
                else
                {
                    stat.Value = answer.First;
                }
            }
        }
    }

    /// <summary>
    /// Every packed property this item's sources grant, by the same key the item's own stats
    /// answer to. See <see cref="PackedFields"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<PackedFields>> PackedOn(Item item) =>
        PackedFieldsOf(item, ModsOn(item, withCorruption: true).Where(mod => mod.HasRange));

    /// <summary>
    /// The packed properties the item's fixed sources grant - a craft's row, a base upgrade's - as
    /// <see cref="PackedOn"/>, which leaves them out. Where the recipe cannot be told, every recipe it could have been.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<PackedFields>> PackedFixedOn(Item item) =>
        PackedFieldsOf(item, ModsOn(item, withCorruption: true).Where(mod => !mod.HasRange));

    private static IReadOnlyDictionary<string, IReadOnlyList<PackedFields>> PackedFieldsOf(Item item, IEnumerable<Mod> mods)
    {
        var found = new Dictionary<string, List<PackedFields>>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in mods)
        {
            if (!_properties.TryGetValue(mod.Code, out var slots))
                continue;

            foreach (var slot in slots)
            {
                if (!slot.IsPacked)
                    continue;

                string key = KeyOf(slot.Stat, mod.Param);

                if (!found.TryGetValue(key, out var answers))
                    found[key] = answers = [];

                // A chance is held at what its field holds, as a plain number is (Held): a magic map's
                // "Splashing" asks a 150% chance of seven bits, and the three rolled in game
                // hold 127 - read as 150 they were flagged.
                var shape = StatShapes.Of(slot.Stat);
                int top = shape == StatShape.Chance ? StatFields.CeilingOf(slot.Stat) ?? int.MaxValue : int.MaxValue;

                foreach (var (level, first) in PackedFrom(item, shape, mod.Param, mod.Min, mod.Max))
                    answers.Add(new PackedFields(mod.Param, level, Math.Min(first, top)));
            }
        }

        return found.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<PackedFields>)entry.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// What the automagic recorded on this item grants, rolled, in the form a stat line is
    /// built from.
    /// <para>
    /// An automagic is not an affix and is not stored as one - the item keeps a single
    /// <c>AutoAffixId</c> - but it writes properties exactly as an affix does, and they sit in
    /// the same property list. Anything that rebuilds that list has to put them back or the
    /// item is left claiming an automagic it does not carry. See <see cref="Crafts.CraftFrom"/>.
    /// </para>
    /// <para>
    /// **The recorded id is used, not a fresh pick.** Whether the game re-rolls the tier when
    /// it makes a crafted item is not known - a javelin crafted in game kept its
    /// <c>AutoAffixId 8</c>, but its level admitted all three tiers, so that settles nothing.
    /// Re-applying what the item records reproduces the observed item and invents no rule.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Wrote> AutomagicOn(Item item, Random random)
    {
        if (!item.IsAutoAffix || item.AutoAffixId <= 0
            || !_automagic.TryGetValue(item.AutoAffixId, out var mods))
        {
            return [];
        }

        var wrote = new List<Wrote>();

        foreach (var mod in mods)
        {
            // A packed row is not a range and must not be rolled inside: its min is the
            // chance or the charges. Splash is written `100-1` - a 100% chance of level 1 -
            // and rolling between those two writes nonsense.
            int roll = IsPackedCode(mod.Code) || !mod.HasRange || mod.Max <= mod.Min
                ? mod.Min
                : random.Next(mod.Min, mod.Max + 1);

            wrote.AddRange(Written(mod.Code, mod.Min, mod.Max, roll, mod.Param));
        }

        return wrote;
    }

    /// <summary>Every property code anything on this item grants.</summary>
    private static IEnumerable<string> CodesOn(Item item) =>
        ModsOn(item, withCorruption: true).Select(mod => mod.Code);

    /// <summary>
    /// The random skill casts an item's own sources grant (<see cref="RandomSkillCast"/>, Ormus' Robes): each row's
    /// <c>par</c>, <c>min</c> and <c>max</c>, for the legitimacy check to hold the two lines to.
    /// </summary>
    public static IEnumerable<(int Par, int Min, int Max)> RandomSkillCastsOn(Item item) =>
        ModsOn(item, withCorruption: false)
            .Where(mod => FunctionOf(mod.Code) == RandomSkillCast.Function)
            .Select(mod => (mod.Param, mod.Min, mod.Max));

    /// <summary>
    /// Every property the item's sources grant, as the rows themselves rather than as
    /// codes - which is what rolling one again needs.
    /// </summary>
    private static IEnumerable<Mod> ModsOn(Item item, bool withCorruption, RollScope scope = RollScope.All)
    {
        // A runeword's own, alone: what its list holds (RollScope).
        if (scope == RollScope.Runeword)
        {
            foreach (var mod in RunewordOn(item) ?? [])
                yield return mod;
            yield break;
        }

        var sources = new List<(IEnumerable<int> Ids, Dictionary<int, List<Mod>> Table)>
        {
            (item.MagicPrefixIds?.Select(id => (int)id) ?? [], _prefixes),
            (item.MagicSuffixIds?.Select(id => (int)id) ?? [], _suffixes),
        };

        if (item.IsAutoAffix && item.AutoAffixId > 0)
            sources.Add(([item.AutoAffixId], _automagic));

        foreach (var (ids, table) in sources)
        {
            foreach (int id in ids)
            {
                if (id > 0 && table.TryGetValue(id, out var mods))
                {
                    foreach (var mod in mods)
                        yield return mod;
                }
            }
        }

        if (FixedRowsOf(item) is { } own)
        {
            foreach (var mod in own)
                yield return mod;
        }

        if (scope == RollScope.All && RunewordOn(item) is { } word)
        {
            foreach (var mod in word)
                yield return mod;
        }

        if (scope == RollScope.All && item.Quality is ItemQuality.Set
            && _setBonuses.TryGetValue((int)item.FileIndex, out var green))
        {
            foreach (var bonus in green)
                yield return bonus.Mod;
        }

        // The recipe that made it, where it can be told - and every recipe it could have
        // been where it cannot, since a property has to be accounted for either way.
        //
        // Yielded with no range, which is how this says "granted, leave the number alone".
        // A craft's own numbers are not rerolled by anything that rerolls the item: they
        // belong to a recipe, and rolling them again against the wrong recipe's range is
        // how a vampiric belt would come back wearing brilliant numbers. Reforging goes
        // through Crafts, which takes the old recipe's properties off first - after which
        // nothing here names them at all.
        var craft = RecipeBehind(item) is { } made
            ? made.Mods
            : CraftsFor(item).SelectMany(recipe => recipe.Mods);

        foreach (var mod in craft)
            yield return mod with { HasRange = false };

        if (withCorruption)
        {
            foreach (var grant in Corruptions.On(item))
                yield return new Mod(grant.Code, grant.Min, grant.Max, Param: grant.Param);
        }

        // The desecration stays a source whatever the corruption does: a corruption's clean takes
        // the corruption off and leaves the desecration on. Gone once its own clean has run.
        foreach (var grant in Desecrations.On(item))
            yield return new Mod(grant.Code, grant.Min, grant.Max, Param: grant.Param);

        // An upgrade's mod is fixed, like a craft's: granted, never rolled again.
        foreach (var grant in BaseUpgrades.Grants(item))
            yield return new Mod(grant.Code, grant.Min, grant.Max, HasRange: false, Param: grant.Param);

        // A map's recipes are fixed numbers; a dungeon's five are ranges, rolled when the scarab made it -
        // and its splash is a packed pair, "100-1", a chance and a level and no order between them.
        foreach (var grant in MapRecipes.Grants(item))
            yield return new Mod(grant.Code, grant.Min, grant.Max, HasRange: grant.Max != grant.Min, Param: grant.Param);

        foreach (var (code, min, max) in QualityItems.For(item))
            yield return new Mod(code, min, max);
    }

    /// <summary>
    /// How far the item's sources reach for each stat, keyed as <see cref="Reroll"/> keys them: what its fixed sources
    /// could hold - a craft's, a base upgrade's, whatever <see cref="ModsOn"/> yields as granted and never rolled - and
    /// the least its rolled ones could give. A writer that rebuilds a stat both write keeps the fixed part by these
    /// (<see cref="MagicAffixes.Apply"/>): rolled again it is not, and left out it was lost - a Caster amulet's 7%
    /// Faster Cast Rate went when "of the Apprentice" came, and the line read 10 where its sources give 15-20.
    /// Where the recipe cannot be told, every recipe it could have been counts, which only widens
    /// what the fixed part may be.
    /// </summary>
    public static (IReadOnlyDictionary<string, (int Min, int Max)> Fixed, IReadOnlyDictionary<string, int> RolledLeast) Shares(
        Item item, bool withCorruption = true)
    {
        var fixedParts = new Dictionary<string, (int Min, int Max)>(StringComparer.OrdinalIgnoreCase);
        var least = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in ModsOn(item, withCorruption))
        {
            // A packed row's two numbers are a chance and a level, not a range.
            if (IsPackedCode(mod.Code))
                continue;

            var low = Written(mod.Code, mod.Min, mod.Max, mod.Min, mod.Param);
            var high = Written(mod.Code, mod.Min, mod.Max, mod.Max, mod.Param);
            for (int at = 0; at < low.Count; at++)
            {
                string key = KeyOf(low[at]);
                int a = low[at].Value, b = at < high.Count ? high[at].Value : a;
                (a, b) = (Math.Min(a, b), Math.Max(a, b));

                if (mod.HasRange)
                    least[key] = (least.TryGetValue(key, out int already) ? already : 0) + a;
                else
                    fixedParts[key] = fixedParts.TryGetValue(key, out var part) ? (part.Min + a, part.Max + b) : (a, b);
            }
        }

        return (fixedParts, least);
    }

    /// <summary>
    /// Which of an item's sources a roll is asked of, for the property list it writes to.
    /// <para>
    /// <b>All</b>: every source, summed where they share a stat - the bound of what the item may hold.
    /// <b>Own</b>: the item's own list's - affixes, its own row, the automagic, a corruption, a
    /// desecration, a craft - without a runeword's properties, which are in a list of their own, or a set's
    /// bonuses, in theirs. <b>Runeword</b>: the runeword's alone. Rolled over every source, a Faith on a
    /// superior base wrote the superior's Enhanced Damage and the word's, summed, into both lines: 600 and
    /// more.
    /// </para>
    /// </summary>
    public enum RollScope
    {
        All,
        Own,
        Runeword,
    }

    /// <summary>
    /// What this item would hold if every one of its properties were rolled again - the
    /// Demonic Cube, which keeps an item's identity and its list of properties and puts
    /// fresh numbers on them.
    /// <para>
    /// <b><c>Values</c> and <c>Packed</c> are keyed by <see cref="KeyOf(string,int)"/></b> - the
    /// stat, plus the parameter where the parameter names what was granted - so a caller must
    /// look up with <see cref="KeyOf(ItemStat)"/> and not with the bare stat name.
    /// <c>Fixed</c> is by bare name, since it is about a property having nothing to roll.
    /// </para>
    /// </summary>
    /// <param name="withCorruption">
    /// Whether the corruption counts as one of the item's sources. False while a corruption
    /// is being taken off - the item is being asked what it is without one. True when the
    /// whole item is rerolled and keeps it, so the corruption's own numbers roll again too
    /// and a property the two share stays the sum of both.
    /// </param>
    /// <returns>
    /// The value each stat takes, and the stats that were deliberately not rolled because
    /// their property has no range to roll inside - a func-17 property takes its value from
    /// the affix's parameter, and rolling it would invent a number the table never offered.
    /// Also the skill and the level belonging to any stat that is a whole property packed
    /// into one: a number on its own is not a chance to cast anything.
    /// </returns>
    /// <param name="scope">
    /// Which sources (<see cref="RollScope"/>): all of them by default, for a writer that answers a stat's lines across
    /// the item's lists as one sum - the corruption's clean; a list's own for one that writes each list from its own -
    /// the Demonic Cube.
    /// </param>
    public static (IReadOnlyDictionary<string, int> Values,
                   IReadOnlySet<string> Fixed,
                   IReadOnlyDictionary<string, Wrote> Packed) Reroll(
        Item item, Random random, bool withCorruption = false, RollScope scope = RollScope.All)
    {
        var values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var held = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packed = new Dictionary<string, Wrote>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in ModsOn(item, withCorruption, scope))
        {
            // Everything this property is responsible for, whether or not a roll can be put
            // to it: the stats its row names, the ones it takes from its parameter, and the
            // ones a damage function fans out to without naming any.
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            _properties.TryGetValue(mod.Code, out var slots);

            foreach (var slot in slots ?? [])
                names.Add(slot.Stat);

            foreach (string stat in AllStatsOf(mod.Code))
                names.Add(stat);

            foreach (string stat in FannedOut(mod.Code))
                names.Add(stat);

            if (mod.HasRange && slots is not null)
            {
                // One roll for the property, not one per stat. A damage property fans out
                // over stats that must all carry the same number, and rolling them apart
                // would write an item the game could never have made.
                int roll = mod.Max > mod.Min ? random.Next(mod.Min, mod.Max + 1) : mod.Min;

                foreach (var wrote in Written(mod.Code, mod.Min, mod.Max, roll, mod.Param))
                {
                    // **Keyed the way the item's own stats are keyed** - by the stat and, where
                    // the parameter names *what* was granted, the parameter too. Keyed by the
                    // bare name, two sources writing one stat for two different subjects were
                    // added together and written as a single line: a javelin's automagic
                    // "+2 Javelin and Spear" and a Fanatic prefix's "+1 Barbarian Masteries"
                    // came out as one "+3", and Halaberd's Reign - which really carries
                    // "+1 Battle Cry" and "+1 Battle Orders" - would merge into "+2". Measured
                    // at 7 of 215 live listings, all of them item_singleskill.
                    string key = KeyOf(wrote);

                    // Two sources granting the same stat add up in game, so the rolls do too.
                    values[key] = values.TryGetValue(key, out int already)
                        ? already + wrote.Value
                        : wrote.Value;

                    if (wrote.SkillId is not null || wrote.Param is not null)
                        packed[key] = wrote;

                    names.Remove(wrote.Stat);
                }
            }

            // What is left over this property still writes, so it keeps whatever it holds
            // rather than being taken for a property with nothing behind it.
            foreach (string stat in names)
                held.Add(stat);
        }

        // `held` is collected by bare stat name, so a key carrying a parameter has to be cut
        // back before it can cancel one.
        foreach (string key in values.Keys)
            held.Remove(key.Split('#')[0]);

        return (values, held, packed);
    }

    /// <summary>
    /// The stats that one of the item's properties writes with a single roll, wherever a property writes
    /// two or more: Enhanced Damage's maximum and minimum (<c>dmg%</c>), all four resistances
    /// (<c>res-all</c>), the four attributes (<c>all-stats</c>), a damage property's one-hand, two-hand
    /// and thrown numbers. One roll lands on every one of them (<see cref="Written"/>), so on the item
    /// they move together - the item panel moves the rest of a group when one of its rows is changed.
    /// <para>
    /// Told by what the property does rather than listed: each of the item's own sources is asked what it
    /// writes for two different rolls, and the stats whose number follows the roll are the group. A stat
    /// that copies the row's minimum, maximum or parameter (funcs 15, 16, 17 - "Adds 3-6 Fire Damage")
    /// follows no roll and is in no group. Keys as the item's stats are keyed (<see cref="KeyOf(ItemStat)"/>).
    /// </para>
    /// </summary>
    public static IReadOnlyList<IReadOnlySet<string>> RolledTogether(Item item)
    {
        var groups = new List<IReadOnlySet<string>>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in ModsOn(item, withCorruption: true))
        {
            if (!_properties.ContainsKey(mod.Code) || !seen.Add($"{mod.Code}#{mod.Param}"))
                continue;

            // Two probes, a roll of 2 and a roll of 3 in a range of 1-3: a copied minimum reads 1 both
            // times, a copied maximum 3, a parameter itself; only a rolled stat reads 2, then 3.
            var low = Written(mod.Code, 1, 3, 2, mod.Param);
            var high = Written(mod.Code, 1, 3, 3, mod.Param);
            if (low.Count != high.Count)
                continue;

            var together = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int n = 0; n < low.Count; n++)
            {
                if (low[n].Value == 2 && high[n].Value == 3 && low[n].SkillId is null)
                    together.Add(KeyOf(low[n]));
            }

            if (together.Count > 1)
                groups.Add(together);
        }

        return groups;
    }

    private static Dictionary<string, string[]> _everyStat = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Every stat a property can write: the ones its row names, the ones it takes from its
    /// parameter, and the ones a function reaches with none named - <c>indestruct</c> names no
    /// stat and writes <c>item_indesctructible</c>. The set <see cref="Reroll"/> answers for.
    /// <see cref="StatsOf"/> alone missed it, and cleaning a corruption of Indestructible left
    /// the line on the item (130 of 11,970 corrupted uniques).
    /// </summary>
    public static IReadOnlyList<string> StatsWritten(string code) =>
        [.. StatsOf(code).Concat(AllStatsOf(code)).Concat(FannedOut(code)).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// The stats a property code writes, as <c>properties.txt</c> names them. Empty for a
    /// code whose row names none - the damage ones, which are written by function.
    /// </summary>
    public static IReadOnlyList<string> StatsOf(string code) =>
        _properties.TryGetValue(code, out var slots)
            ? [.. slots.Select(slot => slot.Stat)]
            : [];

    private static Dictionary<string, Slot[]> LoadProperties(GameData game)
    {
        var map = new Dictionary<string, Slot[]>(StringComparer.OrdinalIgnoreCase);
        var functions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var every = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var lines = Rows(game, "properties");
            if (lines.Count == 0)
                return map;

            string[] header = lines[0];
            int code = Array.IndexOf(header, "code");
            int[] stats = [.. Enumerable.Range(1, 7)
                .Select(n => Array.IndexOf(header, $"stat{n}"))];
            int[] funcs = [.. Enumerable.Range(1, 7)
                .Select(n => Array.IndexOf(header, $"func{n}"))];
            int[] vals = [.. Enumerable.Range(1, 7)
                .Select(n => Array.IndexOf(header, $"val{n}"))];

            if (code < 0)
                return map;

            foreach (string[] cells in lines.Skip(1))
            {
                if (cells.Length <= code || cells[code].Trim().Length == 0)
                    continue;

                string Cell(int column) =>
                    column >= 0 && cells.Length > column ? cells[column].Trim() : "";

                var named = stats
                    .Select((column, n) => (Stat: Cell(column), Func: Cell(funcs[n]), At: n))
                    .Where(pair => pair.Stat.Length > 0)
                    .Select(pair => new Slot(
                        pair.Stat,
                        TakesMin: pair.Func == FromMinimum,
                        TakesMax: pair.Func == FromMaximum,

                        // Only a later part reads the parameter; the first is the property
                        // itself and rolls. See the note on FromParameter.
                        TakesParam: pair.Func == FromParameter && pair.At > 0,
                        Func: pair.Func,
                        Val: int.TryParse(Cell(vals[pair.At]), out int val) ? val : 0))
                    .ToArray();

                // Every stat the row names, including the ones taken from the parameter.
                // Those have no range to offer but they are still written by the affix,
                // which is a different question and the one Accounted asks.
                every[cells[code].Trim()] =
                [
                    .. stats.Select(Cell).Where(stat => stat.Length > 0),
                ];

                if (named.Length > 0)
                {
                    map[cells[code].Trim()] = named;
                }
                else if (Cell(funcs[0]) is { Length: > 0 } only)
                {
                    functions[cells[code].Trim()] = only;

                    // A row that names no stat still writes one, and until this it got the
                    // accounting without the range: the panel showed Enhanced Damage with
                    // its source and no span beside it, and a forged value had nothing to
                    // be measured against. Only the functions that carry a real roll.
                    if (RangeableFunctions.Contains(only))
                    {
                        map[cells[code].Trim()] =
                        [
                            .. StatsOfFunction(only).Select(stat => new Slot(
                                stat, TakesMin: false, TakesMax: false, Func: only)),
                        ];
                    }
                }
            }
        }
        catch (Exception)
        {
            // Without the map no ranges are offered, which is what happened before.
        }

        _functionOf = functions;
        _everyStat = every;
        return map;
    }

    /// <summary>
    /// Reads an affix table into id to mods.
    /// <para>
    /// <b>The tables do not agree on where ids start.</b> A magic prefix or suffix id is a
    /// zero-based index into the data rows; an <c>automagic</c> id is one-based. Both were
    /// settled by checking stored ids against the values on the items carrying them, since
    /// an off-by-one here does not fail - it reads every range as a neighbour's, which
    /// looks like a plausible range for a different affix:
    /// </para>
    /// <list type="bullet">
    /// <item>prefix 238 on an item with <c>tohit 20</c> is Bronze (att 10-20) at zero-based,
    /// and Steel (att 25-36) at one-based, which 20 does not reach.</item>
    /// <item>suffix 347 with <c>maxhp 7</c> is of Life (hp 5-10), not of Vita (31-35).</item>
    /// <item>auto affix 58 on a Throwing Knife with <c>item_crit_multiplier 15</c> is the
    /// Throwing Knife row (crit-strike-multiplier 15-15) at <b>one-based</b>; zero-based
    /// lands on a blank line and the property shows no range at all.</item>
    /// </list>
    /// </summary>
    private static Dictionary<int, List<Mod>> LoadAffixes(
        GameData game, string table, bool oneBased = false)
    {
        var map = new Dictionary<int, List<Mod>>();

        try
        {
            var lines = Rows(game, table);
            if (lines.Count == 0)
                return map;

            string[] header = lines[0];

            // lines[0] is the header. A zero-based id n therefore sits at lines[n + 1],
            // and a one-based id at lines[n]. Blank rows are counted either way.
            int offset = oneBased ? 0 : 1;

            for (int id = oneBased ? 1 : 0; id + offset < lines.Count; id++)
            {
                string[] cells = lines[id + offset];
                var mods = new List<Mod>();

                for (int n = 1; n <= 3; n++)
                {
                    int code = Array.IndexOf(header, $"mod{n}code");
                    int low = Array.IndexOf(header, $"mod{n}min");
                    int high = Array.IndexOf(header, $"mod{n}max");

                    if (code < 0 || low < 0 || high < 0 || cells.Length <= high)
                        continue;
                    if (cells[code].Trim().Length == 0)
                        continue;

                    // A blank min and max is not a reason to forget the mod. An affix
                    // whose property takes its value from the parameter leaves them empty -
                    // of Self-Repair is written that way - and dropping it made an ordinary
                    // property look like one nothing on the item accounts for.
                    bool ranged = int.TryParse(cells[low].Trim(), out int min)
                                  & int.TryParse(cells[high].Trim(), out int max);

                    int spot = Array.IndexOf(header, $"mod{n}param");
                    int param = spot >= 0 && cells.Length > spot
                        ? ParameterOf(cells[code].Trim(), cells[spot].Trim())
                        : 0;

                    var (first, second) = Ordered(cells[code].Trim(), min, max);

                    mods.Add(new Mod(cells[code].Trim(), first, second, ranged, param));
                }

                if (mods.Count > 0)
                    map[id] = mods;
            }
        }
        catch (Exception)
        {
            // As above: no ranges rather than wrong ones.
        }

        return map;
    }

    /// <summary>
    /// What each craft recipe grants, by the item type it takes.
    /// <para>
    /// The recipes are in <c>cubemain.txt</c> like the corruptions, told apart by an output
    /// of <c>usetype,crf</c>. Each is written twice - once for a magic input and once for a
    /// rare one - and both are kept, since they grant the same thing and nothing here has to
    /// choose between them.
    /// </para>
    /// </summary>
    private static Dictionary<string, List<Recipe>> LoadCrafts(GameData game)
    {
        var map = new Dictionary<string, List<Recipe>>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<CraftRow>();

        try
        {
            var lines = Rows(game, "cubemain");
            if (lines.Count == 0)
                return map;

            string[] header = lines[0];

            int input = Array.IndexOf(header, "input 1");
            int output = Array.IndexOf(header, "output");
            int live = Array.IndexOf(header, "enabled");
            int said = Array.IndexOf(header, "description");

            if (input < 0 || output < 0)
                return map;

            foreach (string[] cells in lines.Skip(1))
            {
                if (cells.Length <= Math.Max(input, output))
                    continue;

                if (!cells[output].Trim().Trim('"')
                        .Contains("crf", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (live >= 0 && cells.Length > live && cells[live].Trim() != "1")
                    continue;

                // "helm,mag,noe": the type, then the conditions the cube checks. Only the
                // type says what the recipe applies to.
                string type = cells[input].Trim().Trim('"').Split(',')[0].Trim();
                if (type.Length == 0)
                    continue;

                var mods = new List<Mod>();

                for (int n = 1; n <= 7; n++)
                {
                    int code = Array.IndexOf(header, $"mod {n}");
                    int low = Array.IndexOf(header, $"mod {n} min");
                    int high = Array.IndexOf(header, $"mod {n} max");
                    int spot = Array.IndexOf(header, $"mod {n} param");

                    if (code < 0 || low < 0 || high < 0 || cells.Length <= high)
                        continue;
                    if (cells[code].Trim() is not { Length: > 0 } what)
                        continue;

                    bool ranged = int.TryParse(cells[low].Trim(), out int min)
                                  & int.TryParse(cells[high].Trim(), out int max);

                    int param = spot >= 0 && cells.Length > spot
                        ? ParameterOf(what, cells[spot].Trim())
                        : 0;

                    var (first, second) = Ordered(what, min, max);

                    mods.Add(new Mod(what, first, second, ranged, param));
                }

                if (mods.Count == 0)
                    continue;

                if (!map.TryGetValue(type, out var recipes))
                    map[type] = recipes = [];

                string description = said >= 0 && cells.Length > said ? cells[said].Trim().Trim('"') : "";
                var recipe = new Recipe(FamilyIn(description), mods);
                recipes.Add(recipe);

                // What goes into the cube with it, by code - "jewg", "r06g", "gg4r", or a craft
                // infusion's own "crfb". Kept for the grail's Crafting tab.
                string[] inputs =
                [
                    .. Enumerable.Range(2, 6)
                        .Select(n => Array.IndexOf(header, $"input {n}"))
                        .Where(column => column >= 0 && cells.Length > column)
                        .Select(column => cells[column].Trim().Trim('"').Split(',')[0].Trim())
                        .Where(code => code.Length > 0),
                ];

                rows.Add(new CraftRow(recipe, type, inputs, description));
            }
        }
        catch (Exception)
        {
            // As elsewhere.
        }

        _craftRows = rows;
        return map;
    }

    private sealed record CraftRow(Recipe Recipe, string Type, string[] Inputs, string Description);

    private static List<CraftRow> _craftRows = [];

    /// <summary>
    /// One craft recipe as the PD2 wiki's chart shows it: a family made on a type of item, what
    /// goes in the cube with it, the craft infusion that can stand in for all of that, a base to
    /// picture it by, and what it grants.
    /// </summary>
    /// <param name="Ingredients">Item codes: the rune, the perfect gem and the jewel, in the
    /// table's order.</param>
    /// <param name="Infusion">The family's craft infusion, which alone takes their place.</param>
    /// <param name="Showcase">The base the recipe's own description names - "magic full helm"
    /// - to draw the card with.</param>
    public sealed record CraftCard(
        string Family, string Type, IReadOnlyList<string> Ingredients, string? Infusion,
        string? Showcase, CraftRecipe Recipe);

    /// <summary>
    /// Every craft recipe once: one per family and type. The table writes each four times over -
    /// magic or rare input, ethereal or not - and twice more with a craft infusion in place of the
    /// rune, gem and jewel; S13 has 7 families on 10 types, 70 recipes.
    /// </summary>
    public static IReadOnlyList<CraftCard> CraftCards()
    {
        var cards = new List<CraftCard>();

        foreach (var recipe in _craftRows
                     .Where(row => row.Recipe.Family.Length > 0)
                     .GroupBy(row => (Family: row.Recipe.Family.ToLowerInvariant(), Type: row.Type.ToLowerInvariant())))
        {
            var withRunes = recipe.FirstOrDefault(row => !row.Inputs.Any(IsInfusion)) ?? recipe.First();
            var infused = recipe.FirstOrDefault(row => row.Inputs.Any(IsInfusion));

            cards.Add(new CraftCard(
                withRunes.Recipe.Family,
                withRunes.Type,
                [.. withRunes.Inputs.Where(code => !IsInfusion(code)).Select(ItemFor).Where(code => code is not null).Select(code => code!)],
                infused?.Inputs.FirstOrDefault(IsInfusion),
                ShowcaseIn(withRunes.Description) ?? ItemForge.BaseFor([withRunes.Type]),
                Shown(withRunes.Recipe)));
        }

        return cards;
    }

    /// <summary>A craft infusion - "Craft Ingredient Blood", <c>crfb</c> - by its item type.</summary>
    private static bool IsInfusion(string code) =>
        ItemTypes.TypeOf(code) is { } type && type.Equals("crft", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The item an input names. An input may name an item outright, or a group of them - an item
    /// type such as "r06g" (Ith Rune Generic) or "gg4r" (Generic P Ruby) - which is the item whose
    /// second type is the group: the shortest such code, so the Ith Rune and not its stack.
    /// </summary>
    private static string? ItemFor(string input)
    {
        if (D2SLib.Core.TXT.ItemsTXT.GetByCode(input) is not null)
            return input;

        return D2SLib.Core.TXT.ItemsTXT.MiscTXT?.Rows
            .Where(row => string.Equals(row["type2"]?.Value?.Trim(), input, StringComparison.OrdinalIgnoreCase))
            .Select(row => row["code"]?.Value?.Trim() ?? "")
            .Where(code => code.Length > 0)
            .OrderBy(code => code.Length)
            .FirstOrDefault();
    }

    /// <summary>
    /// The base a recipe's description makes it on: "magic full helm + jewel + ..." is a Full
    /// Helm. Null where the description names none the tables know.
    /// </summary>
    private static string? ShowcaseIn(string description)
    {
        int plus = description.IndexOf('+');
        if (plus <= 0)
            return null;

        string[] words = description[..plus].Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2)
            return null;

        string named = string.Join(' ', words.Skip(1));

        return ItemBases.Codes.FirstOrDefault(code =>
            string.Equals(D2SLib.Core.TXT.ItemsTXT.GetByCode(code)?.Get("name")?.Value?.Trim(), named,
                          StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The family a recipe's description names: everything after the arrow, first word
    /// only, with the "eth" two of them lead with taken off. "magic belt + jewel + rune 02
    /// + perfect skull -&gt; vampiric belt" is Vampiric.
    /// </summary>
    private static string FamilyIn(string description)
    {
        string said = description.Trim().Trim('"');

        int arrow = said.IndexOf("->", StringComparison.Ordinal);
        if (arrow < 0)
            return "";

        string[] words = said[(arrow + 2)..]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // "eth hitpower boots" is the hitpower family made on an ethereal base, not a
        // family of its own.
        int first = words.Length > 1
                    && string.Equals(words[0], "eth", StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;

        if (first >= words.Length)
            return "";

        string family = words[first];

        return family.Length > 0
            ? char.ToUpperInvariant(family[0]) + family[1..].ToLowerInvariant()
            : "";
    }

    /// <summary>
    /// The set bonuses each set item carries, by row number. Its columns are named for the
    /// tier and the half - <c>aprop1a</c>, <c>apar1a</c>, <c>amin1a</c>, <c>amax1a</c> - so
    /// this cannot share the loader below.
    /// </summary>
    private static Dictionary<int, List<SetBonus>> LoadSetBonuses(GameData game)
    {
        var map = new Dictionary<int, List<SetBonus>>();

        try
        {
            var lines = Rows(game, "setitems");
            if (lines.Count == 0)
                return map;

            string[] header = lines[0];
            int marker = MarkerRow(header, lines);

            for (int row = 0; row + 1 < lines.Count; row++)
            {
                string[] cells = lines[row + 1];
                var mods = new List<SetBonus>();

                for (int tier = 1; tier <= 5; tier++)
                {
                    foreach (string half in new[] { "a", "b" })
                    {
                        int code = Array.IndexOf(header, $"aprop{tier}{half}");
                        int low = Array.IndexOf(header, $"amin{tier}{half}");
                        int high = Array.IndexOf(header, $"amax{tier}{half}");
                        int spot = Array.IndexOf(header, $"apar{tier}{half}");

                        if (code < 0 || low < 0 || high < 0 || cells.Length <= high)
                            continue;
                        if (cells[code].Trim() is not { Length: > 0 } what)
                            continue;

                        bool ranged = int.TryParse(cells[low].Trim(), out int min)
                                      & int.TryParse(cells[high].Trim(), out int max);

                        int param = spot >= 0 && cells.Length > spot
                            ? ParameterOf(what, cells[spot].Trim())
                            : 0;

                        var (first, second) = Ordered(what, min, max);

                        // aprop1 is the bonus for two pieces, aprop2 for three.
                        mods.Add(new SetBonus(
                            tier + 1, new Mod(what, first, second, ranged, param)));
                    }
                }

                // Keyed by what an item would call this row, so no lookup has to
                // convert and none can be forgotten. The marker answers to nothing.
                int index = QualityRows.IndexOf(row, marker);

                if (mods.Count > 0 && index >= 0)
                    map[index] = mods;
            }
        }
        catch (Exception)
        {
            // As elsewhere: no ranges rather than wrong ones.
        }

        return map;
    }

    /// <summary>
    /// The parameter a unique or set row keeps, which is <b>not always a number</b>.
    /// <para>
    /// 146 of these columns hold a <b>name</b> instead: 141 skills - Spirit Ward casts
    /// <c>gethit-skill par='Lesser Fade'</c> - and 5 states, which is how the trophies paint
    /// their auras. Read as an id, every one of them is 0, so the property names no skill at
    /// all and the item's own stat cannot be matched against it.
    /// </para>
    /// <para>
    /// Which table names it is decided by the <b>stat's shape</b>, not by guessing: a
    /// <c>state</c> is looked up in <c>states.txt</c> and everything else in
    /// <c>skills.txt</c>, by that table's own internal <c>skill</c> column. All 146 resolve.
    /// </para>
    /// <para>
    /// A row's parameter cell as a number: the number itself, or the id behind the name the
    /// table wrote. <c>Zeal</c>, <c>Holy Freeze</c>, <c>Taunt</c> are skills; a few are states.
    /// </para>
    /// <para>
    /// <b>Internal because <see cref="Runewords"/> needs the same reading.</b> Safe to call
    /// from there: <c>SaveTables</c> loads this class before that one, so
    /// <see cref="_properties"/> - which says whether a code's stat wants a skill or a state -
    /// is already populated.
    /// </para>
    /// </summary>
    internal static int ParameterOf(string code, string cell)
    {
        if (cell.Length == 0)
            return 0;

        if (int.TryParse(cell, out int number))
            return number;

        string stat = _properties.TryGetValue(code, out var slots) && slots.Length > 0
            ? slots[0].Stat
            : "";

        return (StatShapes.Of(stat) is StatShape.State
            ? StateNames.IdOf(cell)
            : SkillNames.IdOf(cell)) ?? 0;
    }

    /// <summary>
    /// The property list a unique or set item is built from, keyed by row number. Indexed
    /// the way the name lookup indexes it - literal data rows, counted from zero, blank
    /// and marker rows included - so the two cannot disagree about which item a row is.
    /// </summary>
    private static Dictionary<int, List<Mod>> LoadFixedItems(GameData game, string table, int slots)
    {
        var map = new Dictionary<int, List<Mod>>();

        try
        {
            var lines = Rows(game, table);
            if (lines.Count == 0)
                return map;

            string[] header = lines[0];
            int marker = MarkerRow(header, lines);

            for (int row = 0; row + 1 < lines.Count; row++)
            {
                string[] cells = lines[row + 1];
                var mods = new List<Mod>();

                for (int n = 1; n <= slots; n++)
                {
                    int code = Array.IndexOf(header, $"prop{n}");
                    int low = Array.IndexOf(header, $"min{n}");
                    int high = Array.IndexOf(header, $"max{n}");

                    if (code < 0 || low < 0 || high < 0 || cells.Length <= high)
                        continue;
                    if (cells[code].Trim().Length == 0)
                        continue;

                    // A blank min and max is not a reason to forget the mod. An affix
                    // whose property takes its value from the parameter leaves them empty -
                    // of Self-Repair is written that way - and dropping it made an ordinary
                    // property look like one nothing on the item accounts for.
                    bool ranged = int.TryParse(cells[low].Trim(), out int min)
                                  & int.TryParse(cells[high].Trim(), out int max);

                    // **`par{n}`, not `mod{n}param`.** The affix tables name the column the
                    // second way and these two name it the first, and asking for a column
                    // that is not there is silent: `IndexOf` returns -1, the parameter stays
                    // 0, and every unique and set item in the game loses it. 769 of them did
                    // - 623 numeric and 146 written as a name - which is why a skill-shaped
                    // stat on a unique never matched its own source and read as "sources
                    // offer item_singleskill#0".
                    int spot = Array.IndexOf(header, $"par{n}");
                    int param = spot >= 0 && cells.Length > spot
                        ? ParameterOf(cells[code].Trim(), cells[spot].Trim())
                        : 0;

                    var (first, second) = Ordered(cells[code].Trim(), min, max);

                    mods.Add(new Mod(cells[code].Trim(), first, second, ranged, param));
                }

                // Keyed by what an item would call this row, so no lookup has to
                // convert and none can be forgotten. The marker answers to nothing.
                int index = QualityRows.IndexOf(row, marker);

                if (mods.Count > 0 && index >= 0)
                    map[index] = mods;
            }
        }
        catch (Exception)
        {
            // As above: no ranges rather than wrong ones.
        }

        return map;
    }

    /// <summary>
    /// Where the uncounted marker row sits, over the raw lines these loaders read. See
    /// <see cref="QualityRows"/> for what it is and why it matters.
    /// </summary>
    private static int MarkerRow(string[] header, IReadOnlyList<string[]> lines)
    {
        int index = Array.IndexOf(header, "index");
        if (index < 0)
            return -1;

        for (int row = 0; row + 1 < lines.Count; row++)
        {
            string[] cells = lines[row + 1];

            if (cells.Length > index
                && cells[index].Trim().Equals("Expansion", StringComparison.OrdinalIgnoreCase))
            {
                return row;
            }
        }

        return -1;
    }

    /// <summary>
    /// What the item's automagic row writes, by key (see <see cref="KeyOf(string, int)"/>), each
    /// as its lowest and highest roll. Nothing when it carries none.
    /// </summary>
    public static IEnumerable<(string Key, int Min, int Max, int? Level)> AutomagicGrants(Item item)
    {
        if (!item.IsAutoAffix || !_automagic.TryGetValue(item.AutoAffixId, out var mods))
            yield break;

        foreach (var mod in mods)
        {
            var low = Written(mod.Code, mod.Min, mod.Max, mod.Min, mod.Param);
            var high = Written(mod.Code, mod.Min, mod.Max, mod.Max, mod.Param);
            for (int i = 0; i < low.Count && i < high.Count; i++)
                yield return (KeyOf(low[i]),
                              Math.Min(low[i].Value, high[i].Value), Math.Max(low[i].Value, high[i].Value), low[i].SkillLevel);
        }
    }

    /// <summary>A one-based table's <c>level</c> and <c>maxlevel</c>, by id.</summary>
    private static Dictionary<int, (int Least, int Most)> Levels(GameData game, string table)
    {
        var levels = new Dictionary<int, (int, int)>();
        try
        {
            var lines = Rows(game, table);
            if (lines.Count == 0)
                return levels;

            int least = Array.IndexOf(lines[0], "level"), most = Array.IndexOf(lines[0], "maxlevel");
            for (int id = 1; id < lines.Count; id++)
            {
                string[] cells = lines[id];
                int low = least >= 0 && cells.Length > least && int.TryParse(cells[least].Trim(), out int a) ? a : 0;
                int high = most >= 0 && cells.Length > most && int.TryParse(cells[most].Trim(), out int b) ? b : 0;
                levels[id] = (low, high);
            }
        }
        catch (Exception)
        {
            // Without the levels every row is taken as possible, as before.
        }

        return levels;
    }

    /// <summary>A one-based table's number column, by id; a blank or unreadable cell is left out.</summary>
    private static Dictionary<int, int> Numbers(GameData game, string table, string name)
    {
        var numbers = new Dictionary<int, int>();
        try
        {
            var lines = Rows(game, table);
            int column = lines.Count == 0 ? -1 : Array.IndexOf(lines[0], name);
            for (int id = 1; column >= 0 && id < lines.Count; id++)
            {
                if (lines[id].Length > column && int.TryParse(lines[id][column].Trim(), out int number))
                    numbers[id] = number;
            }
        }
        catch (Exception)
        {
            // Without the column every row weighs the same.
        }

        return numbers;
    }

    /// <summary>A one-based table's <c>group</c> column, by id.</summary>
    private static Dictionary<int, string> Groups(GameData game, string table)
    {
        var groups = new Dictionary<int, string>();
        try
        {
            var lines = Rows(game, table);
            if (lines.Count == 0)
                return groups;

            int column = Array.IndexOf(lines[0], "group");
            if (column < 0)
                return groups;

            for (int id = 1; id < lines.Count; id++)
            {
                if (lines[id].Length > column && lines[id][column].Trim() is { Length: > 0 } group)
                    groups[id] = group;
            }
        }
        catch (Exception)
        {
            // Without the table no automagic is recognised, which is what it was before.
        }

        return groups;
    }

    /// <summary>One row of <c>automagic.txt</c> a base may carry: its id, what it writes, and the item levels it spawns at (0: any).</summary>
    public sealed record AutomagicRow(int Id, IReadOnlyList<CraftGrant> Grants, int Least, int Most, int Frequency = 1)
    {
        /// <summary>Whether an item of this level can carry the row: at or above its level, at or below its most (0: any).</summary>
        public bool Reaches(int level) => level >= Least && (Most <= 0 || level <= Most);
    }

    /// <summary>
    /// The automagic row the game would give this base at its item level: one of its group's rows the level
    /// reaches, drawn by <c>frequency</c> as the game draws them (it is
    /// random in game; the Forge drew evenly until then). Null where the base has no choice.
    /// </summary>
    public static AutomagicRow? RollAutomagic(Item item, Random random)
    {
        var reached = AutomagicRowsFor(item).Where(row => row.Reaches(item.ItemLevel)).ToList();
        if (reached.Count == 0)
            return null;

        // A row the table weighs at nothing is never drawn - unless every one is, then they weigh alike.
        int total = reached.Sum(row => Math.Max(0, row.Frequency));
        if (total == 0)
            return reached[random.Next(reached.Count)];

        int at = random.Next(total);
        foreach (var row in reached)
        {
            at -= Math.Max(0, row.Frequency);
            if (at < 0)
                return row;
        }

        return reached[^1];
    }

    /// <summary>
    /// Every automagic row this base can carry - its <c>auto prefix</c> group's, each with the item levels it
    /// spawns at - for the item panel's automagic line, just like superior. None on a
    /// unique or a set piece, which never carries one (<see cref="AutomagicFor"/>), or on a base naming no group.
    /// </summary>
    public static IReadOnlyList<AutomagicRow> AutomagicRowsFor(Item item)
    {
        if (item.Quality is ItemQuality.Unique or ItemQuality.Set || item.IsCompact)
            return [];

        string group = D2SLib.Core.TXT.ItemsTXT.GetByCode(item.Code.Trim())?.Get("auto prefix")?.Value?.Trim() ?? "";
        if (group.Length == 0)
            return [];

        var rows = new List<AutomagicRow>();
        foreach (var (id, rowGroup) in _automagicGroup.OrderBy(each => each.Key))
        {
            if (rowGroup != group || !_automagic.TryGetValue(id, out var mods) || mods.Count == 0)
                continue;

            var (least, most) = _automagicLevels.GetValueOrDefault(id);
            rows.Add(new AutomagicRow(id, [.. mods.Select(mod => new CraftGrant(mod.Code, mod.Min, mod.Max, mod.Param))], least, most,
                _automagicFrequency.GetValueOrDefault(id)));
        }
        return rows;
    }

    /// <summary>
    /// Puts automagic row <paramref name="id"/> on the item in place of the one it carries - 0 takes it off.
    /// The old row's share of each stat it wrote goes: the stat is taken off where nothing else on the item
    /// writes it, and held to what the other sources could have rolled where something does (a Paladin shield's
    /// all resistances, an affix's and the automagic's summed into one line). The new row's properties go on at
    /// the top of their ranges, as a superior bonus's do, summed into a line already there.
    /// </summary>
    public static void PutAutomagic(Item item, int id)
    {
        var list = ItemStats.Home(item);

        if (item.IsAutoAffix && _automagic.TryGetValue(item.AutoAffixId, out var old))
        {
            item.IsAutoAffix = false;
            var others = OwnOf(item);

            foreach (var mod in old)
            {
                foreach (var wrote in Written(mod.Code, mod.Min, mod.Max, mod.Max, mod.Param))
                {
                    // By the key; a packed one by its stat alone, its level being the item level's to set
                    // (DerivePacked) - a forged runeword's splash held another than the row writes.
                    string key = KeyOf(wrote);
                    if ((list.Stats.FirstOrDefault(stat => KeyOf(stat) == key)
                         ?? (IsPackedCode(mod.Code)
                             ? list.Stats.FirstOrDefault(stat => string.Equals(stat.Stat, wrote.Stat, StringComparison.OrdinalIgnoreCase))
                             : null)) is not { } held)
                        continue;

                    if (others.TryGetValue(key, out var range))
                        held.Value = Math.Clamp(held.Value, range.Min, range.Max);
                    else
                        list.Stats.Remove(held);
                }
            }
        }

        item.IsAutoAffix = id > 0;
        item.AutoAffixId = (ushort)Math.Max(id, 0);

        if (id > 0 && _automagic.TryGetValue(id, out var mods))
            ItemStats.WriteValues(item, [.. mods.SelectMany(mod => Written(mod.Code, mod.Min, mod.Max, mod.Max, mod.Param))]);

        DamageKinds.Settle(item);
    }

    /// <summary>
    /// Every property of the item's own list at the top of what its own sources could roll (<see cref="OwnOf"/>) -
    /// the affixes, the automod, the staffmods, a craft's recipe, summed where they share a stat - as the Forge makes
    /// a unique, every number its best (a random roll but maxed stats). A packed property -
    /// a chance to cast, charges - is the row's as written, not a range, and is left be; so is a switch (Cannot Be
    /// Frozen), on whatever its sources sum to.
    /// </summary>
    public static void TopOwn(Item item)
    {
        if (item.StatLists.FirstOrDefault() is not { } list)
            return;

        var ranges = OwnOf(item);
        foreach (var stat in list.Stats)
        {
            if (stat.Stat is not { Length: > 0 } name || StatShapes.Of(name) is StatShape.Chance or StatShape.Charges or StatShape.Toggle)
                continue;
            // Held to what the stat's field can store: a dungeon's monster life, summed to 56, wrote as -8.
            if (ranges.TryGetValue(KeyOf(stat), out var range) && range.Max > range.Min)
                stat.Value = StatFields.Held(name, range.Max);
        }
    }

    /// <summary>
    /// The automagic row an item's own properties show it carries, or null: for an item the editor
    /// builds, which a save would otherwise hold as bare properties nothing accounts for.
    /// <para>
    /// Measured on 22,776 items from the S12 item pack and S13 saves: an
    /// item carries one where its base names an "auto prefix" group, whatever its quality -
    /// normal, superior, magic, rare, crafted, runeword - <b>except a unique or a set piece</b>
    /// (0 of 949 and 0 of 96); and it is always one of that group's rows. Of the group's rows, the
    /// one whose properties the item holds within their ranges - most groups have one row
    /// (Splashing, the staves' casting tiers), class items' have several, told apart by their
    /// numbers and by the item levels each row may spawn at. <b>Nothing is added</b>: a row that
    /// the item's properties do not show is not taken, and then there is no answer.
    /// </para>
    /// <para>
    /// Some rows cannot be told apart at all: a Paladin shield's Shimmering (from level 1) and
    /// Rainbow (from 18) both write 5-10 to all resistances, and the game picks between them at
    /// random. The lowest is taken; either explains the item the same. And where a magic affix
    /// writes the same property as the automagic - all resistances again - the item keeps the sum,
    /// which may fall in a higher row's range than the one the game rolled.
    /// </para>
    /// </summary>
    public static int? AutomagicFor(Item item)
    {
        if (item.Quality is ItemQuality.Unique or ItemQuality.Set || item.IsCompact)
            return null;

        string group = D2SLib.Core.TXT.ItemsTXT.GetByCode(item.Code.Trim())?.Get("auto prefix")?.Value?.Trim() ?? "";
        if (group.Length == 0)
            return null;

        var own = item.StatLists.FirstOrDefault()?.Stats ?? [];
        int? loose = null;

        foreach (var (id, rowGroup) in _automagicGroup.OrderBy(each => each.Key))
        {
            if (rowGroup != group || !_automagic.TryGetValue(id, out var mods) || mods.Count == 0)
                continue;

            // The item levels the row may spawn at.
            if (_automagicLevels.TryGetValue(id, out var span)
                && (item.ItemLevel < span.Least || (span.Most > 0 && item.ItemLevel > span.Most)))
                continue;

            bool present = true, within = true;

            foreach (var mod in mods)
            {
                foreach (var wrote in Written(mod.Code, mod.Min, mod.Max, mod.Max, mod.Param))
                {
                    var held = own.FirstOrDefault(stat => ItemStats.ParamAgrees(stat, wrote));
                    if (held is null)
                    {
                        present = false;
                        break;
                    }

                    var (low, high) = (Math.Min(mod.Min, mod.Max), Math.Max(mod.Min, mod.Max));
                    if (mod.HasRange && (held.Value < low || held.Value > high))
                        within = false;
                }

                if (!present)
                    break;
            }

            if (!present)
                continue;
            if (within)
                return id;

            loose ??= id;
        }

        return loose;
    }

    private static IReadOnlyList<string[]> Rows(GameData game, string table) => game.Rows(table);
}
