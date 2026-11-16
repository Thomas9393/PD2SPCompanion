using D2SLib.Model.Save;
using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Shell.Services.Character;
using Xunit;

namespace PD2SPCompanion.Shell.Tests.Character;

/// <summary>Which parts of a character's file each question counts as held, on a file built by hand with one item in each part.</summary>
public sealed class ItemScopesTests
{
    private static Item Named(string code, params Item[] fillers) =>
        new() { Code = code, SocketedItems = [.. fillers] };

    private static readonly Item Own = Named("own"), Hired = Named("merc"), OnCorpse = Named("corpse"), OnGolem = Named("golem");

    private static D2S File() => new()
    {
        PlayerItemList = new ItemList { Items = [Own] },
        MercenaryItemList = new MercenaryItemList { ItemList = new ItemList { Items = [Hired] } },
        PlayerCorpses = new CorpseList { Corpses = [new Corpse { ItemList = new ItemList { Items = [OnCorpse] } }] },
        Golem = new Golem { Exists = true, Item = OnGolem },
    };

    /// <summary>Taken from a corpse or handed back by the golem, an item of a last-season file is no arrival from elsewhere.</summary>
    [Fact]
    public void A_last_season_file_owns_what_lies_on_its_corpse_and_its_golem()
    {
        var own = LastSeasonGuard.OwnOf(true, File());

        foreach (var item in new[] { Own, Hired, OnCorpse, OnGolem })
            Assert.Null(LastSeasonGuard.Refuses(own, item, "Character"));

        Assert.NotNull(LastSeasonGuard.Refuses(own, Named("elsewhere"), "Character"));
        Assert.Null(LastSeasonGuard.OwnOf(false, File()));
    }

    /// <summary>Write order goes by what a file gains and loses, so an item moved onto the golem counts as gained.</summary>
    [Fact]
    public void What_a_file_holds_for_write_order_takes_in_the_corpse_and_the_golem() =>
        Assert.Equal(["own", "merc", "corpse", "golem"], CharacterSession.ItemsHeldIn(File()).Select(item => item.Code));

    /// <summary>The warning that another character holds an item looks at its mercenary too, as the duplicate index does.</summary>
    [Fact]
    public void Another_characters_mercenary_counts_as_holding_an_item()
    {
        var file = File();
        file.MercenaryItemList.ItemList.Items[0] = Named("merc", Named("gem"));

        Assert.Equal(["own", "merc", "gem"], CharacterSession.ItemsAnotherHolds(file).Select(item => item.Code));
    }
}
