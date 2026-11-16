using System.Text;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// The skill bonus a class-specific base rolls on its own.
/// <para>
/// A Wand, an Orb, a Katar and the rest carry a <c>StaffMods</c> class in
/// <c>itemtypes.txt</c>, and a magic or rare one of them rolls a skill from that class.
/// <b>No affix table grants it</b> - <c>item_singleskill</c> appears in no row of
/// <c>automagic</c>, <c>magicprefix</c> or <c>magicsuffix</c>, which is why the property
/// sat with no range against it for as long as it did. The game rolls it from the type,
/// and the level it rolls, <b>1 to 3</b>, is engine behaviour with no table to read: it is
/// carried here the way the gold caps and the item level ceiling are, as a stated constant
/// rather than a guessed one.
/// </para>
/// </summary>
public static class StaffMods
{
    /// <summary>The stat a staff mod grants.</summary>
    public const string Stat = "item_singleskill";

    /// <summary>
    /// What one of them rolls. The same 1-3 for every quality: PD2's General Changes gives
    /// the distribution as 40% +1, 35% +2, 25% +3, against vanilla's 60/30/10, so the mod
    /// <b>changed the odds and not the range</b> - and said nothing about quality, which is
    /// what settles whether a rare rolls lower. It does not.
    /// </summary>
    public static readonly RollRange Levels = new(1, 3);

    /// <summary>
    /// How many an item may carry: none to three, at 10/40/30/20 in PD2 against vanilla's
    /// 30/40/20/10. Not used yet - each one is its own stat line with its own range, so the
    /// count does not change what any single line may hold. It would matter to a check that
    /// asked whether an item has too many.
    /// </summary>
    public const int MostPerItem = 3;

    /// <summary>
    /// The skills a dagger's staffmods are drawn from, by their internal names. <b>Not the table's</b>: itemtypes.txt
    /// gives daggers (<c>knif</c>) the Assassin's, and the PD2 wiki says "dagger-related skills (Poison Strike and most
    /// martial arts skills)". Measured (SkeleStrafe): 32 magic daggers the game made at item
    /// level 99 from a test cube recipe (pd2spc daggertest), 46 staffmods - Poison Strike (the Necromancer's), nine
    /// martial arts, Blade Shield, Blade Fury, Blade Sentinel, and nothing else: no trap, no shadow skill, which 46
    /// draws from the Assassin's thirty would all but never miss. The 29 ladder and trade daggers agree. Not seen and
    /// so not offered: Dragon Claw, Claw Mastery, Weapon Block, Blade Dance - each about 1 in 25 to have been missed if
    /// it were in the pool. (Phoenix Strike's internal name is the vanilla "Royal Strike"; Poison Strike's "Poison Dagger".)
    /// </summary>
    private static readonly string[] DaggerSkills =
    [
        "Poison Dagger", "Tiger Strike", "Dragon Talon", "Fists of Fire", "Cobra Strike", "Claws of Thunder",
        "Dragon Tail", "Blades of Ice", "Dragon Flight", "Royal Strike", "Blade Shield", "Blade Fury", "Blade Sentinel",
    ];

    /// <summary>
    /// The skills this base's staffmods are drawn from where it is not simply its class's: a dagger's
    /// (<see cref="DaggerSkills"/>). Null for every other base - its class's skills on the trees.
    /// </summary>
    public static IReadOnlySet<int>? SkillsFor(Item item) =>
        Rolls(item) && ItemTypes.LineageOfBase(item.Code).Contains("knif", StringComparer.OrdinalIgnoreCase)
            ? DaggerSkills.Select(name => SkillNames.IdOf(name)).OfType<int>().ToHashSet()
            : null;

    /// <summary>Whether this base's staffmods can be <paramref name="skill"/>: its list where it has one, else its class's.</summary>
    public static bool Allows(Item item, SkillOption skill) =>
        SkillsFor(item) is { } listed
            ? listed.Contains(skill.Id)
            : ClassOf(item) is { } owner && skill.CharClass.Equals(owner, StringComparison.OrdinalIgnoreCase);

    /// <summary>One staff mod: the skill, and the levels it adds.</summary>
    public sealed record Mod(int Skill, int Levels);

    /// <summary>
    /// The skills this base's staff mods are drawn from: its class's, on the class's trees - each with the item
    /// level it needs (<see cref="LowestItemLevelFor"/>), which the picker says. Empty where the base rolls none.
    /// </summary>
    public static IReadOnlyList<SkillOption> Offered(Item item) =>
        Rolls(item) && SkillNames.Current is { } names
            ? [.. names.All.Where(skill => skill.OnTree && Allows(item, skill))
                .OrderBy(skill => skill.RequiredLevel).ThenBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)]
            : [];

    /// <summary>The staff mods the item carries: every <c>item_singleskill</c> of its own list, by skill.</summary>
    public static IReadOnlyList<Mod> On(Item item) =>
        [.. (item.StatLists.FirstOrDefault()?.Stats ?? [])
            .Where(stat => Stat.Equals(stat.Stat, StringComparison.OrdinalIgnoreCase))
            .Select(stat => new Mod(stat.SkillId ?? stat.Param ?? 0, stat.Value))
            .Where(mod => mod.Skill > 0)];

    /// <summary>
    /// A fresh set as PD2 rolls one (General Changes): none, one, two or three at 10/40/30/20, each +1, +2 or +3
    /// at 40/35/25, each a different skill the item's level reaches. Fewer where the class has fewer to give.
    /// </summary>
    public static IReadOnlyList<Mod> Roll(Item item, Random random)
    {
        var pool = Offered(item).Where(skill => LowestItemLevelFor(skill) <= item.ItemLevel).ToList();
        int count = random.Next(100) switch { < 10 => 0, < 50 => 1, < 80 => 2, _ => 3 };

        var mods = new List<Mod>();
        for (int n = 0; n < count && pool.Count > 0; n++)
        {
            var skill = pool[random.Next(pool.Count)];
            pool.Remove(skill);
            mods.Add(new Mod(skill.Id, random.Next(100) switch { < 40 => 1, < 75 => 2, _ => 3 }));
        }
        return mods;
    }

    /// <summary>
    /// The item's staff mods made <paramref name="mods"/>: every <c>item_singleskill</c> of its own list taken
    /// off, these written in their place (the <c>skill</c> property, as the game writes one).
    /// <para>
    /// Onto the base's own list and no other: a staff mod is the base's roll. A runeword's line of the same skill -
    /// Dominion's Golem Mastery on a Wand - is a line of its own on the runeword's list; written as a corruption is,
    /// merged into the first line of its skill on any list, the staff mod never reached the base and wrote its levels
    /// over the runeword's.
    /// </para>
    /// </summary>
    public static void Put(Item item, IReadOnlyList<Mod> mods)
    {
        var own = ItemStats.Home(item);
        own.Stats.RemoveAll(stat => Stat.Equals(stat.Stat, StringComparison.OrdinalIgnoreCase));
        // A line each: every one taken off above, and each mod a skill of its own.
        foreach (var wrote in mods.SelectMany(mod => AffixRanges.Written("skill", mod.Levels, mod.Levels, mod.Levels, mod.Skill)))
            own.Stats.Add(ItemStats.LineFrom(wrote));
        ItemStats.InIdOrder(item);
    }

    private static Dictionary<string, string> _typeOf = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Type to the class whose skills it rolls. Its keys are the rolling types.</summary>
    private static Dictionary<string, string> _classOfType = new(StringComparer.OrdinalIgnoreCase);

    public static void Load(GameData game)
    {
        var typeOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string table in new[] { "weapons", "armor", "misc" })
            ReadBases(game, table, typeOf);

        _typeOf = typeOf;
        _classOfType = ReadRollingTypes(game);
    }

    /// <summary>
    /// The class whose skills this base rolls, or null where it rolls none. The class code
    /// as the tables spell it - <c>ass</c>, <c>sor</c> - to compare against a skill's own.
    /// </summary>
    public static string? ClassOf(Item item)
    {
        if (!Rolls(item) || item.Code?.Trim() is not { Length: > 0 } code)
            return null;

        return _typeOf.TryGetValue(code, out string? type)
               && _classOfType.TryGetValue(type, out string? owner)
            ? owner
            : null;
    }

    /// <summary>
    /// The lowest item level this skill could have rolled at. PD2 gates a staff mod at the
    /// skill's required level plus seven, except a skill needing level 1, which rolls from
    /// item level 1.
    /// </summary>
    public static int LowestItemLevelFor(SkillOption skill) =>
        skill.RequiredLevel <= 1 ? 1 : skill.RequiredLevel + 7;

    /// <summary>
    /// True when this item is one the game would have rolled a skill onto.
    /// <para>
    /// Every quality but set and unique. In vanilla a staff mod needs a magic item; <b>PD2
    /// puts one on white class bases too</b> - a plain Katar carries its <c>+3 to Tiger
    /// Strike</c> the same as a magic one does. Gating on magic and above, as vanilla would
    /// have it, left the commonest case with no range at all.
    /// </para>
    /// <para>
    /// Set and unique are excluded because they are the two that dictate exact values from
    /// a table of their own, at whatever level it says. Claiming 1-3 there would mark a
    /// real item impossible - the false positive that teaches people to ignore the mark.
    /// </para>
    /// </summary>
    public static bool Rolls(Item item) =>
        item.Quality is not (ItemQuality.Set or ItemQuality.Unique)
        && item.Code?.Trim() is { Length: > 0 } code
        && _typeOf.TryGetValue(code, out string? type)
        && _classOfType.ContainsKey(type);

    private static void ReadBases(GameData game, string table, Dictionary<string, string> typeOf)
    {
        try
        {
            var lines = Rows(game, table);
            if (lines.Count == 0)
                return;

            string[] header = lines[0];
            int code = Array.IndexOf(header, "code");
            int type = Array.IndexOf(header, "type");
            if (code < 0 || type < 0)
                return;

            foreach (string[] cells in lines.Skip(1))
            {
                if (cells.Length <= Math.Max(code, type))
                    continue;

                string id = cells[code].Trim();
                if (id.Length > 0 && cells[type].Trim().Length > 0)
                    typeOf[id] = cells[type].Trim();
            }
        }
        catch (Exception)
        {
            // No bases mapped means no staff mods claimed, which says nothing rather than
            // something wrong.
        }
    }

    /// <summary>
    /// Types that roll a skill, following <c>Equiv1</c>/<c>Equiv2</c> as the other flags do,
    /// so a sub-type of a wand is covered without carrying the column itself.
    /// </summary>
    private static Dictionary<string, string> ReadRollingTypes(GameData game)
    {
        var rolling = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var lines = Rows(game, "itemtypes");
            if (lines.Count == 0)
                return rolling;

            string[] header = lines[0];
            int code = Array.IndexOf(header, "Code");
            int mods = Array.IndexOf(header, "StaffMods");
            int first = Array.IndexOf(header, "Equiv1");
            int second = Array.IndexOf(header, "Equiv2");
            if (code < 0 || mods < 0)
                return rolling;

            var rows = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (string[] cells in lines.Skip(1))
            {
                if (cells.Length > code && cells[code].Trim().Length > 0)
                    rows[cells[code].Trim()] = cells;
            }

            string Cell(string[] cells, int column) =>
                column >= 0 && cells.Length > column ? cells[column].Trim() : "";

            string? Carries(string type, HashSet<string> walked)
            {
                if (!walked.Add(type) || !rows.TryGetValue(type, out string[]? cells))
                    return null;

                if (Cell(cells, mods) is { Length: > 0 } owner)
                    return owner;

                foreach (int parent in new[] { first, second })
                {
                    string above = Cell(cells, parent);
                    if (above.Length > 0 && Carries(above, walked) is { } inherited)
                        return inherited;
                }

                return null;
            }

            foreach (string type in rows.Keys)
            {
                if (Carries(type, []) is { } owner)
                    rolling[type] = owner;
            }
        }
        catch (Exception)
        {
            // As above.
        }

        return rolling;
    }

    private static IReadOnlyList<string[]> Rows(GameData game, string table) => game.Rows(table);
}
