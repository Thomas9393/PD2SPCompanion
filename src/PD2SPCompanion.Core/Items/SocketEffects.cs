using System.Text;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// What a gem or rune gives the item it is socketed into.
/// <para>
/// A jewel carries its own properties and can simply be read. <b>A gem or a rune carries
/// none</b> - nothing is stored on it at all - because what it grants depends on what it is
/// put in: a Ral Rune is fire damage in a weapon, fire resistance in a helm, and more fire
/// resistance in a shield. <c>gems.txt</c> holds the three sets, and the host decides which
/// one is read.
/// </para>
/// </summary>
public static class SocketEffects
{
    /// <summary>One property a gem gives: its code, its parameter - a poison's length - and its two ends.</summary>
    private sealed record Mod(string Code, int Param, int Min, int Max);

    private sealed record Sets(Mod[] Weapon, Mod[] Helm, Mod[] Shield);

    private static Dictionary<string, Sets> _gems = new(StringComparer.OrdinalIgnoreCase);

    public static void Load(GameData game)
    {
        var found = new Dictionary<string, Sets>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (game.Lines("gems") is not { } lines)
                return;
            if (lines.Length == 0)
                return;

            string[] header = lines[0].Split('\t');
            int code = Array.IndexOf(header, "code");
            if (code < 0)
                return;

            foreach (string line in lines.Skip(1))
            {
                string[] cells = line.Split('\t');
                if (cells.Length <= code || cells[code].Trim().Length == 0)
                    continue;

                found[cells[code].Trim()] = new Sets(
                    Read(header, cells, "weapon"),
                    Read(header, cells, "helm"),
                    Read(header, cells, "shield"));
            }
        }
        catch (Exception)
        {
            // Without the table a gem says nothing rather than something wrong.
        }

        _gems = found;
    }

    /// <summary>
    /// What this gem gives, worded the way the game words it, for the item it is in.
    /// <para>
    /// Empty for a jewel, whose properties are on the jewel itself and read from there -
    /// and for anything the table does not carry.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> On(Item gem, Item? host)
    {
        if (Row(gem.Code) is not { } sets)
            return [];

        var mods = Wearing(host) switch
        {
            Kind.Shield => sets.Shield,
            Kind.Armor => sets.Helm,
            _ => sets.Weapon,
        };

        return Worded(mods);
    }

    /// <summary>
    /// What a gem or rune gives this host, as the stats an item would hold for it - for counting them
    /// into the host's own (the tooltip, the requirements), where <see cref="On"/> gives the words.
    /// </summary>
    public static IReadOnlyList<ItemStat> StatsIn(string gemCode, Item host) =>
        Row(gemCode) is not { } sets
            ? []
            : Stats(Wearing(host) switch
                {
                    Kind.Shield => sets.Shield,
                    Kind.Armor => sets.Helm,
                    _ => sets.Weapon,
                });

    /// <summary>
    /// A set of the table's properties as stats. A gem's are fixed rather than rolled, so the two ends
    /// agree and either will do; the lower is taken for the odd row that disagrees. With the parameter:
    /// it is a poison's length, and without it a Tal's poison was a rate that never ticked.
    /// </summary>
    private static IReadOnlyList<ItemStat> Stats(Mod[] mods) =>
        [.. mods.SelectMany(mod => AffixRanges.Written(mod.Code, mod.Min, mod.Max, mod.Min, mod.Param))
                .Select(wrote => new ItemStat
                {
                    Stat = wrote.Stat, Value = wrote.Value, SkillId = wrote.SkillId,
                    SkillLevel = wrote.SkillLevel, MaxCharges = wrote.MaxCharges, Param = wrote.Param,
                })];

    /// <summary>
    /// Which of gems.txt's three columns a base reads - 0 a weapon, 1 armour, 2 a shield. Two
    /// bases with the same answer get the same from every gem and rune.
    /// </summary>
    public static int KindOf(string baseCode) =>
        (int)Wearing(new Item { Code = baseCode.PadRight(4, ' ') });

    /// <summary>Whether this base is something that goes in a socket at all.</summary>
    public static bool Known(string? code) => Row(code) is not null;

    /// <summary>
    /// What it gives in each of the three kinds of host, which is how the game's own
    /// tooltip puts it for a gem or rune lying loose.
    /// </summary>
    public static (IReadOnlyList<string> Weapon, IReadOnlyList<string> Armor,
                   IReadOnlyList<string> Shield) Everywhere(string? code) =>
        Row(code) is { } sets
            ? (Worded(sets.Weapon), Worded(sets.Helm), Worded(sets.Shield))
            : ([], [], []);

    /// <summary>
    /// The table's row for a base.
    /// <para>
    /// A rune <b>stack</b> has no row of its own - all 33 of them are the single rune's
    /// code with an <c>s</c> on the end, and the table carries only the singles - so a
    /// stack is answered by the rune it is a stack of.
    /// </para>
    /// </summary>
    private static Sets? Row(string? code)
    {
        if (code?.Trim() is not { Length: > 0 } id)
            return null;

        if (_gems.TryGetValue(id, out var sets))
            return sets;

        return id.Length > 1 && id.EndsWith('s')
               && _gems.TryGetValue(id[..^1], out var single)
            ? single
            : null;
    }

    private static IReadOnlyList<string> Worded(Mod[] mods)
    {
        // As an item holding them words them (ItemLines): a Ral's fire is "Adds 5-30 fire damage" and a
        // Tal's poison "+75 poison damage over 5 seconds". Worded stat by stat, they read
        // "+5 to Minimum Fire Damage", "+5 to Maximum Fire Damage" - the maximum by the minimum's number.
        var said = new List<string>(ItemLines.Worded(Stats(mods)));

        // A property the tables word no stat for is still named, rather than left out.
        said.AddRange(mods.Where(mod => AffixRanges.StatsOf(mod.Code).Count == 0).Select(mod => $"{mod.Code} {mod.Min}"));

        return said;
    }

    private enum Kind { Weapon, Armor, Shield }

    /// <summary>
    /// Which of the three sets applies: the base's <c>gemapplytype</c> (0 weapon, 1 armor, 2 shield), which tells a
    /// Paladin shield, a Voodoo head and a quiver as shields where their item types never reach <c>shie</c>. A table
    /// with no such column goes by the type: a shield is armour that has its own column, so it is asked about first.
    /// </summary>
    private static Kind Wearing(Item? host)
    {
        if (host is null)
            return Kind.Weapon;

        var basis = ItemBases.Of(host.Code);

        if (basis?.GemApply is { } column)
            return column switch { 2 => Kind.Shield, 1 => Kind.Armor, _ => Kind.Weapon };

        if (basis is { Type.Length: > 0 }
            && ItemTypes.Ancestry(basis.Type).Contains("shie", StringComparer.OrdinalIgnoreCase))
        {
            return Kind.Shield;
        }

        return basis is { IsArmor: true } ? Kind.Armor : Kind.Weapon;
    }

    private static Mod[] Read(string[] header, string[] cells, string which)
    {
        var mods = new List<Mod>();

        for (int n = 1; n <= 3; n++)
        {
            int code = Array.IndexOf(header, $"{which}Mod{n}Code");
            int param = Array.IndexOf(header, $"{which}Mod{n}Param");
            int low = Array.IndexOf(header, $"{which}Mod{n}Min");
            int high = Array.IndexOf(header, $"{which}Mod{n}Max");

            if (code < 0 || cells.Length <= code || cells[code].Trim().Length == 0)
                continue;

            mods.Add(new Mod(
                cells[code].Trim(),
                Number(cells, param),
                Number(cells, low),
                Number(cells, high)));
        }

        return [.. mods];
    }

    private static int Number(string[] cells, int column) =>
        column >= 0 && cells.Length > column && int.TryParse(cells[column].Trim(), out int value)
            ? value
            : 0;
}
