using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Characters;

/// <summary>
/// Where an item may be worn, from the
/// tables: <c>itemtypes.txt</c>'s <c>BodyLoc1</c>/<c>BodyLoc2</c> (a type taking its parents' through
/// <c>Equiv1</c>/<c>Equiv2</c>), the class its own type names (<see cref="ItemTypes.ClassOfBase"/>),
/// <c>weapons.txt</c>'s <c>2handed</c> and <c>1or2handed</c>. Requirements are <see cref="Requirements"/>'; this is only whether it goes there.
/// <para>
/// A worn item keeps its slot's number in X, with Y and page 0 - all fifteen worn items of the live
/// saves, the character's and the mercenary's, read so. See <see cref="Place"/>.
/// </para>
/// </summary>
public static class Equipping
{
    private static Dictionary<string, (string First, string Second)> _types = new(StringComparer.OrdinalIgnoreCase);

    public static void Load(GameData game)
    {
        var types = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var (table, _) = game.ReadExcel("itemtypes");
            for (int row = 0; row < table.Rows.Count; row++)
            {
                string code = table.Value(row, "Code").Trim();
                if (code.Length > 0)
                {
                    types[code] = (table.Value(row, "BodyLoc1").Trim(), table.Value(row, "BodyLoc2").Trim());
                }
            }
        }
        catch (Exception)
        {
            // Without the table nothing is known to go anywhere, so nothing is put on.
        }

        _types = types;
    }

    /// <summary>The body location code a slot answers to, as the tables name it.</summary>
    private static string? BodyLocOf(ItemLocation at) => at switch
    {
        ItemLocation.Head => "head",
        ItemLocation.Neck => "neck",
        ItemLocation.Torso => "tors",
        ItemLocation.RightHand or ItemLocation.SwapRight => "rarm",
        ItemLocation.LeftHand or ItemLocation.SwapLeft => "larm",
        ItemLocation.RightFinger => "rrin",
        ItemLocation.LeftFinger => "lrin",
        ItemLocation.Waist => "belt",
        ItemLocation.Feet => "feet",
        ItemLocation.Gloves => "glov",
        _ => null,
    };

    /// <summary>The other hand of the same weapon set, or None for a slot that is not a hand.</summary>
    public static ItemLocation OtherHand(ItemLocation at) => at switch
    {
        ItemLocation.RightHand => ItemLocation.LeftHand,
        ItemLocation.LeftHand => ItemLocation.RightHand,
        ItemLocation.SwapRight => ItemLocation.SwapLeft,
        ItemLocation.SwapLeft => ItemLocation.SwapRight,
        _ => ItemLocation.None,
    };

    private static IReadOnlyCollection<string> Types(Item item) => ItemTypes.Ancestry(ItemTypes.TypeOf(item.Code));

    private static bool Is(Item item, string type) => Types(item).Contains(type);

    private static string WeaponColumn(Item item, string column) =>
        D2SLib.Core.TXT.ItemsTXT.GetByCode(item.Code?.Trim() ?? "")?.Get(column)?.Value?.Trim() ?? "";

    /// <summary>Whether this character holds it in both hands: two-handed, less a Barbarian's one-or-two-handed sword.</summary>
    public static bool TwoHanded(Item item, string? classCode) =>
        WeaponColumn(item, "2handed") == "1"
        && !(classCode == "bar" && WeaponColumn(item, "1or2handed") == "1");

    /// <summary>Whether <paramref name="quiver"/> is what <paramref name="weapon"/> fires, which may share its hands.</summary>
    private static bool Feeds(Item quiver, Item weapon) =>
        (Is(weapon, "bow") && Is(quiver, "bowq")) || (Is(weapon, "xbow") && Is(quiver, "xboq"));

    /// <summary>
    /// The other hand's slot, where what it holds comes off when <paramref name="item"/> goes on at
    /// <paramref name="at"/>: a weapon this wearer holds in both hands, beside anything its hands do not share
    /// (a bow keeps its quiver). The game takes both off for it: a two-hander over weapon plus shield takes both
    /// off. None otherwise. <paramref name="classCode"/> null: a mercenary.
    /// </summary>
    public static ItemLocation AlsoComesOff(Item item, ItemLocation at, string? classCode, Func<ItemLocation, Item?> wornAt) =>
        OtherHand(at) is var otherHand && otherHand != ItemLocation.None && wornAt(otherHand) is { } other
        && TwoHanded(item, classCode) && !Feeds(other, item)
            ? otherHand
            : ItemLocation.None;

    /// <summary>
    /// Why <paramref name="item"/> cannot be worn at <paramref name="at"/> by a character of class
    /// <paramref name="classCode"/>, or null. <paramref name="wornAt"/> says what else is worn - the
    /// item itself already left out of it, should it be moving between slots. Belt potions above a
    /// smaller belt's rows are <paramref name="beltRowsUsed"/>.
    /// </summary>
    public static string? CannotWear(Item item, ItemLocation at, string? classCode, Func<ItemLocation, Item?> wornAt, int beltRowsUsed)
    {
        if (BodyLocOf(at) is not { } place)
            return L("Not a slot.");

        var types = Types(item);
        bool fits = types.Any(type => _types.TryGetValue(type, out var row) && (row.First == place || row.Second == place));
        if (!fits)
            return L("Not for that slot.");

        // The item's own type's class, never one above it - throwing knives hang under an
        // Assassin type for its staff mods and are nobody's (see ItemTypes.ClassOfBase).
        if (ItemTypes.ClassOfBase(item.Code) is { Length: > 0 } only
            && !string.Equals(only, classCode, StringComparison.OrdinalIgnoreCase))
        {
            return L($"Require {(CharacterClasses.NameOfCode(only) ?? only)}.");
        }

        // A two-handed weapon beside what its hands do not share takes that off with it (AlsoComesOff): nothing
        // to refuse with "Require empty other hand.", as the game takes both off.
        if (OtherHand(at) is var otherHand && otherHand != ItemLocation.None && wornAt(otherHand) is { } other
            && AlsoComesOff(item, at, classCode, wornAt) == ItemLocation.None)
        {
            if (TwoHanded(other, classCode) && !Feeds(item, other))
                return L("Not beside a two-handed weapon.");

            // Two weapons: a Barbarian's any pair, an Assassin's two claws; nobody else's.
            bool weapon = Is(item, "weap") && !Is(item, "misl"), otherWeapon = Is(other, "weap") && !Is(other, "misl");
            if (weapon && otherWeapon
                && !(classCode == "bar" || (classCode == "ass" && Is(item, "h2h") && Is(other, "h2h"))))
            {
                return L("Require Barbarian or Assassin for two weapons.");
            }
        }

        if (at == ItemLocation.Waist && beltRowsUsed > Belts.RowsFor(item.Code))
            return L("Require potions out of rows this belt lacks.");

        return null;
    }

    /// <summary>
    /// What Project Diablo 2 lets each kind of mercenary take beyond <c>hireling.txt</c>'s two weapon types -
    /// which are the game's of 2001: a bow; a polearm or a spear; a sword and a shield; a sword; a staff. The
    /// rest is in PD2's own code, with no table to read, so it is stated here from PD2's wiki
    /// (wiki.projectdiablo2.com/wiki/Mercenaries, "Equipment"), by the act a kind
    /// is hired in:
    /// <list type="bullet">
    /// <item>Act 1, Rogue Scouts: bows (Amazon bows too), crossbows, and quivers.</item>
    /// <item>Act 2, Desert Guards: polearms, spears, and javelins.</item>
    /// <item>Act 3, Iron Wolves: shields, Paladin shields, one-handed swords, wands, scepters, tipped maces
    /// (not clubs, not hammers), and Sorceress orbs.</item>
    /// <item>Act 4, Ascendants: staves.</item>
    /// <item>Act 5, Barbarians: swords, axes, tipped maces and hammers (every mace but a club), and Barbarian
    /// helms - never two weapons.</item>
    /// </list>
    /// A class's own item is a mercenary's only where the list names it (<c>Own</c>): the editor refused every
    /// one of them, and a Barbarian could not put on the helm the game gives him.
    /// Seen in game: the Barbarian's helm. The rest is the wiki's word.
    /// </summary>
    private static readonly Dictionary<int, (string[] Weapons, string[] OtherHand, string[] Own, bool OneHandedSwords)> MercenaryGear = new()
    {
        [1] = (["bow", "xbow"], ["bowq", "xboq"], ["abow"], false),
        [2] = (["pole", "spea", "jave"], [], [], false),
        [3] = (["swor", "wand", "scep", "mace", "orb"], ["shld"], ["ashd", "orb"], true),
        [4] = (["staf"], [], [], false),
        [5] = (["swor", "axe", "mace", "hamm"], [], ["phlm"], false),
    };

    /// <summary>
    /// Why a mercenary cannot wear <paramref name="item"/> at <paramref name="at"/>, or null. Its hands take
    /// its own weapon types - <c>hireling.txt</c>'s WType1/WType2 (<paramref name="weapons"/>) and what PD2
    /// adds for the act it is hired in (<see cref="MercenaryGear"/>) - a weapon of one of them in the weapon
    /// hand; a shield or a quiver in the other where its kind takes one. Head, body, gloves, belt and boots go
    /// by the body location, as the character's. A class's own item only where its kind takes that one.
    /// </summary>
    public static string? CannotWearMercenary(Item item, ItemLocation at, int act, IReadOnlyList<string> weapons, Func<ItemLocation, Item?> wornAt)
    {
        var types = Types(item);
        var gear = MercenaryGear.GetValueOrDefault(act, ([], [], [], false));

        if (ItemTypes.ClassOfBase(item.Code) is { Length: > 0 } && !gear.Own.Any(types.Contains))
            return L("Not for a mercenary.");

        bool shield = types.Contains("shld"), quiver = gear.OtherHand.Any(type => type != "shld" && types.Contains(type));

        if (at == ItemLocation.RightHand)
        {
            if (shield || quiver || !weapons.Concat(gear.Weapons).Any(weapon => weapon != "shie" && types.Contains(weapon)))
                return L("Not this mercenary's weapon.");

            // An Iron Wolf's sword is a one-handed one.
            if (gear.OneHandedSwords && types.Contains("swor") && TwoHanded(item, null))
                return L("Not this mercenary's weapon.");

            // A two-handed weapon takes the shield off with it (AlsoComesOff), as the character's does.
            return null;
        }

        if (at == ItemLocation.LeftHand)
        {
            // A quiver beside what it feeds, or beside nothing yet.
            if (quiver)
                return wornAt(ItemLocation.RightHand) is { } fed && !Feeds(item, fed) ? L("Not for this mercenary's other hand.") : null;

            if (!shield || !(weapons.Contains("shie") || gear.OtherHand.Contains("shld")))
                return L("Not for this mercenary's other hand.");

            if (wornAt(ItemLocation.RightHand) is { } weapon && TwoHanded(weapon, null))
                return L("Not beside a two-handed weapon.");

            return null;
        }

        if (BodyLocOf(at) is not { } place || at is ItemLocation.RightFinger or ItemLocation.LeftFinger or ItemLocation.Neck
            or ItemLocation.SwapRight or ItemLocation.SwapLeft)
        {
            return L("Not a mercenary's slot.");
        }

        return types.Any(type => _types.TryGetValue(type, out var row) && (row.First == place || row.Second == place))
            ? null
            : L("Not for that slot.");
    }

    /// <summary>Puts an item on at a slot, as the game writes a worn one: X the slot's number, Y and page 0.</summary>
    public static void Place(Item item, ItemLocation at)
    {
        item.Mode = ItemMode.Equipped;
        item.Location = at;
        item.X = (byte)at;
        item.Y = 0;
        item.Page = 0;
    }
}
