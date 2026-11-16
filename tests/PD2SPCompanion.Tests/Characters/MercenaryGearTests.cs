using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Characters;

/// <summary>
/// What each kind of mercenary can wear (a Barbarian mercenary could not equip
/// a barbarian helmet here and can in game; checked against PD2's wiki page): the game's old
/// table names two weapon types a kind, PD2's own code adds the rest, and a class's own item is a mercenary's
/// only where its kind takes that one. By the act a kind is hired in.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class MercenaryGearTests(Game game)
{
    private const int Rogue = 1, Guard = 2, Wolf = 3, Ascendant = 4, Barbarian = 5;

    /// <summary>hireling.txt's own two, by act - what the editor went by alone.</summary>
    private static readonly Dictionary<int, string[]> Table = new()
    {
        [Rogue] = ["bow"], [Guard] = ["pole", "spea"], [Wolf] = ["swor", "shie"], [Ascendant] = ["staf"], [Barbarian] = ["swor"],
    };

    private static Item Of(string code) => ItemForge.PlainBase(code, 0, "103").Item;

    private static string? Refused(int act, string code, ItemLocation at, Item? inWeaponHand = null) =>
        Equipping.CannotWearMercenary(Of(code), at, act, Table[act], where => where == ItemLocation.RightHand ? inWeaponHand : null);

    private static void Takes(int act, ItemLocation at, params string[] codes)
    {
        foreach (string code in codes)
            Assert.True(Refused(act, code, at) is null, $"act {act} refused {code} ({ItemPresenter.BaseName(code)}) at {at}: {Refused(act, code, at)}");
    }

    private static void Refuses(int act, ItemLocation at, params string[] codes)
    {
        foreach (string code in codes)
            Assert.True(Refused(act, code, at) is not null, $"act {act} took {code} ({ItemPresenter.BaseName(code)}) at {at}");
    }

    [GameFact]
    public void A_Barbarian_wears_a_Barbarian_helm_and_no_other_kind_does()
    {
        if (!game.Available)
            return;

        // Jawbone Cap: the helm that was refused.
        Takes(Barbarian, ItemLocation.Head, "ba1", "cap");
        foreach (int other in new[] { Rogue, Guard, Wolf, Ascendant })
        {
            Refuses(other, ItemLocation.Head, "ba1");
            Takes(other, ItemLocation.Head, "cap");
        }

        // A Druid's pelt is nobody's.
        foreach (int act in new[] { Rogue, Guard, Wolf, Ascendant, Barbarian })
            Refuses(act, ItemLocation.Head, "dr1");
    }

    [GameFact]
    public void Each_kind_takes_its_own_weapons_as_PD2_has_them()
    {
        if (!game.Available)
            return;

        // Rogue Scout: bows, an Amazon's too, and crossbows.
        Takes(Rogue, ItemLocation.RightHand, "sbw", "am1", "lxb");
        Refuses(Rogue, ItemLocation.RightHand, "ssd", "spr", "jav", "sst", "aqv");

        // Desert Guard: polearms, spears, javelins - not an Amazon's own.
        Takes(Guard, ItemLocation.RightHand, "bar", "spr", "jav");
        Refuses(Guard, ItemLocation.RightHand, "am3", "am5", "ssd", "sbw", "sst");

        // Iron Wolf: one-handed swords, wands, scepters, tipped maces, a Sorceress's orb.
        Takes(Wolf, ItemLocation.RightHand, "ssd", "wnd", "scp", "mac", "fla", "ob1");
        Refuses(Wolf, ItemLocation.RightHand, "2hs", "clb", "whm", "hax", "sst", "buc");

        // Ascendant: staves.
        Takes(Ascendant, ItemLocation.RightHand, "sst");
        Refuses(Ascendant, ItemLocation.RightHand, "ssd", "wnd", "ob1");

        // Barbarian: swords of either hand, axes, every mace but a club.
        Takes(Barbarian, ItemLocation.RightHand, "ssd", "2hs", "hax", "mac", "fla", "whm");
        Refuses(Barbarian, ItemLocation.RightHand, "clb", "spr", "sbw", "sst", "wnd");
    }

    [GameFact]
    public void The_other_hand_is_a_shield_for_an_Iron_Wolf_a_quiver_for_a_Rogue_and_nothing_for_the_rest()
    {
        if (!game.Available)
            return;

        // A shield, a Paladin's too - never a Necromancer's head.
        Takes(Wolf, ItemLocation.LeftHand, "buc", "pa1");
        Refuses(Wolf, ItemLocation.LeftHand, "ne1", "ssd");

        // Arrows and bolts; and each only beside what it feeds.
        Takes(Rogue, ItemLocation.LeftHand, "aqv", "cqv");
        Refuses(Rogue, ItemLocation.LeftHand, "buc");
        Assert.Null(Refused(Rogue, "aqv", ItemLocation.LeftHand, Of("sbw")));
        Assert.NotNull(Refused(Rogue, "cqv", ItemLocation.LeftHand, Of("sbw")));
        Assert.Null(Refused(Rogue, "cqv", ItemLocation.LeftHand, Of("lxb")));

        // A Barbarian never holds two weapons, nor a shield.
        Refuses(Barbarian, ItemLocation.LeftHand, "ssd", "hax", "buc");
        Refuses(Guard, ItemLocation.LeftHand, "buc", "jav");
        Refuses(Ascendant, ItemLocation.LeftHand, "buc");
    }

    [GameFact]
    public void Every_kind_wears_plain_armor_and_none_a_ring()
    {
        if (!game.Available)
            return;

        foreach (int act in new[] { Rogue, Guard, Wolf, Ascendant, Barbarian })
        {
            Takes(act, ItemLocation.Torso, "qui");
            Takes(act, ItemLocation.Gloves, "lgl");
            Takes(act, ItemLocation.Feet, "lbt");
            Takes(act, ItemLocation.Waist, "lbl");
            Refuses(act, ItemLocation.RightFinger, "rin");
            Refuses(act, ItemLocation.Neck, "amu");
        }
    }
}
