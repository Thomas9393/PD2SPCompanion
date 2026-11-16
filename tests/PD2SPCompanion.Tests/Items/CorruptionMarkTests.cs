using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// Which lines take the asterisks, as in game: the red one on
/// "Socketed (n)" when the corruption bored the sockets. The gold one follows the desecration's own mod
/// (Desecrations.StatsOn), not the game's two-bit slip, and is tested with desecration.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class CorruptionMarkTests(Game game)
{
    private const string Stamp = "103";

    [GameFact]
    public void A_socket_corruption_marks_the_socket_line()
    {
        if (!game.Available)
            return;

        var blank = ItemForge.BaseAs("7cr", ItemQuality.Normal, Stamp, new Random(5)).Item;
        var sockets = Corruptions.Available(blank)
            .First(option => option.Grants.Any(grant => grant.Code.Equals("sock", StringComparison.OrdinalIgnoreCase)));
        // A white item can only gain sockets; a rare can gain a property.
        var other = Corruptions.Available(ItemForge.BaseAs("7cr", ItemQuality.Rare, Stamp, new Random(5)).Item)
            .First(option => option.Grants.Count > 0 && option.Grants.All(grant => !grant.Code.Equals("sock", StringComparison.OrdinalIgnoreCase)));

        var bored = ItemForge.BaseAs("7cr", ItemQuality.Normal, Stamp, new Random(5)).Item;
        Corruptions.Apply(bored, sockets, new Random(1));
        Assert.True(Corruptions.GaveSockets(bored));

        var plain = ItemForge.BaseAs("7cr", ItemQuality.Rare, Stamp, new Random(5)).Item;
        Corruptions.Apply(plain, other, new Random(1));
        Assert.False(Corruptions.GaveSockets(plain));
        Assert.NotEmpty(Corruptions.StatsOn(plain));
    }

    [GameFact]
    public void The_gold_mark_follows_the_desecrations_own_mod()
    {
        if (!game.Available)
            return;

        var amulet = ItemForge.BaseAs("amu", ItemQuality.Rare, Stamp, new Random(5)).Item;
        Corruptions.Apply(amulet, Corruptions.Available(amulet).First(option => option.Grants.Count > 0), new Random(1));
        Assert.True(Desecrations.CanTake(amulet), "a corrupted amulet takes a desecration");

        // A mod other than enhanced damage: the one the game's two-bit field would have misread as Attack Rating.
        var mod = Desecrations.Pool(amulet).First(option => option.Grants.All(grant => grant.Code != "dmg%"));
        Assert.True(Desecrations.Apply(amulet, Corruptions.Draft(amulet, mod, new Random(2))));

        var marked = Desecrations.StatsOn(amulet);
        Assert.NotEmpty(marked);
        Assert.All(mod.Grants.SelectMany(grant => AffixRanges.StatsWritten(grant.Code)),
            stat => Assert.Contains(stat, marked, StringComparer.OrdinalIgnoreCase));
    }
}
