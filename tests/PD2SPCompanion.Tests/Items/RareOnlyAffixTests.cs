using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// Season 14's <c>rare</c> 3 (the table sweep): "a dropped rare only affix", the patch notes say of Visceral
/// and Celestial - the two rows turned from 1 to 3 - and 40 prefixes and 6 suffixes carry it. A rare rolls it, a magic
/// item and a craft do not - the wiki's Rarity column gives all 46 "R", where every 1 is "M, R, C"; read
/// as 1 alone, it was offered on magic items and on no rare. Blank stays magic only, 1 all three.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class RareOnlyAffixTests(Game game)
{
    private static Item Weapon(ItemQuality quality)
    {
        var item = AffixSweepTests.Fresh("7wa");
        item.Quality = quality;
        return item;
    }

    private static bool Offered(Item item, string name) =>
        MagicAffixes.Available(item, prefix: true, 0).Any(option => option.Name == name);

    [GameFact]
    public void A_dropped_rare_only_affix_is_offered_on_rares_and_never_on_a_craft_or_a_magic_item()
    {
        if (!game.Available || Game.Tables is not null || D2SLib.Model.TXT.StatEncoding.PreviousSeason != "S13")
            return;

        Assert.True(Offered(Weapon(ItemQuality.Rare), "Visceral"));
        Assert.False(Offered(Weapon(ItemQuality.Craft), "Visceral"));
        Assert.False(Offered(Weapon(ItemQuality.Magic), "Visceral"));

        // 1: all three; blank: a magic item's alone.
        Assert.True(Offered(Weapon(ItemQuality.Rare), "Cruel"));
        Assert.True(Offered(Weapon(ItemQuality.Craft), "Cruel"));
        Assert.True(Offered(Weapon(ItemQuality.Magic), "Cruel"));
        var magicOnly = MagicAffixes.Available(Weapon(ItemQuality.Magic), prefix: true, 0).First(option => !option.ForRare);
        Assert.False(Offered(Weapon(ItemQuality.Rare), magicOnly.Name)
                     && MagicAffixes.Available(Weapon(ItemQuality.Rare), prefix: true, 0).Any(option => option.Id == magicOnly.Id));
    }
}
