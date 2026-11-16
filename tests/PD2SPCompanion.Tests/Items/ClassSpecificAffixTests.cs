using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// A class item takes only its own class's class-specific affixes (<c>magicprefix</c> <c>classspecific</c>; the wiki's
/// "Non-Class" group). A Paladin shield is a shield to <c>itype</c>, so "+1 to Amazon Skills" (shld) reached it by type
/// alone; none of 235 class skill lines on 300 class bases in the ladder corpus was another class's.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class ClassSpecificAffixTests(Game game)
{
    private static Item Rare(string code)
    {
        var item = AffixSweepTests.Fresh(code);
        item.Quality = ItemQuality.Rare;
        return item;
    }

    private static HashSet<string> ClassesOffered(Item item) =>
        [.. MagicAffixes.Available(item, prefix: true, 0).Select(option => option.ClassSpecific).Where(each => each.Length > 0)];

    [GameTheory]
    [InlineData("paf", "pal")] // Vortex Shield
    [InlineData("nef", "nec")] // Bloodlord Skull
    [InlineData("amf", "ama")] // Matriarchal Javelin - no Barbarian Fanatic
    public void A_class_item_is_offered_only_its_own_class_rows(string code, string own)
    {
        if (!game.Available)
            return;

        var item = Rare(code);
        Assert.Equal([own], ClassesOffered(item));

        Assert.All(MagicAffixes.ReachableBy(item), option => Assert.True(option.ClassSpecific is "" || option.ClassSpecific == own));

        var other = MagicAffixes.Available(Rare("uit"), prefix: true, 0).First(option => option.ClassSpecific is { Length: > 0 } cls && cls != own);
        Assert.False(MagicAffixes.Offers(item, prefix: true, 0, other.Id));
    }

    [GameFact]
    public void An_item_every_class_uses_is_offered_every_class_rows()
    {
        if (!game.Available)
            return;

        // Monarch: the +class skill rows on shields, each class's.
        Assert.True(ClassesOffered(Rare("uit")).Count >= 7);
    }
}
