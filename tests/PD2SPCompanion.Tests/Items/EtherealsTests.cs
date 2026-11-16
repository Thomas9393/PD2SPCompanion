using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// Which bases can be ethereal: a base with
/// <c>nodurability</c> through its whole tier family never is - 0 of 799 ladder bows and crossbows - while the Phase
/// Blade is, cubed up from an ethereal Crystal Sword. The legitimacy check says so of an ethereal bow, and of an
/// ethereal sword nothing.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class EtherealsTests(Game game)
{
    [GameFact]
    public void Bows_and_crossbows_cannot_be_ethereal_and_the_Phase_Blade_can()
    {
        if (!game.Available)
            return;

        Assert.False(Ethereals.BaseCanBe("sbw"));   // Short Bow
        Assert.False(Ethereals.BaseCanBe("lxb"));   // Light Crossbow
        Assert.True(Ethereals.NoDurability("7cr")); // Phase Blade: none of its own
        Assert.True(Ethereals.BaseCanBe("7cr"));    // but its family's Crystal Sword has
        Assert.True(Ethereals.BaseCanBe("lsd"));    // Long Sword
        Assert.True(Ethereals.BaseCanBe("jav"));    // Javelin: a throwing weapon has durability
        Assert.False(Ethereals.BaseCanBe("rin"));   // not gear
    }

    [GameFact]
    public void An_ethereal_bow_is_said_and_an_ethereal_sword_is_not()
    {
        if (!game.Available)
            return;

        var bow = ItemForge.BaseAs("sbw", ItemQuality.Normal, "103", new Random(1)).Item;
        bow.IsEthereal = true;
        Assert.Contains(Legitimacy.Judge(bow), finding => finding.Subject == "Ethereal" && finding.Certainty == Certainty.Impossible);

        var sword = ItemForge.BaseAs("lsd", ItemQuality.Normal, "103", new Random(1)).Item;
        sword.IsEthereal = true;
        Assert.DoesNotContain(Legitimacy.Judge(sword), finding => finding.Subject == "Ethereal");
    }

    /// <summary>
    /// The item, not only its base (Azurwrath or Lightsabre never is): a
    /// Phase Blade is ethereal only cubed up - Ginther's Rift from its Dimensional Blade, a rare - never a runeword or a
    /// Lightsabre; an indestructible row never; an ethereal-always row always.
    /// </summary>
    [GameFact]
    public void Lightsabre_never_Ginthers_Rift_cubed_up_may_and_Ethereal_Edge_always()
    {
        if (!game.Available)
            return;

        Ethereals.Form Of(string unique, string? code = null)
        {
            var item = ItemForge.Unique(unique, "105", random: new Random(1)).Item;
            if (code is not null)
                item.Code = code;
            return Ethereals.Of(item);
        }

        Assert.Equal(Ethereals.Form.Never, Of("Lightsabre"));
        Assert.Equal(Ethereals.Form.Never, Of("Azurewrath"));
        Assert.Equal(Ethereals.Form.Either, Of("Ginther's Rift", "7cr"));
        Assert.Equal(Ethereals.Form.Never, Of("Tyrael's Might"));
        Assert.Equal(Ethereals.Form.Always, Of("Ethereal Edge"));
        Assert.Equal(Ethereals.Form.Either, Of("Doombringer"));

        // A set row's ethereal does nothing (three real ones, all plain): indestructible, so never - and made plain.
        var sazabi = ItemForge.SetItemAt(ItemForge.SetItemNamed("Sazabi's Cobalt Redeemer")!.Value, "105", random: new Random(1)).Item;
        Assert.False(sazabi.IsEthereal);
        Assert.Equal(Ethereals.Form.Never, Ethereals.Of(sazabi));

        var rare = ItemForge.BaseAs("7cr", ItemQuality.Rare, "105", new Random(1)).Item;
        Assert.Equal(Ethereals.Form.Either, Ethereals.Of(rare));
        var white = ItemForge.BaseAs("7cr", ItemQuality.Normal, "105", new Random(1)).Item;
        Assert.Equal(Ethereals.Form.Never, Ethereals.Of(white));

        var lightsabre = ItemForge.Unique("Lightsabre", "105", random: new Random(1)).Item;
        lightsabre.IsEthereal = true;
        Assert.Contains(Legitimacy.Judge(lightsabre), finding => finding.Subject == "Ethereal" && finding.Certainty == Certainty.Impossible);
        var edge = ItemForge.Unique("Ethereal Edge", "105", random: new Random(1)).Item;
        edge.IsEthereal = false;
        Assert.Contains(Legitimacy.Judge(edge), finding => finding.Subject == "Ethereal" && finding.Certainty == Certainty.Impossible);
    }
}
