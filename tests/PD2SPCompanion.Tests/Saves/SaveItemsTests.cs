using D2SLib.Model.Save;
using Xunit;

namespace PD2SPCompanion.Tests.Saves;

/// <summary>
/// What each scope of <see cref="SaveItems.Walk"/> covers, on a file built by hand so that each section holds one item
/// with one filler. The moved call sites each name a scope; these say what that name means.
/// </summary>
public sealed class SaveItemsTests
{
    private static Item Named(string code, params Item[] fillers) =>
        new() { Code = code, SocketedItems = [.. fillers] };

    private static D2S File(bool golemStands = true) => new()
    {
        PlayerItemList = new ItemList { Items = [Named("own", Named("own-gem")), Named("own2")] },
        MercenaryItemList = new MercenaryItemList { ItemList = new ItemList { Items = [Named("merc", Named("merc-gem"))] } },
        PlayerCorpses = new CorpseList
        {
            Corpses =
            [
                new Corpse { ItemList = new ItemList { Items = [Named("corpse1", Named("corpse1-gem"))] } },
                new Corpse { ItemList = new ItemList { Items = [Named("corpse2")] } },
            ],
        },
        Golem = new Golem { Exists = golemStands, Item = Named("golem", Named("golem-gem")) },
    };

    private static string[] Walked(D2S? save, ItemScope scope) => [.. SaveItems.Walk(save, scope).Select(item => item.Code)];

    [Theory]
    [InlineData(ItemScope.Own, "own own2")]
    [InlineData(ItemScope.Mercenary, "merc")]
    [InlineData(ItemScope.Corpses, "corpse1 corpse2")]
    [InlineData(ItemScope.Golem, "golem")]
    [InlineData(ItemScope.Sockets, "")]
    [InlineData(ItemScope.Own | ItemScope.Sockets, "own own-gem own2")]
    [InlineData(ItemScope.Own | ItemScope.Mercenary, "own own2 merc")]
    [InlineData(ItemScope.Own | ItemScope.Mercenary | ItemScope.Sockets, "own own-gem own2 merc merc-gem")]
    [InlineData(ItemScope.Corpses | ItemScope.Sockets, "corpse1 corpse1-gem corpse2")]
    [InlineData(ItemScope.Golem | ItemScope.Sockets, "golem golem-gem")]
    [InlineData(ItemScope.Own | ItemScope.Mercenary | ItemScope.Corpses, "own own2 merc corpse1 corpse2")]
    [InlineData(ItemScope.Own | ItemScope.Mercenary | ItemScope.Corpses | ItemScope.Golem, "own own2 merc corpse1 corpse2 golem")]
    [InlineData(ItemScope.Own | ItemScope.Mercenary | ItemScope.Corpses | ItemScope.Golem | ItemScope.Sockets,
        "own own-gem own2 merc merc-gem corpse1 corpse1-gem corpse2 golem golem-gem")]
    public void A_scope_walks_its_parts_and_no_others(ItemScope scope, string expected) =>
        Assert.Equal(expected.Split(' ', StringSplitOptions.RemoveEmptyEntries), Walked(File(), scope));

    [Fact]
    public void A_golem_not_standing_holds_nothing_even_with_an_item_left_in_it() =>
        Assert.Equal(["own", "own2"], Walked(File(golemStands: false), ItemScope.Own | ItemScope.Golem));

    [Fact]
    public void A_file_missing_a_section_walks_the_rest()
    {
        var all = ItemScope.Own | ItemScope.Mercenary | ItemScope.Corpses | ItemScope.Golem | ItemScope.Sockets;

        Assert.Empty(Walked(null, all));
        Assert.Empty(Walked(new D2S(), all));
        Assert.Equal(["own", "own-gem", "own2"], Walked(new D2S { PlayerItemList = File().PlayerItemList }, all));

        var bareCorpse = new D2S { PlayerCorpses = new CorpseList { Corpses = [new Corpse()] } };
        Assert.Empty(Walked(bareCorpse, all));
    }

    [Fact]
    public void A_list_or_an_item_with_its_sockets_comes_parent_first()
    {
        var item = Named("jewelled", Named("a"), Named("b"));

        Assert.Equal(["jewelled", "a", "b"], item.WithSockets().Select(each => each.Code));
        Assert.Equal(["plain", "jewelled", "a", "b"], new[] { Named("plain"), item }.WithSockets().Select(each => each.Code));
    }

    [Fact]
    public void The_walk_hands_back_the_items_themselves()
    {
        var save = File();
        var golem = save.Golem.Item;
        var filler = golem.SocketedItems[0];
        var walked = SaveItems.Walk(save, ItemScope.Golem | ItemScope.Sockets).ToList();

        Assert.Same(golem, walked[0]);
        Assert.Same(filler, walked[1]);
    }

    /// <summary>A move's count goes by the top of own and mercenary alone: a corpse's, the golem's and a socket's are not counted.</summary>
    [Fact]
    public void A_moves_count_sees_own_and_mercenary_alone()
    {
        var save = File();
        save.Attributes = new Attributes();
        foreach (var item in SaveItems.Walk(save, ItemScope.Own | ItemScope.Mercenary | ItemScope.Corpses | ItemScope.Golem | ItemScope.Sockets))
            item.IsCompact = true;

        var counts = SaveCounts.Of(save);

        foreach (string code in new[] { "own", "own2", "merc" })
            Assert.Equal(1, counts.Count(null, code));
        foreach (string code in new[] { "own-gem", "merc-gem", "corpse1", "corpse2", "golem", "golem-gem" })
            Assert.Equal(0, counts.Count(null, code));
    }
}
