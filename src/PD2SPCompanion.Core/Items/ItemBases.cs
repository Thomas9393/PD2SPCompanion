using System.Text;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// What a base item is worth before anything is rolled onto it: its defence, its durability,
/// and which table it came from.
/// <para>
/// This is the soft half of the two limits an editable number has. The file says what may be
/// written; <c>armor.txt</c> and <c>weapons.txt</c> say what the game would have made. A
/// Light Plate is 93-99 defence and an Archon Plate 364-407 - the same stat, three hundred
/// apart - so the file's own limit says almost nothing about whether a value is plausible.
/// </para>
/// <para>
/// Shown as context rather than enforced. The real ceiling is the base multiplied by
/// ethereal and superior and enhanced defence and whatever a socketed jewel adds, and an
/// editor that clamped to the base alone would refuse items the game really produced.
/// </para>
/// </summary>
public static class ItemBases
{
    /// <param name="MinimumDamage">
    /// One-handed damage, or a throwing weapon's. Null where the base does not carry it -
    /// a two-handed weapon keeps its damage in its own pair of columns, and a piece of
    /// armour in none at all.
    /// </param>
    /// <param name="Speed">
    /// A weapon's base attack speed - weapons.txt's <c>speed</c>, the number players call WSM: lower is
    /// faster, 0 where the column is empty. Null for what is no weapon (armor.txt has a column of the same
    /// name that is a different thing).
    /// </param>
    /// <param name="WeaponClass">
    /// How a weapon is held, which is the animation it is swung with - weapons.txt's <c>wclass</c>: <c>1hs</c>
    /// one hand swinging, <c>1ht</c> one hand thrusting, <c>2ht</c>, <c>stf</c>, <c>bow</c>, <c>xbw</c>,
    /// <c>ht1</c> a claw. "" for what is no weapon. What a form's speed is counted on (CharacterSheet.WereformSpeed).
    /// </param>
    /// <param name="TwoHandedClass">
    /// How it is held in both hands - weapons.txt's <c>2handedwclass</c>: a two-handed sword's <c>2hs</c>, where its
    /// <see cref="WeaponClass"/> is the <c>1hs</c> a Barbarian swings it with in one. "" for what is no weapon.
    /// </param>
    /// <param name="OneOrTwoHanded">Whether a Barbarian may hold it in one hand though it is for two - weapons.txt's <c>1or2handed</c>.</param>
    /// <param name="Block">A shield's own chance to block - armor.txt's <c>block</c>; 0 for anything else.</param>
    public sealed record Basis(
        bool IsArmor, int? Durability, int? MinimumDefense, int? MaximumDefense,
        int GemSockets = 0, string Type = "", int QualityLevel = 0,
        int? MinimumDamage = null, int? MaximumDamage = null,
        int? TwoHandMinimum = null, int? TwoHandMaximum = null,
        int RequiredStrength = 0, int RequiredDexterity = 0, int MagicLevel = 0, int? Speed = null,
        string WeaponClass = "", int Block = 0, int? ThrowMinimum = null, int? ThrowMaximum = null,
        bool TwoHanded = false, string TwoHandedClass = "", bool OneOrTwoHanded = false, int? GemApply = null)
    {
        public bool HasDefense => MinimumDefense is not null && MaximumDefense is not null;

    }

    private static Dictionary<string, Basis> _bases = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Each type's socket ceiling at item level 1, 25 and 40.</summary>
    private static Dictionary<string, (int Low, int Middle, int High)> _byType =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The most sockets this item could have.
    /// <para>
    /// Two ceilings and the lower wins. The base has its own <c>gemsockets</c>, and the
    /// type has one that rises with item level - <c>MaxSock1</c> up to level 24,
    /// <c>MaxSock25</c> to 39, <c>MaxSock40</c> above. A sword base allowing six still
    /// takes only four at item level 30.
    /// </para>
    /// </summary>
    public static int MaximumSockets(Item item)
    {
        if (Of(item.Code) is not { } basis || basis.GemSockets <= 0)
            return 0;

        if (!_byType.TryGetValue(basis.Type, out var bands))
            return basis.GemSockets;

        int byLevel = item.ItemLevel >= 40 ? bands.High
            : item.ItemLevel >= 25 ? bands.Middle
            : bands.Low;

        return Math.Max(0, Math.Min(basis.GemSockets, byLevel));
    }

    private static Dictionary<string, int> _blockFactors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What a class adds to a shield's chance to block (charstats <c>BlockFactor</c>), by its code: 0 for an unknown one.</summary>
    public static int BlockFactorOf(string classCode) => _blockFactors.GetValueOrDefault(classCode);

    private static Dictionary<string, int> ReadBlockFactors(GameData game)
    {
        var found = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (game.Lines("charstats") is not { Length: > 0 } lines)
            return found;

        string[] header = lines[0].Split('\t');
        int name = Array.IndexOf(header, "class"), factor = Array.IndexOf(header, "BlockFactor");
        if (name < 0 || factor < 0)
            return found;

        foreach (string line in lines.Skip(1))
        {
            string[] cells = line.Split('\t');
            if (cells.Length <= Math.Max(name, factor) || !int.TryParse(cells[factor], out int value))
                continue;

            int at = CharacterClasses.Names.ToList().IndexOf(cells[name].Trim());
            if (at >= 0 && CharacterClasses.CodeOf(at) is { } code)
                found[code] = value;
        }

        return found;
    }

    public static void Load(GameData game)
    {
        var found = new Dictionary<string, Basis>(StringComparer.OrdinalIgnoreCase);

        Read(game, "armor", isArmor: true, found);
        Read(game, "weapons", isArmor: false, found);

        // misc too, and not only for completeness: PD2 quivers take sockets and live here,
        // as do charms and jewels. Reading only the two big tables left a socketed quiver
        // with no base at all, so the page could not say how many sockets it was allowed
        // and hid the section on an item that plainly had one.
        Read(game, "misc", isArmor: false, found);

        _bases = found;
        _blockFactors = ReadBlockFactors(game);
        _byType = ReadTypeSockets(game);
    }

    /// <summary>Every base the three tables carry - armour, weapons and misc - by code.</summary>
    public static IReadOnlyCollection<string> Codes => _bases.Keys;

    /// <summary>
    /// What the tables say about this base, or null for one they do not carry. The code off
    /// an item is padded to four characters, so it is trimmed here.
    /// </summary>
    public static Basis? Of(string? code) =>
        code?.Trim() is { Length: > 0 } id && _bases.TryGetValue(id, out var basis) ? basis : null;

    /// <summary>
    /// Whether a base is a quest item: the tables' own <c>quest</c> column, which the S13 tables
    /// set on exactly 25 bases - 17 in misc (the Bark Scrolls, Khalim's organs, the SoulStone,
    /// the Horadric Cube, Book of Skill...), 8 in weapons (Gidbinn, the Horadric Staff, Khalim's
    /// Flail...), none in armor. A second one can break a character's quest, which is why the
    /// vault will not hand one out.
    /// </summary>
    public static bool IsQuestItem(string? code) =>
        code?.Trim() is { Length: > 0 } id
        && D2SLib.Core.TXT.ItemsTXT.GetByCode(id)?.Get("quest")?.Value is { } quest
        && int.TryParse(quest, out int number) && number > 0;

    /// <summary>
    /// The defence an ethereal armour stores for a base roll: <b>a quarter more, floored</b> -
    /// the stored number carries the bonus. PD2's figure, where classic D2 gave half: a
    /// Haemosu's Adamant (a Cuirass, 170-176) stores 213, which is 171 x 1.25 and no roll x 1.5,
    /// and all five ethereal armours on the S13 market fit it - a Stone on an
    /// Archon Plate stores 913, (730 + 1) x 1.25. Items from older seasons stored x 1.5 (the
    /// item packs) and keep it; the figure is fixed
    /// when the item drops.
    /// </summary>
    public static int Ethereal(int roll) => roll * 5 / 4;

    /// <summary>The base roll behind an ethereal armour's stored defence - <see cref="Ethereal"/> undone.</summary>
    public static int BehindEthereal(int stored) => (stored * 4 + 4) / 5;

    /// <summary>
    /// Where a newly made item's current durability lands against its maximum <paramref name="most"/>: half
    /// of it to one under it. Measured on the cube's upgrade in game (BaseUpgrades.Upgrade, six rolls); the
    /// bounds are what those rolls show, not the game's code.
    /// </summary>
    public static (int Low, int High) DurabilityRoll(int most) =>
        most <= 1 ? (most, most) : (most / 2, most - 1);

    private static void Read(
        GameData game, string table, bool isArmor, Dictionary<string, Basis> found)
    {
        try
        {
            if (game.Lines(table) is not { } lines)
                return;
            if (lines.Length == 0)
                return;

            string[] header = lines[0].Split('\t');
            int code = Array.IndexOf(header, "code");
            int durability = Array.IndexOf(header, "durability");
            int lowest = Array.IndexOf(header, "minac");
            int highest = Array.IndexOf(header, "maxac");
            int sockets = Array.IndexOf(header, "gemsockets");
            int kind = Array.IndexOf(header, "type");
            int gemApply = Array.IndexOf(header, "gemapplytype");

            // The base's own level - qlvl - which is not the level needed to wear it. It is
            // half of what an item's affix level is worked out from.
            int quality = Array.IndexOf(header, "level");

            int least = Array.IndexOf(header, "mindam");
            int most = Array.IndexOf(header, "maxdam");
            int bothLeast = Array.IndexOf(header, "2handmindam");
            int bothMost = Array.IndexOf(header, "2handmaxdam");
            int strength = Array.IndexOf(header, "reqstr");
            int dexterity = Array.IndexOf(header, "reqdex");
            int speed = table == "weapons" ? Array.IndexOf(header, "speed") : -1;
            int held = table == "weapons" ? Array.IndexOf(header, "wclass") : -1;
            int block = table == "armor" ? Array.IndexOf(header, "block") : -1;
            int thrownLeast = table == "weapons" ? Array.IndexOf(header, "minmisdam") : -1;
            int thrownMost = table == "weapons" ? Array.IndexOf(header, "maxmisdam") : -1;
            int bothHands = table == "weapons" ? Array.IndexOf(header, "2handed") : -1;
            int heldInBoth = table == "weapons" ? Array.IndexOf(header, "2handedwclass") : -1;
            int eitherHands = table == "weapons" ? Array.IndexOf(header, "1or2handed") : -1;

            // The base's magic level, which a handful of bases carry: circlets 3 to 18 by
            // tier, and staves, wands and orbs 1. It raises the level this base rolls its
            // affixes at, and it is the reason a circlet carries affixes far above its own
            // level. See MagicAffixes.AffixLevel.
            int magic = Array.IndexOf(header, "magic lvl");
            if (magic < 0)
                magic = Array.IndexOf(header, "magiclvl");

            if (code < 0)
                return;

            foreach (string line in lines.Skip(1))
            {
                string[] cells = line.Split('\t');
                if (cells.Length <= code)
                    continue;

                string id = cells[code].Trim();
                if (id.Length == 0)
                    continue;

                found[id] = new Basis(
                    isArmor,
                    Number(cells, durability),
                    Number(cells, lowest),
                    Number(cells, highest),
                    Number(cells, sockets) ?? 0,
                    kind >= 0 && cells.Length > kind ? cells[kind].Trim() : "",
                    Number(cells, quality) ?? 0,
                    Number(cells, least),
                    Number(cells, most),
                    Number(cells, bothLeast),
                    Number(cells, bothMost),
                    Number(cells, strength) ?? 0,
                    Number(cells, dexterity) ?? 0,
                    Number(cells, magic) ?? 0,
                    table == "weapons" ? Number(cells, speed) ?? 0 : null,
                    held >= 0 && cells.Length > held ? cells[held].Trim() : "",
                    Number(cells, block) ?? 0,
                    Number(cells, thrownLeast),
                    Number(cells, thrownMost),
                    Number(cells, bothHands) == 1,
                    heldInBoth >= 0 && cells.Length > heldInBoth ? cells[heldInBoth].Trim() : "",
                    Number(cells, eitherHands) == 1, GemApply: Number(cells, gemApply));
            }
        }
        catch (Exception)
        {
            // No bases read means no context offered, which says nothing rather than
            // something wrong.
        }
    }

    private static Dictionary<string, (int, int, int)> ReadTypeSockets(GameData game)
    {
        var found = new Dictionary<string, (int, int, int)>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (game.Lines("itemtypes") is not { } lines)
                return found;
            if (lines.Length == 0)
                return found;

            string[] header = lines[0].Split('\t');
            int code = Array.IndexOf(header, "Code");
            int low = Array.IndexOf(header, "MaxSock1");
            int middle = Array.IndexOf(header, "MaxSock25");
            int high = Array.IndexOf(header, "MaxSock40");

            if (code < 0 || low < 0 || middle < 0 || high < 0)
                return found;

            foreach (string line in lines.Skip(1))
            {
                string[] cells = line.Split('\t');
                if (cells.Length <= high || cells[code].Trim().Length == 0)
                    continue;

                found[cells[code].Trim()] = (
                    Number(cells, low) ?? 0,
                    Number(cells, middle) ?? 0,
                    Number(cells, high) ?? 0);
            }
        }
        catch (Exception)
        {
            // Without the bands only the base ceiling applies, which is the looser of the
            // two and so refuses nothing the game allows.
        }

        return found;
    }

    private static int? Number(string[] cells, int column) =>
        column >= 0 && cells.Length > column && int.TryParse(cells[column].Trim(), out int value)
            ? value
            : null;
}
