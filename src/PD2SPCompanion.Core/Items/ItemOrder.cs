using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// Where an item falls in the vault's Rarity and Type orders. Worked out once, when the item is
/// stored or the vault's per-item data is rebuilt, and kept beside it so the database can sort by
/// it - see <see cref="Vault.ProjectionVersion"/>.
/// </summary>
public static class ItemOrder
{
    /// <summary>The Rarity order, first to last. An item's rank is its place in this list.</summary>
    public static readonly IReadOnlyList<string> Rarities =
        [N("Unique"), N("Set"), N("Runeword"), N("Crafted"), N("Rare"), N("Magic"), N("Superior"), N("Normal"), N("Low quality"), N("Simple")];

    /// <summary>
    /// An item's place in <see cref="Rarities"/>. A runeword is ranked as one whatever its base's
    /// quality says - it is a normal or superior item underneath. "Simple" is what has no quality
    /// to rank: runes, gems, potions, the materials tab's stacks - and the cube, keys and tomes,
    /// which PD2 keeps as full items with an id and a Normal quality, but whose type could never
    /// be anything else. A white map or base could, so it stays Normal.
    /// </summary>
    public static int Rarity(Item item)
    {
        if (item.IsCompact || IsMaterialStack(item.Code))
            return 9;

        if (item.IsRuneword)
            return 2;

        if (item.Quality == ItemQuality.Normal && !ItemTypes.CanRollAffixes(TypeOfItem(item.Code)))
            return 9;

        return item.Quality switch
        {
            ItemQuality.Unique => 0,
            ItemQuality.Set => 1,
            ItemQuality.Craft or ItemQuality.Tempered => 3,
            ItemQuality.Rare => 4,
            ItemQuality.Magic => 5,
            ItemQuality.Superior => 6,
            ItemQuality.Normal => 7,
            ItemQuality.Inferior => 8,
            _ => 9,
        };
    }

    /// <summary>
    /// The Type order's groups, first to last, and the <c>itemtypes.txt</c> types that put an
    /// item in each - an item is in a group when one of them is among its ancestors. The raw row
    /// order of <c>itemtypes.txt</c> means nothing, so the order is this list's.
    /// <para>
    /// The ancestry already places the odd ones: circlets, druid pelts and barbarian helms are
    /// helms; necromancer heads and paladin shields are shields; orbs and amazon weapons are
    /// weapons. Maps are the exception - PD2's T4 and T5 maps and the uber map name no parent,
    /// where T1 to T3 descend from <c>map</c>, so they are listed by name.
    /// </para>
    /// </summary>
    private static readonly (string Group, string[] Types)[] Families =
    [
        (N("Helms"), ["helm"]),
        (N("Chests"), ["tors"]),
        (N("Shields"), ["shld"]),
        (N("Gloves"), ["glov"]),
        (N("Boots"), ["boot"]),
        (N("Belts"), ["belt"]),
        (N("Amulets"), ["amul"]),
        (N("Rings"), ["ring"]),
        (N("Weapons"), ["weap"]),
        (N("Quivers"), ["misl"]),
        (N("Charms"), ["char"]),
        (N("Jewels"), ["jewl"]),
        (N("Runes"), ["rune"]),
        (N("Gems"), ["gem"]),
        (N("Maps"), ["map", "t4m", "t5m", "ubmp"]),
    ];

    public static readonly IReadOnlyList<string> Groups = [.. Families.Select(f => f.Group), N("Other")];

    /// <summary>
    /// Weapons by kind, in alphabetical order of the kind. A weapon's kind is the nearest of its
    /// ancestors named here, so a throwing axe sits with the axes, an amazon bow with the bows and
    /// a hand-to-hand weapon of either kind with the claws. Crystal swords descend from
    /// "one-handed" rather than from <c>swor</c>, so they are named beside it.
    /// </summary>
    private static readonly (string Kind, string[] Types)[] Kinds =
    [
        (N("Axes"), ["axe"]),
        (N("Bows"), ["bow"]),
        (N("Claws"), ["h2h"]),
        (N("Crossbows"), ["xbow"]),
        (N("Daggers"), ["knif"]),
        (N("Hammers"), ["hamm"]),
        (N("Javelins"), ["jave"]),
        (N("Maces"), ["mace", "club"]),
        (N("Orbs"), ["orb"]),
        (N("Polearms"), ["pole"]),
        (N("Scepters"), ["scep"]),
        (N("Spears"), ["spea"]),
        (N("Staves"), ["staf"]),
        (N("Swords"), ["swor", "crys"]),
        (N("Wands"), ["wand"]),
    ];

    /// <summary>
    /// An item's place in the Type order: its group's index in <see cref="Groups"/> times a
    /// hundred, plus, for a weapon, its kind's index - so one number sorts both. A weapon of no
    /// kind named here comes after every kind.
    /// </summary>
    public static int TypeRank(Item item) => TypeRankOf(item.Code);

    /// <summary>The same rank from a base code alone - what a picker of bases is sorted into.</summary>
    public static int TypeRankOf(string? code)
    {
        string? type = TypeOfItem(code);
        var ancestry = ItemTypes.Ancestry(type);

        int group = Array.FindIndex(Families, f => f.Types.Any(ancestry.Contains));
        if (group < 0)
            return (Groups.Count - 1) * 100;

        if (Families[group].Group != "Weapons")
            return group * 100;

        int kind = Kinds.Length;

        foreach (string step in ItemTypes.Lineage(type))
        {
            int found = Array.FindIndex(Kinds, k => k.Types.Contains(step));
            if (found >= 0)
            {
                kind = found;
                break;
            }
        }

        return group * 100 + kind;
    }

    /// <summary>
    /// The ranks a type filter can name, first to last, each with the span of ranks it takes in:
    /// a group whole ("Weapons" is every kind), and under Weapons each kind on its own.
    /// </summary>
    public static IReadOnlyList<(string Label, int From, int To)> TypeSpans()
    {
        var spans = new List<(string, int, int)>();

        for (int group = 0; group < Groups.Count; group++)
        {
            spans.Add((Groups[group], group * 100, group * 100 + 99));

            if (Groups[group] != "Weapons")
                continue;

            for (int kind = 0; kind < Kinds.Length; kind++)
                spans.Add(($"Weapons: {Kinds[kind].Kind}", group * 100 + kind, group * 100 + kind));
        }

        return spans;
    }

    /// <summary>
    /// Whether a base is a map: one of the Maps group - the three tiers, the dungeons, the unique ones.
    /// What the `map_` properties are for and nothing else is (<see cref="StatCatalogue.IsMapOnly"/>).
    /// </summary>
    public static bool IsMap(string? code) => TypeName(TypeRankOf(code)) == "Maps";

    /// <summary>A type rank in words: "Helms", or "Weapons: Swords".</summary>
    public static string TypeName(int rank)
    {
        int group = Math.Clamp(rank / 100, 0, Groups.Count - 1);
        if (Groups[group] != "Weapons")
            return Groups[group];

        int kind = rank % 100;
        return $"Weapons: {(kind < Kinds.Length ? Kinds[kind].Kind : "Other")}";
    }

    /// <summary>
    /// The type an item is ranked by. A materials-tab stack is the single item's code plus "s"
    /// and has a type of its own, "Stack Rune" and the like, which nothing else descends from -
    /// so it is ranked as the item it is a stack of.
    /// </summary>
    private static string? TypeOfItem(string? code)
    {
        code = code?.Trim();
        if (code is not { Length: > 1 })
            return null;

        if (IsMaterialStack(code) && ItemTypes.TypeOf(code[..^1]) is { } single)
            return single;

        return ItemTypes.TypeOf(code);
    }

    private static bool IsMaterialStack(string? code) =>
        code?.Trim() is { Length: > 1 } id
        && id.EndsWith('s')
        && Materials.Codes.Contains(id, StringComparer.OrdinalIgnoreCase);
}
