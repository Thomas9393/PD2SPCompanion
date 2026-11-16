using System.Text;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// The magic affixes an item carries and the ones it could carry instead.
/// <para>
/// <b>The save stores them in pairs.</b> A magic item writes one prefix id and one suffix
/// id; a rare or crafted one writes three slots, each a presence bit for a prefix followed
/// by a presence bit for a suffix. So a slot can hold a prefix and no suffix, and the
/// asymmetry a rare item shows on screen is the asymmetry in the file - a glove here reads
/// <c>prefixes [238,667,143] suffixes [732,730,0]</c>, three prefixes against two suffixes.
/// </para>
/// <para>
/// How many an item gets is engine rule rather than table: the wiki gives a rare 3 to 6
/// affixes by item level within a cap of three each, a magic item one of each at most, and
/// a rare jewel exactly four whatever its level. <b>The cap of three each is the one part
/// the format itself confirms</b>, and it is the part enforced here.
/// </para>
/// </summary>
public static class MagicAffixes
{
    /// <summary>The most of either kind the save can hold.</summary>
    public const int Slots = 3;

    /// <summary>One affix row: what it is called, what it grants, and who may carry it.</summary>
    public sealed record AffixOption(
        int Id,
        bool IsPrefix,
        string Name,
        string Group,
        int Level,
        int MaxLevel,
        int Frequency,
        IReadOnlyList<(string Code, int Param, int Min, int Max)> Grants,
        string[] On,
        string[] NotOn,
        bool ForRare,
        int RequiredLevel = 0,
        string ForClass = "",
        int ClassRequiredLevel = 0,
        bool RareOnly = false,
        string ClassSpecific = "")
    {
        /// <summary>
        /// Whether an item made for that class (<see cref="ItemTypes.ClassOfBase"/>; null for one every class uses)
        /// may roll it. A class item takes only its own class's class-specific rows (<c>classspecific</c>) - a Paladin
        /// shield is a shield to <c>itype</c>, yet never rolls "+1 to Amazon Skills": none of 235 class skill lines on 300
        /// class bases in the ladder corpus was another class's.
        /// </summary>
        public bool FitsClassItem(string? itemClass) =>
            ClassSpecific.Length == 0 || itemClass is null
            || string.Equals(ClassSpecific, itemClass, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether a magic item may roll it: not a "dropped rare only" affix (<c>rare</c> 3, Season 14's: Visceral,
        /// Celestial, the +2 class skills on helms and armour - the patch notes' words), which only a rare rolls.
        /// </summary>
        public bool ForMagic => !RareOnly;

        /// <summary>
        /// Whether an item of this quality may roll it: a rare the rare pool, a craft the rare pool but a rare's alone
        /// (<see cref="RareOnly"/>), anything else the magic one. Crafts no "R"-only row: the wiki's Rarity column
        /// gives all 46 <c>rare</c> 3 rows "R" and every <c>rare</c> 1 row "M, R, C" (Item_Affixes).
        /// </summary>
        public bool RollsOn(ItemQuality quality) => quality switch
        {
            ItemQuality.Rare => ForRare,
            ItemQuality.Craft => ForRare && !RareOnly,
            _ => ForMagic,
        };

        /// <summary>
        /// The level this affix asks of whoever uses the item: its <c>levelreq</c>, or its
        /// <c>classlevelreq</c> when the affix is its class's (<c>class</c>) and that class is the
        /// character's.
        /// </summary>
        public int LevelFor(string? classCode) =>
            ClassRequiredLevel > 0 && ForClass.Length > 0
            && string.Equals(ForClass, classCode, StringComparison.OrdinalIgnoreCase)
                ? ClassRequiredLevel
                : RequiredLevel;

        /// <summary>
        /// What the affix's parameter names, where it names anything. On the properties
        /// that cast or grant a skill it is a skill id, and the game's wording has a slot
        /// waiting for it.
        /// </summary>
        private static string? Subject(int param) => SkillNames.NameOf(param);

        /// <summary>
        /// Every stat this affix writes, the fanned-out ones included - and those a property writes naming none
        /// (<see cref="AffixRanges.StatsWritten"/>): "of Ages" gives Indestructible by <c>indestruct</c>, which names no
        /// stat, and swapped for "of Teleportation" on the Forge it left the line behind - an
        /// affix change moves only what its affixes write.
        /// </summary>
        public IReadOnlyList<string> Stats =>
        [
            .. Grants.SelectMany(grant => AffixRanges.StatsWritten(grant.Code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];

        /// <summary>
        /// Whether putting this affix on an item needs a stat parameter whose shape is still
        /// unproven - in which case it is refused rather than written wrong.
        /// <para>
        /// A property that casts or grants a skill stores the skill id beside its value, in
        /// a field whose width <c>itemstatcost</c> gives as <c>Save Param Bits</c>. Writing a
        /// plain value-only stat was settled first - the corruption path does it and four
        /// gloves confirmed it in game - and every affix with a parameter was refused until
        /// its own shape was settled too.
        /// </para>
        /// <para>
        /// The packed ones now are: a chance to cast and a charged skill, where the parameter
        /// holds the skill and the level. Settled against a cap off the character
        /// that carries one, and written back the same way - so these are offered.
        /// </para>
        /// <para>
        /// A skill tree and a class are packed as well, written by hand and read back
        /// off the item in game: "+2 to Masteries (Barbarian Only)" from a parameter of 33,
        /// which is the Barbarian three bits above his second tree, and "+1 to Paladin Skill
        /// Levels" from a parameter of 3.
        /// </para>
        /// <para>
        /// The rest are still refused: a monster type, a state, an element. Nothing has
        /// confirmed what their parameters hold, and an affix that came out silently wrong
        /// would be worse than one that was not offered.
        /// </para>
        /// </summary>
        public bool NeedsParam =>
            Stats.Any(stat =>
                StatFields.ParamMaximumOf(stat) is not null
                && !StatText.IsPacked(stat)
                && stat is not ("item_addskill_tab" or "item_addclassskills")

                // A stat the engine no longer reads (none in S13: item_elemskill was thought
                // retired and is the half that grants - see StatCatalogue).
                && !StatCatalogue.IsObsolete(stat));

        /// <summary>
        /// What it gives, worded the way the game words a property and carrying the range
        /// it rolls in - the same shape the corruption list uses, for the same reason.
        /// </summary>
        public IReadOnlyList<string> Lines
        {
            get
            {
                var said = new List<string>();

                foreach (var (code, param, min, max) in Grants)
                {
                    // A damage property names no stat of its own and is worded by its
                    // function instead.
                    var stats = AffixRanges.StatsOf(code);
                    if (stats.Count == 0 && AffixRanges.WordingFor(code) is { } worded)
                        stats = [worded];

                    // What each part will actually hold, which is what says whether the
                    // row's two numbers are a range for it, one end of one, or neither.
                    var written = AffixRanges.Written(code, min, max, min, param)
                        .ToDictionary(each => each.Stat, each => each, StringComparer.OrdinalIgnoreCase);

                    foreach (string stat in stats.DefaultIfEmpty(code))
                    {
                        var span = AffixRanges.RangeFor(code, stat, min, max, param);

                        // A stat whose parameter picks the wording is worded from the
                        // parameter - the one the property worked out, not the one the row
                        // holds, or every skill tree in the list reads as the Amazon's.
                        written.TryGetValue(stat, out var holds);

                        if (StatText.ByParameter(stat, span.Min, holds?.Param ?? param) is { } picked)
                        {
                            said.Add(span.Min == span.Max
                                ? picked
                                : $"{picked} [{span.Min} - {span.Max}]");

                            continue;
                        }

                        // A packed property is not a range at any point: it is a chance, a
                        // skill and a level, and only the whole of it reads as anything.
                        string? said2 = holds is { SkillId: not null }
                            ? StatText.Describe(
                                stat, holds.Value, Subject(param), holds.SkillLevel, holds.MaxCharges)
                            : StatText.DescribeRange(stat, span.Min, span.Max, Subject(param));

                        string words = said2 ?? stat;

                        if (!words.Any(char.IsDigit))
                            words += span.Min == span.Max ? $" {span.Min}" : $" [{span.Min} - {span.Max}]";

                        said.Add(words);
                    }
                }

                return said;
            }
        }

        /// <summary>The same, on one line, for a row in a list.</summary>
        public string Readable =>
            Lines.Count > 0 ? string.Join(", ", Lines) : Name;
    }

    /// <summary>
    /// One slot of the item's affix pairs: what sits in it, either side.
    /// <para>
    /// Kept as a pair because the file keeps it as a pair. A row with a prefix and no
    /// suffix is ordinary, not a gap to be closed up.
    /// </para>
    /// </summary>
    public sealed record AffixPair(int Slot, AffixOption? Prefix, AffixOption? Suffix)
    {
        public bool IsEmpty => Prefix is null && Suffix is null;

        public string PrefixName => Prefix?.Name ?? "";
        public string SuffixName => Suffix?.Name ?? "";
    }

    private static AffixOption[] _prefixes = [];
    private static AffixOption[] _suffixes = [];

    /// <summary>One build's affix tables: its prefixes and its suffixes.</summary>
    private sealed record Tables(AffixOption[] Prefixes, AffixOption[] Suffixes);

    /// <summary>
    /// The earlier builds of the season whose affix tables are kept, newest first (<see cref="LoadEarlier"/>): a patch that
    /// changed where an affix may roll - S14's second took Celestial off staves, wands and orbs, and moved of Frostward to
    /// another group - leaves the items rolled before it judged by tables no longer installed.
    /// </summary>
    private static List<Tables> _earlier = [];

    /// <summary>An earlier build's tables in place of this one's, for the judging under way on this thread (<see cref="Judging"/>).</summary>
    private static readonly AsyncLocal<Tables?> _judging = new();

    /// <summary>The prefixes the rows are read from: this build's, or an earlier one's while it is judged by (<see cref="Judging"/>).</summary>
    private static AffixOption[] Prefixes => _judging.Value?.Prefixes ?? _prefixes;

    private static AffixOption[] Suffixes => _judging.Value?.Suffixes ?? _suffixes;

    /// <summary>How many earlier builds' tables are kept.</summary>
    public static int EarlierBuilds => _earlier.Count;

    /// <summary>
    /// The affix tables of the season's earlier builds, newest first: each one's magicprefix and magicsuffix, as the
    /// season's snapshot keeps them. Read to judge an item by, never to roll one: the Forge and the cube roll by this build.
    /// </summary>
    public static void LoadEarlier(IEnumerable<(string[] Prefixes, string[] Suffixes)> builds) =>
        _earlier = [.. builds.Select(build => new Tables(Read(build.Prefixes, prefix: true, noteDisabled: false),
                                                          Read(build.Suffixes, prefix: false, noteDisabled: false)))
                             .Where(tables => tables.Prefixes.Length > 0 && tables.Suffixes.Length > 0)];

    /// <summary>
    /// Earlier build <paramref name="build"/>'s affix tables in place of this one's on this thread until disposed: what an
    /// item rolled before a patch is judged and searched by (Legitimacy.Check, TradeItems.Build).
    /// </summary>
    public static IDisposable Judging(int build)
    {
        var before = _judging.Value;
        _judging.Value = _earlier[build];
        return new Restore(() => _judging.Value = before);
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    /// <summary>The automagic table's rows, in the affixes' shape, for the green bracket alone (<see cref="InBracketOf"/>).</summary>
    private static AffixOption[] _autoRows = [];

    /// <summary>
    /// Each auto affix's name and exclusion group, keyed the way an item names it.
    /// <para>
    /// Only these two columns, because this table is read in full elsewhere - see
    /// <see cref="AffixRanges"/> - and all that is wanted here is the group, which the
    /// engine shares across <c>magicprefix</c>, <c>magicsuffix</c> and <c>automagic</c>
    /// alike. Keyed by <c>AutoAffixId</c>, which is one-based, so row n answers to n + 1.
    /// </para>
    /// </summary>
    private static Dictionary<int, (string Name, string Group)> _autoGroups = [];

    /// <summary>
    /// The names of rows that exist but are <c>spawnable 0</c>, which the pools deliberately
    /// treat as empty. Kept so a finding can name the affix instead of calling the id unknown.
    /// </summary>
    private static readonly Dictionary<int, string> _disabledPrefixes = [];
    private static readonly Dictionary<int, string> _disabledSuffixes = [];

    /// <summary>
    /// The name of a row this id points at when the row exists but cannot spawn, or null when
    /// the id names nothing at all - a blank spacer row, or past the end of the table.
    /// </summary>
    public static string? DisabledName(int id, bool prefix) =>
        (prefix ? _disabledPrefixes : _disabledSuffixes).TryGetValue(id, out string? name)
            ? name
            : null;

    /// <summary>Whether this item's quality carries magic affixes at all.</summary>
    public static bool Carries(Item item) =>
        item.Quality is ItemQuality.Magic or ItemQuality.Rare or ItemQuality.Craft;

    /// <summary>
    /// How many affixes the generator gives an item of this quality - the legitimacy check's
    /// ceiling and <see cref="RollFresh"/>'s range. A rare jewel is the one exception in the
    /// game: always exactly four. A crafted item's recipe properties are not affixes and are
    /// not counted; how likely each count is depends on its level (<see cref="RollCount"/>).
    /// </summary>
    public static (int Least, int Most) CountFor(Item item) => item.Quality switch
    {
        ItemQuality.Magic => (1, 2),
        ItemQuality.Rare when IsJewel(item) => (4, 4),
        ItemQuality.Rare => (3, 6),
        ItemQuality.Craft => (1, 4),
        _ => (0, 0),
    };

    /// <summary>
    /// Whether the item is a jewel - by its type, <c>jewl</c>. Asked as the type equalling "jew" (the code),
    /// which no item's type is, a rare jewel is held to three to six, and rolled so.
    /// </summary>
    public static bool IsJewel(Item item) =>
        ItemTypes.LineageOfBase(item.Code).Contains("jewl", StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// How many affixes to roll onto this item: evenly within <see cref="CountFor"/>, except a
    /// crafted item's, which the cube draws by its level (<see cref="Crafts.RollAffixCount"/>) -
    /// and <b>a rare map's, which is always six</b>: fifty tier-3 maps made rare in the game's cube
    /// carry three prefixes and three suffixes, every one of the fifty. <b>A
    /// magic map's is always two</b>, a prefix and a suffix: fifty of fifty. Maps
    /// of older seasons are found with five and four, so the least a
    /// rare map may <i>hold</i> is left where it was; this is what a fresh roll gives.
    /// </summary>
    public static int RollCount(Item item, Random random)
    {
        if (item.Quality == ItemQuality.Craft)
            return Crafts.RollAffixCount(item.ItemLevel, random);

        // A map is rolled full: six on a rare one, a prefix and a suffix on a magic one.
        var (least, most) = CountFor(item);
        if (item.Quality is ItemQuality.Rare or ItemQuality.Magic
            && ItemTypes.LineageOfBase(item.Code).Contains("map", StringComparer.OrdinalIgnoreCase))
        {
            return most;
        }

        return item.Quality switch
        {
            ItemQuality.Rare when least == most => most,
            ItemQuality.Rare => RareCount(item.ItemLevel, random),
            ItemQuality.Magic => MagicAlwaysTwo(item) || random.Next(4) == 3 ? 2 : 1,
            _ => random.Next(least, most + 1),
        };
    }

    /// <summary>
    /// A rare's count by its item level, as PD2's wiki tabulates it (Item_Affixes): three to six with
    /// 1/3/3/1 eighths below 45, then a floor - four at 45, five at 65, six at 85 (Season 4 and 5 patch
    /// notes: "Rares now roll minimum 4 affixes at 45+, 5 at 65+ and 6 at 85+"). The floors are the
    /// patch notes'; the shares above them are the wiki's alone, and vanilla's own table is not them
    /// by default this is how many stats are rolled, which the reroll rare button goes by.
    /// </summary>
    public static int RareCount(int itemLevel, Random random)
    {
        int[] eighths = itemLevel switch
        {
            >= 85 => [6, 6, 6, 6, 6, 6, 6, 6],
            >= 65 => [5, 5, 5, 5, 6, 6, 6, 6],
            >= 45 => [4, 4, 5, 5, 5, 5, 6, 6],
            _ => [3, 4, 4, 4, 5, 5, 5, 6],
        };
        return eighths[random.Next(8)];
    }

    /// <summary>
    /// Whether a magic item always rolls a prefix and a suffix: weapons and armour from item level 65,
    /// jewels and jewellery from 85, charms from 90 (Season 11 patch notes say "above"; the Item_Affixes
    /// page writes "65+" - which side 65 itself falls on is unproven, and 65+ is taken). Below that the
    /// game's own split: a suffix alone half the time, a prefix alone a quarter, both a quarter.
    /// </summary>
    public static bool MagicAlwaysTwo(Item item)
    {
        var lineage = ItemTypes.LineageOfBase(item.Code);
        bool Is(string type) => lineage.Contains(type, StringComparer.OrdinalIgnoreCase);

        int from = Is("char") ? 90 : Is("ring") || Is("amul") || Is("jewl") ? 85 : 65;
        return item.ItemLevel >= from;
    }

    /// <summary>How many pairs this quality can hold: one for magic, three for the rest.</summary>
    public static int SlotsFor(Item item) =>
        item.Quality == ItemQuality.Magic ? 1 : Slots;

    /// <summary>The affix in one slot, or null where the slot is empty.</summary>
    public static AffixOption? At(Item item, int slot, bool prefix)
    {
        var ids = prefix ? item.MagicPrefixIds : item.MagicSuffixIds;

        return ids is null || slot < 0 || slot >= ids.Length
            ? null
            : Find(ids[slot], prefix);
    }

    /// <summary>
    /// Whether the tables say anything at all about what this base may carry - that is,
    /// whether any affix names its type or one of that type's ancestors.
    /// <para>
    /// An uber map is <c>ubr</c>, a type <b>no affix row mentions</b>, and its lineage has no
    /// parent to fall back on. Asked whether a given affix belongs on one, the pool answered
    /// no and the check called six affixes impossible. The game disagrees: hovering one in
    /// S13 shows every one of them - "Map contains Stygian Dolls" is the Stygian prefix,
    /// printed as a sentence rather than as a word in the name, which is why the item is
    /// called plain "Blood Casque".
    /// </para>
    /// <para>
    /// So the pool is silent about these bases rather than restrictive, and a silence must
    /// not be read as a refusal. Where it is silent the check says so and claims nothing.
    /// </para>
    /// </summary>
    public static bool Judgeable(Item item)
    {
        var family = Family(item);
        if (family.Count == 0)
            return false;

        _typesNamed ??= new HashSet<string>(
            _prefixes.Concat(_suffixes).SelectMany(row => row.On),
            StringComparer.OrdinalIgnoreCase);

        return family.Any(_typesNamed.Contains);
    }

    /// <summary>Every item type the affix tables name, built once.</summary>
    private static HashSet<string>? _typesNamed;

    /// <summary>
    /// Whether this is a real affix rather than one of the table's blank rows.
    /// <para>
    /// A blank row keeps its number so the ids after it stay where they are, so
    /// <see cref="Find"/> hands one back as an unusable placeholder instead of null. Asking
    /// "did I get an affix" is therefore not a null check, and code that treated it as one
    /// reported an item's bogus affix with an empty name - "&#160;is not an affix this item
    /// can carry" - when the honest statement is that no affix has that id.
    /// </para>
    /// </summary>
    public static bool Names(AffixOption? option) => option is { Name.Length: > 0 };

    /// <summary>An affix by its id, from whichever table it belongs to.</summary>
    public static AffixOption? Find(int id, bool prefix)
    {
        if (id <= 0)
            return null;

        var table = prefix ? Prefixes : Suffixes;
        return id < table.Length && table[id].Id == id ? table[id] : null;
    }

    /// <summary>
    /// The item's affixes as the file holds them: slot by slot, prefix beside suffix.
    /// Runs to the last slot holding anything, so an item with one pair shows one row.
    /// </summary>
    public static IReadOnlyList<AffixPair> PairsOn(Item item)
    {
        if (!Carries(item))
            return [];

        var pairs = new List<AffixPair>();

        for (int slot = 0; slot < SlotsFor(item); slot++)
            pairs.Add(new AffixPair(slot, At(item, slot, true), At(item, slot, false)));

        // Trailing empty slots are nothing to show, but the first row stays whatever
        // happens: an item with no affixes at all still needs somewhere to put one.
        while (pairs.Count > 1 && pairs[^1].IsEmpty)
            pairs.RemoveAt(pairs.Count - 1);

        return pairs;
    }

    /// <summary>
    /// The level the game rolls this item's affixes at, which is <b>not</b> its item level.
    /// <para>
    /// The base's own level counts against it: <c>alvl = ilvl - qlvl / 2</c>, and where the
    /// base outranks the item the item level is raised to the base's first. Near 99 it turns
    /// and climbs twice as fast, which is what keeps the top tiers reachable at all.
    /// </para>
    /// <para>
    /// A Brandistock is <c>qlvl 16</c>, so one found at item level 12 rolls its affixes at
    /// <b>8</b> - and the game prints exactly that on the tooltip. Gating the affix list by
    /// item level offered four tiers the item could never have rolled.
    /// </para>
    /// </summary>
    public static int AffixLevel(Item item) => AffixLevel(item.Code, item.ItemLevel);

    /// <summary>
    /// The affix levels this item's affixes may have rolled at: its own base's, and - for a rare
    /// or crafted item, which PD2's cube upgrades keeping its affixes - every lower tier of its
    /// base at the same item level, since the affixes stay put while the base moves up. A crafted
    /// Matriarchal Spear at item level 84 reaches 69, yet carried Visceral (75) and "of
    /// Hypothermia" (70): it was crafted as a Maiden Spear, which reaches 75 at 84, and upgraded
    /// twice (a Stone Barb). The current base's level comes first.
    /// </summary>
    public static IReadOnlyList<int> AffixLevelsAcrossUpgrades(Item item) =>
        [.. BasesAcrossUpgrades(item).Select(code => AffixLevel(code, item.ItemLevel))];

    /// <summary>
    /// The bases this item may have carried its affixes on: its own first, then - for a rare or
    /// crafted item the cube can upgrade - every lower tier of its family.
    /// </summary>
    private static IReadOnlyList<string?> BasesAcrossUpgrades(Item item)
    {
        if (!BaseUpgrades.GameUpgrades(item.Quality) || item.IsRuneword
            || ItemTier.Family(item.Code) is not { } family || ItemTier.Rank(item.Code) is not int rank)
            return [item.Code];

        var bases = new List<string?> { item.Code };
        for (int lower = 0; lower < rank && lower < family.Count; lower++)
            bases.Add(family[lower]);

        // And what upgrades into the family from outside its three: a Coronet, a Tiara or a Diadem may
        // have begun a Circlet.
        foreach (string into in ItemTier.Before(item.Code))
        {
            if (!bases.Contains(into, StringComparer.OrdinalIgnoreCase))
                bases.Add(into);
        }

        return bases;
    }

    /// <summary>The affix level an item of this base would have at this item level.</summary>
    public static int AffixLevel(string? code, int itemLevel)
    {
        var basis = ItemBases.Of(code);
        int ilvl = Math.Min(itemLevel, 99);
        int qlvl = Math.Min(basis?.QualityLevel ?? 0, 99);
        int magic = basis?.MagicLevel ?? 0;

        if (qlvl > ilvl)
            ilvl = qlvl;

        // A base with a magic level is a different rule, not an adjustment to this one: the
        // quality penalty does not apply to it at all, and the level is simply raised.
        //
        // Measured off the game's own tooltip, which prints both numbers. An Imp Casque on a
        // Circlet - qlvl 24, magic level 3 - reads "Item Level 49, Affix Level 52": 49 + 3,
        // where the penalty would have made it 37. Without this, every circlet carrying an
        // affix above its penalised level was called impossible, which is a real drop being
        // called a fake.
        if (magic > 0)
            return Math.Min(99, ilvl + magic);

        return ilvl < 99 - qlvl / 2 ? ilvl - qlvl / 2 : 2 * ilvl - 99;
    }

    /// <summary>
    /// The item level this base would have to reach for an affix of that level to roll on
    /// it - the inverse of <see cref="AffixLevel"/>.
    /// <para>
    /// Said this way round because item level is the field the editor offers. Told that an
    /// affix needs level 12, the obvious move is to set item level 12, and on a Brandistock
    /// that still leaves it four short.
    /// </para>
    /// </summary>
    public static int ItemLevelFor(Item item, int affixLevel) => ItemLevelFor(item.Code, affixLevel);

    /// <summary>
    /// The lowest item level at which any base this item may have carried its affixes on
    /// (<see cref="AffixLevelsAcrossUpgrades"/>) reaches that affix level - what an upgraded
    /// rare or craft would have needed when it rolled.
    /// </summary>
    public static int ItemLevelAcrossUpgradesFor(Item item, int affixLevel) =>
        BasesAcrossUpgrades(item).Min(code => ItemLevelFor(code, affixLevel));

    private static int ItemLevelFor(string? code, int affixLevel)
    {
        // The lowest item level whose affix level reaches it, asked of AffixLevel itself. Worked out as
        // affix level + qlvl / 2, it missed the item level raised to the base's and the turn near 99: a Vortex
        // Shield (qlvl 85) rolls at 71 or more at any item level, and "item level 72" was said for a level 30
        // affix, 99 for a 65 one (a Paladin shield). 99 where none does.
        for (int level = 1; level < 99; level++)
        {
            if (AffixLevel(code, level) >= affixLevel)
                return level;
        }

        return 99;
    }

    /// <summary>
    /// Every affix of one kind this item could carry, in table order.
    /// <para>
    /// The filter is the game's own: the row must be spawnable, reachable at this item's
    /// level, allowed on one of the item's types and refused on none of them, on a class
    /// item its own class's or no class's (<see cref="AffixOption.FitsClassItem"/>), and - on a
    /// rare or crafted item - marked as one a rare may roll. <b>Group exclusivity applies
    /// too</b>: an item holds no more than one affix from a group, so the groups its other
    /// slots already spend are struck out. The slot being filled is left out of that, since
    /// replacing an affix with another from its own group is legal.
    /// </para>
    /// </summary>
    /// <param name="narrowTo">
    /// The item type to filter by instead of the item's own: null asks about the item, an
    /// empty string asks for everything whatever it fits, and a type code asks about that
    /// type's family. A guide rather than a rule, the same as the rare name filter - the
    /// editor writes whatever is picked.
    /// </param>
    /// <param name="outgrown">
    /// Include affixes whose own ceiling the item has risen above. The picker leaves these
    /// out - a tier the item has outgrown is not a thing to aspire to - but asking whether
    /// the game could <b>ever</b> put an affix on a base is a different question from asking
    /// what it would roll now, and only the first of those is grounds for calling an item
    /// impossible. See <see cref="Legitimacy"/>.
    /// </param>
    /// <param name="taken">
    /// Include the affixes whose group another of the item's affixes holds: the picker shows them, unpickable, to
    /// say why they cannot be had (<see cref="GroupsTaken"/>).
    /// </param>
    public static IReadOnlyList<AffixOption> Available(
        Item item, bool prefix, int forSlot = -1, string? narrowTo = null, bool outgrown = false, bool taken = false)
    {
        if (!Carries(item))
            return [];

        var family = narrowTo is null ? Family(item) : Lineage(narrowTo);

        // Only the item's own family can be empty, and an item with no type matches nothing.
        if (narrowTo is null && family.Count == 0)
            return [];

        var asked = new PoolQuestion(family, family.Count == 0, item.Quality,
                                     GroupsSpent(item, prefix, forSlot), AffixLevel(item), outgrown, taken,
                                     narrowTo is null ? ItemTypes.ClassOfBase(item.Code) : ItemTypes.ClassOfType(narrowTo));

        return [.. (prefix ? Prefixes : Suffixes).Where(asked.Offers)];
    }

    /// <summary>
    /// Whether <see cref="Available"/> asked of the item's own family would offer this one affix - asked of its row
    /// alone. The legitimacy check asks it of each affix an item carries: it built the whole pool, some 700 rows, to
    /// look for one id in it, up to six times an item.
    /// </summary>
    public static bool Offers(Item item, bool prefix, int forSlot, int id, bool outgrown = false, bool taken = false)
    {
        if (!Carries(item))
            return false;

        var family = Family(item);
        if (family.Count == 0 || Find(id, prefix) is not { } row)
            return false;

        return new PoolQuestion(family, false, item.Quality,
                                GroupsSpent(item, prefix, forSlot), AffixLevel(item), outgrown, taken,
                                ItemTypes.ClassOfBase(item.Code)).Offers(row);
    }

    /// <summary>What a pool asks of each row - the one rule of <see cref="Available"/> and <see cref="Offers"/>.</summary>
    private sealed record PoolQuestion(
        HashSet<string> Family, bool Everything, ItemQuality Quality, HashSet<string> Spent, int AffixLevel, bool Outgrown, bool Taken,
        string? ItemClass)
    {
        public bool Offers(AffixOption row) =>
            row.Id > 0

            // Weight zero is never picked, and every such row is a classic-era
            // duplicate of one that is - which is why two affixes of the same name
            // kept appearing side by side in the pool.
            && row.Frequency > 0
            && row.RollsOn(Quality)

            // Note what is *not* here: the affix's own level. An affix the item is too
            // low for is offered and marked rather than hidden, because an editor that
            // hides it leaves no way to say what level the item would need.
            //
            // Its ceiling stays, though. A tier the item has outgrown - mana 1-2 stops
            // at item level 2 - is not a thing to aspire to, and leaving it in put a
            // second affix of the same name beside the one that replaced it. The item's
            // level is worked out once a pool, not per row.
            && (Outgrown || row.MaxLevel <= 0 || AffixLevel <= row.MaxLevel)
            && (Everything || (row.On.Any(Family.Contains) && !row.NotOn.Any(Family.Contains) && row.FitsClassItem(ItemClass)))
            && (Taken || row.Group.Length == 0 || !Spent.Contains(row.Group));
    }

    /// <summary>
    /// The groups the item's other affixes hold - and its automagic - which an affix for this slot may not repeat
    /// (a pick on one stat blocks another slot from selecting it, and the user is told).
    /// </summary>
    public static IReadOnlySet<string> GroupsTaken(Item item, bool prefix, int forSlot) => GroupsSpent(item, prefix, forSlot);

    /// <summary>
    /// Every affix row the game counts when it draws the green bracket on this item: the rows
    /// this base could have rolled <b>at its item level</b>.
    /// <para>
    /// This is deliberately not <see cref="Available"/>. That answers what the picker may
    /// offer and gates on <see cref="AffixLevel"/> with the tier ceiling applied; the bracket
    /// gates on the <b>item level</b> and ignores the ceiling entirely. Both readings fit a
    /// single tooltip, which is why two were needed to tell them apart.
    /// </para>
    /// <para>
    /// Measured on an ilvl 18 / affix level 15 boot, whose Fire Resist reads
    /// <c>[5 - 30]</c>: the 30 is <c>Garnet</c> at level <b>18</b>, above the affix level and
    /// exactly at the item level. The same item's Enhanced Defense stops at 40 because
    /// <c>Glorious</c> is level <b>19</b>, one past it. An ilvl 53 helm then showed
    /// <c>[10 - 200]</c> enhanced defence, whose 10 is a row with <c>maxlevel 41</c> - so the
    /// ceiling plays no part.
    /// </para>
    /// </summary>
    /// <param name="atLeast">
    /// A level the rows may reach to instead, where higher: what the item could have <b>rolled</b>, which a base with a
    /// magic level rolls above its item level - a Diadem at item level 87 at 99 (magic level 18), and Priest's (+2
    /// Paladin, level 90) is one of its affixes. The search for an item's affixes asks it (<see cref="AffixSearch"/>);
    /// the bracket does not - its item-level reading was measured on bases without one.
    /// </param>
    public static IReadOnlyList<AffixOption> ReachableBy(Item item, int atLeast = 0)
    {
        if (!Carries(item))
            return [];

        var family = Family(item);
        if (family.Count == 0)
            return [];

        int ilvl = Math.Max(Math.Min((int)item.ItemLevel, 99), Math.Min(atLeast, 99));
        string? itemClass = ItemTypes.ClassOfBase(item.Code);

        return
        [
            .. Prefixes.Concat(Suffixes).Where(row =>
                row.Id > 0

                // Weight zero is never picked, and this one is load-bearing here rather than
                // merely tidy: a boot's Faster Run/Walk prints no bracket although of Haste
                // (move2 20-20, level 12) fits it. That row's frequency is blank - a
                // classic-era duplicate the game never rolls - and counting it invents a
                // [10 - 20] the game does not draw.
                && row.Frequency > 0
                && row.RollsOn(item.Quality)
                && row.Level <= ilvl
                && row.On.Any(family.Contains)
                && !row.NotOn.Any(family.Contains)
                && row.FitsClassItem(itemClass))
        ];
    }

    /// <summary>
    /// What the game's green bracket unions over (<see cref="AffixRanges.DisplayedFor"/>): what the item could roll, and on
    /// a weapon or a quiver the auto prefixes its type admits too, by the same clauses, though none of those is the base's
    /// to draw. Read off LastChecks (screenshots): a Short Staff's Faster Cast Rate [10 - 50], Staff
    /// Tier 3's top where the affixes stop at 40; arrows' Maximum Damage [1 - 18], Arrows Tier 3's; bolts' Enemy Physical
    /// Resistance [2 - 6], no affix's; a crafted staff's Enhanced Damage [10 - 65], Knight's row, which no base draws. Not
    /// on armor: a Cap's Attack Rating stays [10 - 120] beside Knight's 121, Battle Boots' Run/Walk [10 - 40] beside Heavy's -10.
    /// </summary>
    public static IReadOnlyList<AffixOption> InBracketOf(Item item)
    {
        var reached = ReachableBy(item);
        var family = Family(item);
        if (!Carries(item) || family.Count == 0 || family.Contains("armo"))
            return reached;

        int ilvl = Math.Min((int)item.ItemLevel, 99);
        return
        [
            .. reached,
            .. _autoRows.Where(row =>
                row.Frequency > 0
                && row.RollsOn(item.Quality)
                && row.Level <= ilvl
                && row.On.Any(family.Contains)
                && !row.NotOn.Any(family.Contains)),
        ];
    }

    /// <summary>
    /// Every item type the affix tables name, which is what the filter can narrow to.
    /// Sorted by name, so the list reads rather than follows the file.
    /// </summary>
    public static IReadOnlyList<string> Families =>
        field ??=
        [
            .. _prefixes.Concat(_suffixes)
                .SelectMany(row => row.On)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(type => ItemTypes.NameOf(type) is { Length: > 0 } named
                               && !named.Equals(type, StringComparison.OrdinalIgnoreCase))
                .OrderBy(ItemTypes.NameOf, StringComparer.OrdinalIgnoreCase)
        ];

    private static HashSet<string> Lineage(string type) =>
        type.Length == 0
            ? []
            : new HashSet<string>(ItemTypes.Lineage(type), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the affix belongs to another tier of the item's own base family, rather than
    /// to something unrelated to it.
    /// <para>
    /// The item types form chains - <c>t3m</c> is a <c>t2m</c> is a <c>t1m</c> is a
    /// <c>map</c>, and the armour and weapon tiers run the same way. An affix named for a
    /// point further along the item's own chain is one the game does put on this family, and
    /// the game has recipes that move an item along that chain <b>after</b> its affixes were
    /// rolled: the map tier shuffles, and the base upgrades that turn a <c>wsd</c> into a
    /// <c>9wd</c>. So a mismatch of tier is not evidence the game could not hold it, and
    /// must not be reported as though it were.
    /// </para>
    /// </summary>
    public static bool SameFamilyOtherTier(Item item, AffixOption option)
    {
        var family = Family(item);
        if (family.Count == 0)
            return false;

        // Already allowed here, so there is nothing to excuse.
        if (option.On.Any(family.Contains))
            return false;

        // A type further down the item's own chain names the item's own type among its parents. Not any step of the
        // item's chain: a glove-only affix shares the root armo with a helm, and that is no tier of it.
        if (ItemTypes.TypeOf(item.Code) is not { } own)
            return false;

        return option.On.Any(type =>
            ItemTypes.Lineage(type).Contains(own, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The groups the item's other affixes already hold, which nothing new may repeat.
    /// Both kinds count: a group is spent across prefixes and suffixes alike.
    /// </summary>
    private static HashSet<string> GroupsSpent(Item item, bool prefix, int forSlot)
    {
        var spent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int slot = 0; slot < Slots; slot++)
        {
            foreach (bool side in new[] { true, false })
            {
                // The slot being filled is not competing with itself.
                if (side == prefix && slot == forSlot)
                    continue;

                if (At(item, slot, side) is { Group.Length: > 0 } held)
                    spent.Add(held.Group);
            }
        }

        // The auto affix spends a group too - the engine shares one group space across
        // magicprefix, magicsuffix and automagic, so an auto affix blocks a prefix the same
        // way a prefix blocks a suffix. Left out, it lets the picker
        // offer an affix the generator could not have put beside the item's own auto one.
        if (item.IsAutoAffix && item.AutoAffixId > 0
            && _autoGroups.TryGetValue(item.AutoAffixId, out var auto)
            && auto.Group.Length > 0)
        {
            spent.Add(auto.Group);
        }

        return spent;
    }

    /// <summary>
    /// The affix asking more of the item than its affix level can give, or null where they
    /// all fit. Named rather than counted: "affixes need item level 13" leaves a player with
    /// a three-affix item hunting for which one.
    /// <para>
    /// An upgraded rare or craft is measured against the best of the bases it may have rolled
    /// on (<see cref="AffixLevelsAcrossUpgrades"/>), as the legitimacy check is: the Stone Barb
    /// crafted as a Maiden Spear still read "Visceral needs item level 99" here after the
    /// check itself was fixed.
    /// </para>
    /// </summary>
    public static AffixOption? OutOfReach(Item item)
    {
        // Only where the fields are affixes at all. A Tome of Identify is a normal item
        // that keeps a 1 in the suffix field for its own reasons, and read as an affix it
        // came out as "of Protection needs item level 18" on a book.
        if (!Carries(item))
            return null;

        int reach = AffixLevelsAcrossUpgrades(item).Max();
        AffixOption? worst = null;

        for (int slot = 0; slot < Slots; slot++)
        {
            foreach (bool prefix in new[] { true, false })
            {
                // A blank or disabled row is held as level int.MaxValue so nothing rolls it: no level to answer for.
                if (At(item, slot, prefix) is { } affix && Names(affix)
                    && affix.Level > reach
                    && affix.Level > (worst?.Level ?? 0))
                {
                    worst = affix;
                }
            }
        }

        return worst;
    }

    /// <summary>
    /// The least item level at which every affix on the item is in reach: the highest affix level among
    /// them, answered as an item level on the best base it may have been on - what
    /// <see cref="OutOfReach"/> asks, asked whether or not the item already reaches it. Null where it
    /// carries no affix.
    /// </summary>
    public static int? LeastItemLevel(Item item)
    {
        if (!Carries(item))
            return null;

        int most = 0;
        for (int slot = 0; slot < Slots; slot++)
        {
            foreach (bool prefix in new[] { true, false })
            {
                if (At(item, slot, prefix) is { } affix && Names(affix))
                    most = Math.Max(most, affix.Level);
            }
        }

        return most > 0 ? ItemLevelAcrossUpgradesFor(item, most) : null;
    }

    /// <summary>The item's own type and every type above it, which is what the rows name.</summary>
    private static HashSet<string> Family(Item item)
    {
        var family = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string step in ItemTypes.LineageOfBase(item.Code))
            family.Add(step);

        return family;
    }

    /// <summary>
    /// Rolls the item's properties from the affixes it now carries, adding what is missing
    /// and removing what nothing accounts for. Returns what it said about each change.
    /// </summary>
    public static IReadOnlyList<string> Rebuild(Item item, Random random)
    {
        var told = new List<string>();

        if (item.StatLists.FirstOrDefault() is not { } list)
            return told;

        var (fresh, held, packed) = AffixRanges.Reroll(item, random, withCorruption: true);
        var accounted = AffixRanges.Accounted(item);

        foreach (var stat in list.Stats.ToList())
        {
            if (stat.Stat is not { Length: > 0 } name)
                continue;

            // By the stat and what its parameter names, the way Reroll keys its answer - two
            // lines of one stat for two different subjects are two questions, not one.
            string key = AffixRanges.KeyOf(stat);

            if (fresh.TryGetValue(key, out int rolled))
            {
                if (rolled != stat.Value)
                    told.Add($"{name} {stat.Value} -> {rolled}");

                stat.Value = rolled;
                Pack(stat, packed, key);
                continue;
            }

            // Granted but unrollable - a parameter-valued property, or a damage function
            // that names no stat - keeps what it holds.
            if (held.Contains(name) || accounted.Contains(name))
                continue;

            told.Add($"{name} removed");
            list.Stats.Remove(stat);
        }

        foreach (var (name, value) in fresh)
        {
            if (list.Stats.Any(each =>
                    string.Equals(each.Stat, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (StatCatalogue.IdOf(name) is not { } id)
                continue;

            var added = new ItemStat { Id = (ushort)id, Stat = name, Value = value };
            Pack(added, packed);

            list.Stats.Add(added);
            told.Add($"{name} {value} added");
        }

        // A staff's charges and a level-0 cast are the item level's to set, not the row's.
        AffixRanges.DerivePacked(item);

        // A flat damage as the base holds it (DamageKinds): what every writer of an item ends on.
        DamageKinds.Settle(item);
        return told;
    }

    /// <summary>
    /// An affix staged for a slot, with the numbers it would land. <c>Id</c> of zero
    /// empties the slot and carries no values.
    /// </summary>
    public sealed record AffixPick(int Id, IReadOnlyDictionary<string, int> Values);

    /// <summary>
    /// A whole new set of affixes for this item, as the Perfect Skull recipe gives a rare (the
    /// reroll-all): every slot emptied, then as many affixes as the quality
    /// rolls (<see cref="CountFor"/>), each drawn by frequency from what the item could
    /// legally roll now - its type and quality, at or below its affix level, no parameter
    /// still unproven, one per group. Drawn one at a time into the item so each draw sees the
    /// groups already spent. The item level stays; the rare name stays.
    /// <para>
    /// Returns a pick for every slot, an id of 0 emptying one - hand it to <see cref="Apply"/>.
    /// The item itself is left as it came: its slots are only borrowed while drawing.
    /// </para>
    /// </summary>
    public static Dictionary<(int Slot, bool Prefix), AffixPick> RollFresh(Item item, Random random)
    {
        var picks = new Dictionary<(int Slot, bool Prefix), AffixPick>();
        int slots = SlotsFor(item);
        if (CountFor(item).Most == 0 || item.MagicPrefixIds is null || item.MagicSuffixIds is null)
            return picks;

        ushort[] keptPrefixes = [.. item.MagicPrefixIds], keptSuffixes = [.. item.MagicSuffixIds];

        try
        {
            for (int slot = 0; slot < Slots; slot++)
            {
                if (slot < item.MagicPrefixIds.Length) item.MagicPrefixIds[slot] = 0;
                if (slot < item.MagicSuffixIds.Length) item.MagicSuffixIds[slot] = 0;
                if (slot < slots)
                {
                    picks[(slot, true)] = new AffixPick(0, new Dictionary<string, int>());
                    picks[(slot, false)] = new AffixPick(0, new Dictionary<string, int>());
                }
            }

            foreach (var (key, pick) in Draw(item, RollCount(item, random), random))
                picks[key] = pick;

            return picks;
        }
        finally
        {
            keptPrefixes.CopyTo(item.MagicPrefixIds, 0);
            keptSuffixes.CopyTo(item.MagicSuffixIds, 0);
        }
    }

    /// <summary>
    /// Draws <paramref name="wanted"/> affixes into the item's empty slots one at a time, as the game rolls a
    /// rare, a craft and a magic item, writing each id into its slot so the next draw sees its group spent.
    /// <list type="bullet">
    /// <item>Each pick flips its own coin for a side while both have room - three a side at most - so a
    /// craft's four can be 3+1 or 1+3 as well as 2+2. It was filled prefix, suffix, prefix, suffix: four were always 2+2 and one was
    /// always a prefix.</item>
    /// <item>A pick that finds nothing on its side: a rare's moves to the other side and is not spent; a
    /// craft's is lost (D2MOO).</item>
    /// <item>A magic item's one affix is a suffix two times in three: the game gives a suffix alone half the
    /// time and a prefix alone a quarter.</item>
    /// <item>Weighted by each affix's frequency, at or below the item's affix level, no parameter unproven.</item>
    /// </list>
    /// </summary>
    public static Dictionary<(int Slot, bool Prefix), AffixPick> Draw(Item item, int wanted, Random random)
    {
        var picks = new Dictionary<(int Slot, bool Prefix), AffixPick>();
        if (item.MagicPrefixIds is null || item.MagicSuffixIds is null)
            return picks;

        int slots = SlotsFor(item);
        int reach = AffixLevel(item);
        bool movesOver = item.Quality != ItemQuality.Craft;
        int prefixes = 0, suffixes = 0;

        for (int drawn = 0; drawn < wanted; drawn++)
        {
            bool roomPrefix = prefixes < slots, roomSuffix = suffixes < slots;
            if (!roomPrefix && !roomSuffix)
                break;

            bool prefix = item.Quality == ItemQuality.Magic && wanted == 1
                ? random.Next(3) == 0
                : roomPrefix && (!roomSuffix || random.Next(2) == 0);

            var pool = Available(item, prefix)
                .Where(option => option.Level <= reach && !option.NeedsParam)
                .ToList();

            // Nothing eligible on this side: a rare's pick goes to the other, which is how a rare ends up
            // carrying fewer than asked only when both are dry. A craft's is simply lost.
            if (pool.Count == 0 && movesOver && (prefix ? roomSuffix : roomPrefix))
            {
                prefix = !prefix;
                pool = [.. Available(item, prefix).Where(option => option.Level <= reach && !option.NeedsParam)];
            }

            if (pool.Count == 0)
                continue;

            int space = pool.Sum(option => Math.Max(1, option.Frequency));
            int landed = random.Next(space);
            var pick = pool[^1];
            foreach (var option in pool)
            {
                landed -= Math.Max(1, option.Frequency);
                if (landed < 0) { pick = option; break; }
            }

            int at = prefix ? prefixes++ : suffixes++;
            (prefix ? item.MagicPrefixIds : item.MagicSuffixIds)[at] = (ushort)pick.Id;
            picks[(at, prefix)] = new AffixPick(pick.Id, Roll(pick, random));
        }

        return picks;
    }

    /// <summary>
    /// Rolls an affix, one roll per property rather than one per stat - a property that
    /// fans out over several stats writes the same number to all of them, and rolling them
    /// apart would make an item the game could not.
    /// </summary>
    public static Dictionary<string, int> Roll(AffixOption option, Random? random)
    {
        var values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (code, param, min, max) in option.Grants)
        {
            int rolled = random is null || max <= min ? min : random.Next(min, max + 1);

            foreach (var wrote in AffixRanges.Written(code, min, max, rolled, param))
            {
                values[wrote.Stat] = values.TryGetValue(wrote.Stat, out int already)
                    ? already + wrote.Value
                    : wrote.Value;
            }
        }

        return values;
    }

    /// <summary>
    /// The skill and the level an affix packs into a stat, for the stats where the number is
    /// one field of three. Fixed by the affix rather than rolled, so no random is wanted:
    /// <c>of Nova</c> always casts level three.
    /// </summary>
    public static IReadOnlyDictionary<string, AffixRanges.Wrote> PackedParts(AffixOption option)
    {
        var parts = new Dictionary<string, AffixRanges.Wrote>(StringComparer.OrdinalIgnoreCase);

        foreach (var (code, param, min, max) in option.Grants)
        {
            foreach (var wrote in AffixRanges.Written(code, min, max, min, param))
            {
                // A parameter as much as a skill: a skill tree is written flat in the row
                // and split in the stat, so what the property worked out is what has to be
                // carried, not what the row said.
                if (wrote.SkillId is not null || wrote.Param is not null)
                    parts[wrote.Stat] = wrote;
            }
        }

        return parts;
    }

    /// <summary>
    /// Gives a stat the fields its shape needs beyond the number. A packed property is three
    /// numbers wearing one row, and a row written from the value alone reads "chance to cast
    /// level 0 (none)" however right the chance is.
    /// </summary>
    private static void Pack(
        ItemStat stat, IReadOnlyDictionary<string, AffixRanges.Wrote> packed, string? key = null)
    {
        if (stat.Stat is not { Length: > 0 } name)
            return;

        if (!packed.TryGetValue(key ?? AffixRanges.KeyOf(stat), out var parts))
            return;

        if (parts.SkillId is not null)
        {
            stat.SkillId = parts.SkillId;
            stat.SkillLevel = parts.SkillLevel;
            stat.MaxCharges = parts.MaxCharges;
        }

        if (parts.Param is not { } param)
            return;

        stat.Param = param;

        // A skill tree keeps the same number twice: as the parameter the file writes, and
        // split into the two fields the wording reads.
        if (name.Equals("item_addskill_tab", StringComparison.OrdinalIgnoreCase))
        {
            stat.SkillTab = param & 0x7;
            stat.CharacterClass = (param >> 3) & 0x1fff;
        }
    }

    /// <summary>
    /// Puts staged affixes on the item and makes its properties match - <b>but only the
    /// properties the change actually touches</b>.
    /// <para>
    /// Rebuilding everything was the first attempt, and it meant swapping a prefix in slot
    /// three quietly rerolled the attack rating in slot one. What a change is entitled to
    /// move is the stats the affix leaving wrote and the stats the affix arriving writes;
    /// everything else on the item is left exactly as it stands.
    /// </para>
    /// <para>
    /// Inside that set the split is still not recoverable - two affixes granting cold
    /// resist are stored added together with no record of the share - so every source of a
    /// touched stat is rolled again, except the staged affix itself, which uses the number
    /// staged for it. A stat nothing writes any more goes.
    /// </para>
    /// <para>
    /// A fixed source - a craft's, a base upgrade's - is not rolled again, and keeps its part: what the line held
    /// less the least the rolled sources could have given it, within the fixed source's own range
    /// (<see cref="AffixRanges.Shares"/>). Exact where the fixed source and the affix are the line's only two. It was
    /// left out: a Caster amulet's 7% Faster Cast Rate went when "of the Apprentice" came, the line 10 where its
    /// sources give 15-20 - and taking the affix off kept the 17.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Apply(
        Item item, IReadOnlyDictionary<(int Slot, bool Prefix), AffixPick> picks, Random random)
    {
        var told = new List<string>();

        if (item.StatLists.FirstOrDefault() is not { } list || picks.Count == 0)
            return told;

        // What the change is allowed to move: what is leaving, and what is arriving.
        var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ((slot, prefix), pick) in picks)
        {
            foreach (string stat in At(item, slot, prefix)?.Stats ?? [])
                touched.Add(stat);

            foreach (string stat in Find(pick.Id, prefix)?.Stats ?? [])
                touched.Add(stat);
        }

        // The rolled sources' least, asked while the affixes leaving are still on: what the line held before is theirs
        // and the fixed part's together.
        var (_, rolledLeast) = AffixRanges.Shares(item);

        // Everything the item would hold with the staged slots emptied. Emptying them for
        // the question rather than copying the item keeps this out of the save layer - the
        // ids are about to be written anyway.
        foreach (var ((slot, prefix), _) in picks)
        {
            var ids = prefix ? item.MagicPrefixIds : item.MagicSuffixIds;
            if (ids is not null && slot >= 0 && slot < ids.Length)
                ids[slot] = 0;
        }

        // The fixed sources' reach, of what stays: a leaving affix's own unranged part is no fixed source's. Asked with
        // it still on, "of Ages"'s Indestructible was kept as a craft's part would be, and stayed when "of
        // Teleportation" took its place (a staff).
        var fixedParts = AffixRanges.Shares(item).Fixed;

        var (others, held, rolled) = AffixRanges.Reroll(item, random, withCorruption: true);

        // Copied, because the staged affix is about to overrule some of it.
        var packed = new Dictionary<string, AffixRanges.Wrote>(rolled, StringComparer.OrdinalIgnoreCase);

        foreach (var ((slot, prefix), pick) in picks)
        {
            var ids = prefix ? item.MagicPrefixIds : item.MagicSuffixIds;
            if (ids is not null && slot >= 0 && slot < ids.Length)
                ids[slot] = (ushort)Math.Max(0, pick.Id);
        }

        // The staged numbers, added where two staged affixes write the same stat.
        var staged = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var ((_, prefix), pick) in picks)
        {
            // Keyed like everything else here: the affix's own packed parts say what its
            // parameter names, so two affixes granting one stat for two subjects stay apart.
            var mineParts = Find(pick.Id, prefix) is { } row ? PackedParts(row) : null;

            foreach (var (stat, value) in pick.Values)
            {
                string key = mineParts is not null && mineParts.TryGetValue(stat, out var part)
                    ? AffixRanges.KeyOf(part)
                    : stat;

                staged[key] = staged.TryGetValue(key, out int already) ? already + value : value;
            }

            // The staged affix's own packed parts win over what the item's remaining sources
            // would have written, since it is the one being chosen.
            if (Find(pick.Id, prefix) is { } chosen)
            {
                foreach (var parts in PackedParts(chosen).Values)
                    packed[AffixRanges.KeyOf(parts)] = parts;
            }
        }

        // A touched stat may answer to more than one key - the item can carry it twice for two
        // different subjects, and each line is its own question. Every key the sources offer
        // for that stat, plus every key the item already holds it under.
        var asked = new List<(string Stat, string Key)>();

        foreach (string stat in touched)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string key in others.Keys.Concat(staged.Keys).Concat(packed.Keys))
            {
                if (ItemStats.NameIn(key).Equals(stat, StringComparison.OrdinalIgnoreCase))
                    keys.Add(key);
            }

            foreach (var line in ItemStats.Every(item))
            {
                if (line.Stat is { Length: > 0 } had
                    && had.Equals(stat, StringComparison.OrdinalIgnoreCase))
                {
                    keys.Add(AffixRanges.KeyOf(line));
                }
            }

            if (keys.Count == 0)
                keys.Add(stat);

            foreach (string key in keys)
                asked.Add((stat, key));
        }

        foreach (var (stat, key) in asked)
        {
            bool fromOthers = others.TryGetValue(key, out int rest);
            bool fromStaged = staged.TryGetValue(key, out int mine);

            var carried = ItemStats.FindKeyed(item, key);

            // A fixed source's part of the line, kept.
            int? kept = carried is not null && fixedParts.TryGetValue(key, out var fixedPart)
                ? Math.Clamp(carried.Value - (rolledLeast.TryGetValue(key, out int least) ? least : 0), fixedPart.Min, fixedPart.Max)
                : null;

            if (!fromOthers && !fromStaged)
            {
                // Nothing rolled writes it any more: a fixed source's part is what stays.
                if (carried is not null && kept is { } alone)
                {
                    if (carried.Value != alone)
                        told.Add($"{stat} {carried.Value} -> {alone}");
                    carried.Value = alone;
                    continue;
                }

                // Nothing writes it any more - unless it never had a range to begin with,
                // in which case it is still granted and keeps what it holds.
                if (carried is null || held.Contains(stat))
                    continue;

                told.Add($"{stat} removed");
                ItemStats.Remove(item, carried);
                continue;
            }

            int total = (fromOthers ? rest : 0) + (fromStaged ? mine : 0) + (kept ?? 0);

            // Held at what the field holds, as ItemStats.WriteValues holds it: Cannot Be Frozen is one bit, and 1 + 1 written
            // would read back 0. A packed line's number is not a plain value.
            if (!packed.ContainsKey(key))
                total = Math.Min(total, StatFields.CeilingFor(item, stat));

            if (carried is null)
            {
                if (StatCatalogue.IdOf(stat) is not { } id)
                    continue;

                var added = new ItemStat { Id = (ushort)id, Stat = stat, Value = total };
                Pack(added, packed, key);

                list.Stats.Add(added);
                told.Add($"{stat} {total} added");
                continue;
            }

            if (carried.Value != total)
                told.Add($"{stat} {carried.Value} -> {total}");

            carried.Value = total;
            Pack(carried, packed, key);
        }

        if (touched.Contains(ItemForge.SocketCount))
            SettleSockets(item, told);

        AffixRanges.DerivePacked(item, touched);

        // A flat damage as the base holds it (DamageKinds): what every writer of an item ends on.
        DamageKinds.Settle(item);
        return told;
    }

    /// <summary>
    /// Writes a whole new set of affixes - <see cref="RollFresh"/>'s - onto an item that keeps
    /// everything else it has.
    /// <para>
    /// <b>Not through <see cref="Apply"/></b>, which rebuilds a touched stat from the other
    /// sources' rerolled numbers plus the new affix: a crafted item's recipe share has no
    /// range, so it fell out of that sum, and 1 roll in 7 over the market's rares and crafts
    /// came back flagged. Instead the old affixes come off by subtraction - the
    /// corruption clean's own <see cref="Corruptions.TakeOff"/>, which keeps what the recipe,
    /// the corruption, the desecration or the automagic put on a line - and the new ones are
    /// added on top with <see cref="ItemStats.WriteValues"/>.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> ApplyFresh(
        Item item, IReadOnlyDictionary<(int Slot, bool Prefix), AffixPick> picks, Random random)
    {
        var told = new List<string>();
        if (item.MagicPrefixIds is null || item.MagicSuffixIds is null)
            return told;

        ItemStats.Home(item);

        // A desecration is told from the lines around it, and those are about to move: told
        // now and kept, as a corruption's clean does.
        Desecrations.Pin(item);

        // And a crafted item's recipe, which its old affixes may be what tells - see PinRecipe.
        AffixRanges.PinRecipe(item);

        var old = new List<CorruptionGrant>();
        bool sockets = false;

        for (int slot = 0; slot < Slots; slot++)
        {
            foreach (bool prefix in new[] { true, false })
            {
                if (At(item, slot, prefix) is not { } option)
                    continue;

                old.AddRange(option.Grants.Select(grant => new CorruptionGrant(grant.Code, grant.Min, grant.Max, grant.Param)));
                sockets |= option.Stats.Contains(ItemForge.SocketCount, StringComparer.OrdinalIgnoreCase);
            }
        }

        Array.Clear(item.MagicPrefixIds);
        Array.Clear(item.MagicSuffixIds);

        foreach (var moved in Corruptions.TakeOff(item, old, random, withCorruption: true))
        {
            told.Add(moved.Removed ? $"{moved.Stat} removed" : $"{moved.Stat} {moved.Was} -> {moved.Now}");
        }

        var values = new List<AffixRanges.Wrote>();

        foreach (var ((slot, prefix), pick) in picks)
        {
            if (pick.Id <= 0 || Find(pick.Id, prefix) is not { } option)
                continue;

            (prefix ? item.MagicPrefixIds : item.MagicSuffixIds)[slot] = (ushort)pick.Id;
            sockets |= option.Stats.Contains(ItemForge.SocketCount, StringComparer.OrdinalIgnoreCase);

            var parts = PackedParts(option);
            foreach (var (stat, value) in pick.Values)
            {
                values.Add(parts.TryGetValue(stat, out var part)
                    ? part with { Value = value }
                    : new AffixRanges.Wrote(stat, value));
            }
        }

        ItemStats.WriteValues(item, values);
        AffixRanges.DerivePacked(item, values.Select(each => each.Stat).ToHashSet(StringComparer.OrdinalIgnoreCase));

        if (sockets)
            SettleSockets(item, told);

        // A flat damage as the base holds it (DamageKinds): what every writer of an item ends on.
        DamageKinds.Settle(item);
        return told;
    }

    /// <summary>
    /// A socket affix set on an item a corruption gave sockets: the affix goes first, so the corruption is cleaned
    /// before the picks are written (else a rare's 3 socket corruption holds a Jeweler's at 3 until
    /// the corruption is taken off). Whatever sits in the sockets stays. False where there is nothing to clean.
    /// </summary>
    public static bool CleanSocketCorruptionFor(Item item, IReadOnlyDictionary<(int Slot, bool Prefix), AffixPick> picks, Random random)
    {
        if (Corruptions.IdOn(item) != Corruptions.Sockets
            || !picks.Any(each => each.Value.Id > 0 && Find(each.Value.Id, each.Key.Prefix) is { } option
                && option.Stats.Contains(ItemForge.SocketCount, StringComparer.OrdinalIgnoreCase)))
            return false;

        byte filled = item.NumberOfSocketedItems;
        Corruptions.Clean(item, random);
        // Extended items only. On a compact one this bit is a quest flag, not a count.
        if (!item.IsCompact)
            item.NumberOfSocketedItems = filled;
        return true;
    }

    /// <summary>
    /// Sockets are the item's header count, never a stat - not one listing on the PD2 market
    /// stores item_numsockets, of any quality. So a change that touches a socket prefix -
    /// Mechanist's, Artificer's, Jeweler's arriving or leaving - ends with the count in the
    /// header and no such line, as ItemForge does for a unique's row.
    /// </summary>
    private static void SettleSockets(Item item, List<string> told)
    {
        {
            int sockets = 0;

            while (ItemStats.Find(item, ItemForge.SocketCount) is { } line)
            {
                sockets += line.Value;
                ItemStats.Remove(item, line);
            }

            // A corruption's sockets are its own, and a socket prefix neither adds to them nor takes
            // them away: the socket corruption needs an item with none, so in game the two never meet.
            // Taking the larger let a Jeweler's rolled onto a rare armour the corruption gave three lift
            // it to four, and the next roll kept the four with no Jeweler's (rollcheck x2).
            if (Corruptions.IdOn(item) == Corruptions.Sockets)
                sockets = item.TotalNumberOfSockets;
            // And no more than the base takes at its level, as the game caps any socket roll -
            // a socket affix offering three on a claw that holds two gives two. A socket
            // corruption's count is under the same ceiling (see Legitimacy.Sockets), so it is
            // capped too: exempt, a Jeweler's rolled onto a socket-corrupted mace gave it four
            // where the base holds three (rollcheck).
            if (ItemBases.MaximumSockets(item) is > 0 and var room)
                sockets = Math.Min(sockets, room);
            sockets = Math.Clamp(Math.Max(sockets, item.SocketedItems.Count), 0, 15);

            // Said as sockets, not as the stat the lines above passed through on the way here.
            told.RemoveAll(said => said.StartsWith(ItemForge.SocketCount, StringComparison.OrdinalIgnoreCase));

            if (sockets != item.TotalNumberOfSockets)
                told.Add($"sockets {item.TotalNumberOfSockets} -> {sockets}");

            item.TotalNumberOfSockets = (byte)sockets;
            item.IsSocketed = sockets > 0;
        }
    }

    public static void Load(GameData game)
    {
        // Another install's tables: what was worked out from the last is not this one's.
        _disabledPrefixes.Clear();
        _disabledSuffixes.Clear();
        _typesNamed = null;

        _prefixes = Read(game, "magicprefix");
        _suffixes = Read(game, "magicsuffix");
        _earlier = [];
        _autoRows = Read(game, "automagic");
        _autoGroups = ReadAutoGroups(game);
    }

    private static Dictionary<int, (string Name, string Group)> ReadAutoGroups(GameData game)
    {
        var found = new Dictionary<int, (string, string)>();

        try
        {
            if (game.Lines("automagic") is not { } lines)
                return found;
            if (lines.Length == 0)
                return found;

            string[] header = lines[0].Split('\t');
            int name = Array.IndexOf(header, "Name");
            int group = Array.IndexOf(header, "group");

            if (group < 0)
                return found;

            var rows = lines.Skip(1).ToList();
            while (rows.Count > 0 && rows[^1].Trim().Length == 0)
                rows.RemoveAt(rows.Count - 1);

            for (int row = 0; row < rows.Count; row++)
            {
                string[] cells = rows[row].Split('\t');
                if (cells.Length <= group || cells[group].Trim().Length == 0)
                    continue;

                found[row + 1] = (
                    name >= 0 && cells.Length > name ? cells[name].Trim() : "",
                    cells[group].Trim());
            }
        }
        catch (Exception)
        {
            // A missing or malformed table leaves the map empty, which only means the
            // group check has nothing to say - never that an item is wrong.
        }

        return found;
    }

    /// <summary>
    /// Two affixes on this item drawn from the same exclusion group, or null where there are
    /// none.
    /// <para>
    /// <b>The engine will not pick two affixes from one group</b>, and the group space is
    /// shared across <c>magicprefix</c>, <c>magicsuffix</c> and <c>automagic</c> - so a
    /// prefix can block a suffix, and an auto affix can block either. Groups exist to keep
    /// contradictory affixes apart, which is why two from one group is a thing the generator
    /// could not have produced.
    /// </para>
    /// <para>
    /// Measured across both vaults before this was written: <b>0 items violate it</b>. It is
    /// a guard against forgery and against our own editor, not a response to anything seen.
    /// </para>
    /// </summary>
    public static (string Group, string First, string Second)? GroupClash(Item item)
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Note(string group, string name, ref (string, string, string)? clash)
        {
            if (clash is not null || group.Length == 0)
                return;

            if (seen.TryGetValue(group, out string? already))
                clash = (group, already, name);
            else
                seen[group] = name;
        }

        (string, string, string)? found = null;

        if (item.IsAutoAffix && item.AutoAffixId > 0
            && _autoGroups.TryGetValue(item.AutoAffixId, out var auto))
        {
            Note(auto.Group, auto.Name.Length > 0 ? auto.Name : L($"auto affix {item.AutoAffixId}"),
                 ref found);
        }

        if (Carries(item))
        {
            for (int slot = 0; slot < Slots; slot++)
            {
                foreach (bool side in new[] { true, false })
                {
                    if (At(item, slot, side) is { } held)
                        Note(held.Group, held.Name, ref found);
                }
            }
        }

        return found;
    }

    private static AffixOption[] Read(GameData game, string table) =>
        Read(game.Lines(table), prefix: table == "magicprefix", noteDisabled: true);

    /// <summary>
    /// An affix table's rows by id. <paramref name="noteDisabled"/>: the rows that cannot spawn are named for the findings -
    /// this build's alone, an earlier build's leave the names as they are.
    /// </summary>
    private static AffixOption[] Read(string[]? lines, bool prefix, bool noteDisabled)
    {
        try
        {
            if (lines is null || lines.Length == 0)
                return [];

            string[] header = lines[0].Split('\t');

            // An affix id is a zero-based index into the data rows, blank rows counted.
            // The file's trailing newline is not a row - counting it is what once shifted
            // the rare numbering by one - so the list is cut back to the last real line.
            var rows = lines.Skip(1).ToList();
            while (rows.Count > 0 && rows[^1].Trim().Length == 0)
                rows.RemoveAt(rows.Count - 1);

            var found = new List<AffixOption>();

            for (int id = 0; id < rows.Count; id++)
            {
                string[] cells = rows[id].Split('\t');

                string Cell(string name)
                {
                    int column = Array.IndexOf(header, name);
                    return column >= 0 && cells.Length > column ? cells[column].Trim() : "";
                }

                // A blank row still holds its number, so it is kept as an unusable entry
                // rather than skipped - the ids after it depend on it being there.
                if (cells.Length < header.Length || Cell("Name") is not { Length: > 0 } name)
                {
                    found.Add(Nothing(id, prefix));
                    continue;
                }

                var grants = new List<(string, int, int, int)>();

                for (int n = 1; n <= 3; n++)
                {
                    if (Cell($"mod{n}code") is not { Length: > 0 } code)
                        continue;

                    int param = AffixRanges.ParameterOf(code, Cell($"mod{n}param"));
                    _ = int.TryParse(Cell($"mod{n}min"), out int min);
                    _ = int.TryParse(Cell($"mod{n}max"), out int max);
                    var (first, second) = AffixRanges.Ordered(code, min, max);
                    grants.Add((code, param, first, second));
                }

                _ = int.TryParse(Cell("level"), out int level);
                _ = int.TryParse(Cell("maxlevel"), out int maxLevel);
                _ = int.TryParse(Cell("frequency"), out int frequency);

                string[] Pick(string stem, int count) =>
                [
                    .. Enumerable.Range(1, count)
                        .Select(n => Cell($"{stem}{n}"))
                        .Where(each => each.Length > 0)
                ];

                // The Name column is a string key, not the words the game prints. Most keys
                // resolve to themselves, which is how this went unnoticed, but 197 do not
                // and some are different words rather than spellings: `Blank` is shown as
                // "Void", `Assamic` as "Lunar", `of Warmth` as "of Thawing". Caught by
                // hovering a glove in game that the editor called "Athlete's" and the game
                // called "Athletic".
                string shown = StringTable.Current?.Get(name) is { Length: > 0 } worded
                    ? worded
                    : name;

                if (Cell("spawnable") == "1")
                {
                    _ = int.TryParse(Cell("levelreq"), out int required);
                    _ = int.TryParse(Cell("classlevelreq"), out int classRequired);

                    // The rare column: blank a magic item's alone, 1 a rare's and a craft's as well, 3 (Season 14) a
                    // rare's alone - "a dropped rare only affix", the patch notes say of Visceral and Celestial, the two
                    // rows it turned from 1 to 3. Read as 1 alone, every rare-only affix was offered on magic items and
                    // on no rare (the table sweep). Not on a craft either: the wiki's Rarity column gives all
                    // 46 rows "R", where every 1 is "M, R, C" (Item_Affixes).
                    string rare = Cell("rare");
                    found.Add(new AffixOption(
                        id, prefix, shown, Cell("group"), level, maxLevel, frequency,
                        [.. grants], Pick("itype", 7), Pick("etype", 5), rare is "1" or "3",
                        required, Cell("class"), classRequired, RareOnly: rare == "3", ClassSpecific: Cell("classspecific")));
                    continue;
                }

                // Not spawnable, so it is no use to a picker or a roll and is kept as an
                // empty slot - but the row is **there and has a name**, and anything
                // reporting on an item that carries it should say so. Told that "no prefix
                // in the game has id 217", the obvious next move is to go looking for a
                // missing row; the row is `Maroon`, and PD2 simply switched it off.
                if (noteDisabled)
                    (prefix ? _disabledPrefixes : _disabledSuffixes)[id] = shown;
                found.Add(Nothing(id, prefix));
            }

            return [.. found];
        }
        catch (Exception)
        {
            // An empty pool leaves the picker with nothing to offer, which is visible and
            // recoverable; a half-read one would offer affixes the game refuses.
            return [];
        }
    }

    /// <summary>
    /// A row that holds its place in the numbering and nothing else - blank, or one the
    /// table says cannot spawn. It names no types, so no item ever matches it.
    /// </summary>
    private static AffixOption Nothing(int id, bool prefix) =>
        new(id, prefix, "", "", int.MaxValue, 0, 0, [], [], [], false);
}
