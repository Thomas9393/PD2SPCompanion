using System.Text;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// One property an item can be given, as something to pick from a list. A stat that names a class,
/// a tree or a skill is one choice per <paramref name="Param"/>, worded for it (<paramref name="Worded"/>),
/// as the trade site lists them - one line of "+X to Javelin and Spear Skills" for the whole stat
/// told the picker the tree before anyone chose one.
/// </summary>
public sealed record StatChoice(int Id, string Name, int? Param = null, string? Worded = null) : IPropertyChoice
{
    public string IdText => $"stat {Id}";

    /// <summary>
    /// How the list reads: the id, then what the property does.
    /// <para>
    /// The table's name is the engine's - <c>item_elemskill_fire</c>, <c>damageresist</c> -
    /// and says nothing to anyone who has not read the tables. Where the game has words of
    /// its own they are used instead; the internal name is the fallback for the few it does
    /// not word, since an id alone is worse than a name nobody loves.
    /// </para>
    /// </summary>
    public string Label => $"{Id} - {Text}";

    /// <summary>What the property does, in the game's words where it has any - the picker's row.</summary>
    public string Text => Worded ?? (HasWording ? Wording : Name);

    /// <summary>
    /// The table's name is the engine's, not anyone's idea of English. This is the game's own
    /// wording for it, where the tables have any - "+1 to Fire Skills" beside
    /// <c>item_elemskill_fire</c> - so the list can be read by what a property does rather
    /// than by what it is called.
    /// </summary>
    public string Wording => StatText.Template(Name) ?? "";

    public bool HasWording => Wording.Length > 0;

    /// <summary>What the list says about it beyond its name, where there is anything.</summary>
    public string Note => StatCatalogue.NoteOn(Id);

    public bool HasNote => Note.Length > 0;

    /// <summary>A map's alone - see <see cref="StatCatalogue.IsMapOnly"/>.</summary>
    public bool IsMapOnly => StatCatalogue.IsMapOnly(Name);

    public override string ToString() => Label;
}

/// <summary>
/// Every property an item can carry, from <c>itemstatcost.txt</c>, and the handful that
/// cannot stand alone.
/// </summary>
public static class StatCatalogue
{
    /// <summary>
    /// Whether a stat belongs to maps alone - the `map_` family, 104 stats in S13 (monster
    /// damage and resists, area level, density, drop bonuses...). Measured: the only
    /// sources are the eight unique maps (t51-t58) and the map affixes, whose rows are limited
    /// to the map types t1m-t4m, and no map property writes any other stat. The pickers and the
    /// item panel say "(maps)" beside them so they can be told apart from the rest at a glance.
    /// With them the marks the cube's map recipes leave (<see cref="MapRecipes.Marks"/>): `heroic` and
    /// `treacherous` are a map's too, whatever their names.
    /// </summary>
    public static bool IsMapOnly(string? stat) =>
        stat?.StartsWith("map_", StringComparison.OrdinalIgnoreCase) == true || MapRecipes.Marks(stat);

    /// <summary>
    /// Whether the item panel's Add property lists a stat for an item: **a map is offered a map's
    /// properties and nothing else, and nothing else is offered a map's**. The
    /// two corruption marks stay on a map's list: real maps carry them (72 of the 150 the game rolled),
    /// and they are on every other item's. What an item already holds is not asked
    /// of this - its rows stay - and the raw list is every stat by design.
    /// </summary>
    public static bool OfferedOn(string? stat, bool map) =>
        // The elemental skill words go on with their grant and are never picked (ElementSkills).
        ElementSkills.IsLabel(stat) ? false
        : !map ? !IsMapOnly(stat)
        : IsMapOnly(stat)
          || string.Equals(stat, Corruptions.IdStat, StringComparison.OrdinalIgnoreCase)
          || string.Equals(stat, Corruptions.FlagStat, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The properties the game writes as a run: an id, then one or two more with no id of
    /// their own after it.
    /// <para>
    /// This is not in any table - the reader and the writer both hardcode it, from D2's own
    /// item code - so it is hardcoded here too and must agree with them. It matters because
    /// <b>writing a head without its followers throws</b>: the writer reaches past the end
    /// of the list looking for them. Adding cold damage means adding all three.
    /// </para>
    /// </summary>
    private static readonly Dictionary<int, int[]> Runs = new()
    {
        [17] = [18],          // item_maxdamage_percent -> item_mindamage_percent
        [48] = [49],          // firemindam  -> firemaxdam
        [50] = [51],          // lightmindam -> lightmaxdam
        [52] = [53],          // magicmindam -> magicmaxdam
        [54] = [55, 56],      // coldmindam  -> coldmaxdam, coldlength
        [57] = [58, 59],      // poisonmindam -> poisonmaxdam, poisonlength
    };

    private static readonly HashSet<int> Followers = [.. Runs.Values.SelectMany(ids => ids)];

    /// <summary>
    /// Properties the table still carries and the engine no longer reads.
    /// <para>
    /// Empty, and kept for the next one. <c>item_elemskill</c> (126) was listed here for a
    /// few hours on the strength of a ring that showed no line for it - and a Leaf runeword
    /// then proved the mod still writes it, beside <c>item_elemskill_fire</c>. What the ring
    /// showed was that <b>126 has no wording</b>, not that nothing uses it: the table gives
    /// it no <c>descfunc</c> and no <c>descstrpos</c>, so the game has nothing to print for
    /// it, and the line you see comes from its partner.
    /// </para>
    /// <para>
    /// The lesson for the next candidate: a property that shows nothing in game may be one
    /// the game cannot word, which is not the same as one it does not read.
    /// </para>
    /// </summary>
    private static readonly HashSet<int> Retired = [];

    /// <summary>
    /// Properties that only put a line on the tooltip.
    /// <para>
    /// The five elemental skill stats, 362 to 366, are the wording half of a pair: they carry
    /// the strings and the game prints them, and they do nothing else. Rings holding only
    /// these showed their line and granted no skills at all, while
    /// <c>item_elemskill</c> (126) - which has no wording anywhere - grants them and shows
    /// nothing. A real item carries both, which is what a Leaf runeword does.
    /// </para>
    /// <para>
    /// Worth saying on the row and in the list: one of these on its own is a label on an item
    /// that does not do what the label says.
    /// </para>
    /// </summary>
    private static readonly HashSet<int> Cosmetic = [362, 363, 364, 365, 366];

    public static bool IsObsolete(int id) => Retired.Contains(id);

    public static bool IsObsolete(string? name) =>
        name is { Length: > 0 } && IdOf(name) is { } id && Retired.Contains(id);

    /// <summary>What to say about a property beyond its name, or empty for most of them.</summary>
    public static string NoteOn(int id) =>
        Retired.Contains(id) ? "(obsolete)"
        : Cosmetic.Contains(id) ? "(UI)"
        : _engineOnly.Contains(id) ? "(engine)"
        : "";

    /// <summary>
    /// Stats no property writes and the game has no way to word: the engine's own bookkeeping -
    /// a character's gold and life, a skill's running state, a missile's pierce count. Measured:
    /// 120 of S13's 503, and none on any vault item. They stay in the list, since free
    /// editing may still want one, but after every property an item can be given.
    /// </summary>
    private static HashSet<int> _engineOnly = [];

    public static bool IsEngineOnly(int id) => _engineOnly.Contains(id);

    public static string NoteOn(string? name) =>
        name is { Length: > 0 } && IdOf(name) is { } id ? NoteOn(id) : "";

    /// <summary>
    /// Everything that can be picked, in id order. A follower is left out: it is added with
    /// the property it belongs to, and on its own it says half of something.
    /// </summary>
    public static IReadOnlyList<StatChoice> All { get; private set; } = [];

    /// <summary>
    /// What the item panel's picker offers: <see cref="All"/>, with each stat that names a class, a tree
    /// or a skill given as its lines - one per class, tree and skill (<see cref="PropertyIndex.ByParameter"/>),
    /// as the trade site and the vault's search list them.
    /// </summary>
    public static IReadOnlyList<StatChoice> Pickable() =>
    [
        .. All.Where(choice => !PropertyIndex.IsByParameter(choice.Name)),
        .. PropertyIndex.ByParameter()
            .Where(line => IdOf(line.Stat) is not null)
            .Select(line => new StatChoice(IdOf(line.Stat)!.Value, line.Stat, line.Param, line.Label)),
    ];

    /// <summary>The whole run a property belongs to, itself first. One entry for most.</summary>
    public static IReadOnlyList<int> RunFrom(int id) =>
        Runs.TryGetValue(id, out int[]? rest) ? [id, .. rest] : [id];

    /// <summary>
    /// What the table calls a property. Asked of every row, not of the pickable ones: a
    /// follower is added by name like any other and is not on that list.
    /// </summary>
    public static string? NameOf(int id) =>
        _named.TryGetValue(id, out string? name) ? name : null;

    private static Dictionary<int, string> _named = [];

    /// <summary>The id a property is stored under, or null for a name no table carries.</summary>
    public static ushort? IdOf(string? name) =>
        name?.Trim() is { Length: > 0 } wanted && _numbered.TryGetValue(wanted, out int id)
            ? (ushort)id
            : null;

    private static Dictionary<string, int> _numbered = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the game would ever put a property on this base at all.
    /// <para>
    /// Saving a full record is not the test. A Worldstone Shard, a Skeleton Key, a Town
    /// Portal Book, the Horadric Cube and a throwing potion all save full - they have
    /// somewhere to put a property - and none of them can be anything but normal.
    /// <c>itemtypes.txt</c> says which types can be magic or rare, so that is what is
    /// asked. See <see cref="ItemTypes.CanRollAffixes"/>.
    /// </para>
    /// <para>
    /// It draws the line in the right place across all 830 bases: out go potions of every
    /// kind including the throwing ones, gems, runes and rune stacks, quest items, keys,
    /// books, scrolls, the cube, shards, uber organs, body parts, crafting materials,
    /// Jewel Fragments and a Torch Fragment; in stay jewels, all three charms, rings,
    /// amulets, quivers and everything in the armour and weapon tables.
    /// </para>
    /// </summary>
    public static bool CanCarry(string? code) =>
        ItemTypes.CanRollAffixes(ItemTypes.TypeOf(code));

    public static void Load(GameData game)
    {
        var found = new List<StatChoice>();
        var named = new Dictionary<int, string>();
        var numbered = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (game.Lines("itemstatcost") is not { } lines)
                return;
            if (lines.Length == 0)
                return;

            string[] header = lines[0].Split('\t');
            int id = Array.IndexOf(header, "ID");
            int stat = Array.IndexOf(header, "Stat");
            if (id < 0 || stat < 0)
                return;

            foreach (string line in lines.Skip(1))
            {
                string[] cells = line.Split('\t');
                if (cells.Length <= Math.Max(id, stat))
                    continue;

                if (!int.TryParse(cells[id].Trim(), out int number)
                    || cells[stat].Trim() is not { Length: > 0 } name)
                {
                    continue;
                }

                named[number] = name;
                numbered[name] = number;

                if (!Followers.Contains(number))
                    found.Add(new StatChoice(number, name));
            }
        }
        catch (Exception)
        {
            // Without the table nothing can be added, which is better than offering a list
            // of properties the save cannot hold.
        }

        var written = WrittenByProperties(game);
        _engineOnly = [.. found
            .Where(choice => !written.Contains(choice.Name) && StatText.FunctionOf(choice.Name) == 0)
            .Select(choice => choice.Id)];

        All = [.. found.OrderBy(choice => _engineOnly.Contains(choice.Id)).ThenBy(choice => choice.Id)];
        _named = named;
        _numbered = numbered;
    }

    /// <summary>Every stat some row of <c>properties.txt</c> writes, in its stat1 to stat7.</summary>
    private static HashSet<string> WrittenByProperties(GameData game)
    {
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (game.Lines("properties") is not { } lines)
                return written;
            string[] header = lines[0].Split('\t');
            int[] columns = [.. Enumerable.Range(1, 7)
                .Select(n => Array.IndexOf(header, $"stat{n}"))
                .Where(index => index >= 0)];

            foreach (string line in lines.Skip(1))
            {
                string[] cells = line.Split('\t');
                foreach (int column in columns)
                {
                    if (column < cells.Length && cells[column].Trim() is { Length: > 0 } stat)
                        written.Add(stat);
                }
            }
        }
        catch (Exception)
        {
            // Without the table nothing is marked engine-only, which errs toward showing.
        }

        return written;
    }

}
