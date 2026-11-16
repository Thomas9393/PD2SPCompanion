using D2SLib.Model.Save;
using Xunit;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// The form a rune or gem takes in a socket and out of one (<see cref="ItemForge.Socket"/>, <see cref="ItemForge.Loose"/>).
/// Season 14 stacks runes and gems as themselves, loose (<c>r04</c>, extended, a quantity), and keeps a socketed one
/// compact as Season 13 did: NewS with a Nef socketed into Bonehew, against the Nef the S14
/// tab handed out loose.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class SocketFormTests(Game game)
{
    private static string Specimen(string when) =>
        Path.Combine(Game.Corpus, "specimens", "s14-launch-2026-10-23", when, "NewS.d2s");

    /// <summary>An item's bytes alone, as a socketed one is written - the game's "just handed over" flag (13) aside.</summary>
    private static byte[] Bytes(Item item, bool socketed)
    {
        item.IsNew = false;
        if (socketed)
        {
            item.X = 0;
            item.Y = 0;
        }
        else
        {
            item.Id = 0;
            item.Page = 0;
            item.X = 0;
            item.Y = 0;
        }

        return Item.Write(item, 0x60);
    }

    [CorpusFact]
    public void A_rune_put_in_a_socket_in_season_14_is_the_games_own()
    {
        // Season 14's own tables only: under S13's a rune never stacked, and the file holding a loose one does not read.
        if (!game.Available || Game.Tables is not null || !File.Exists(Specimen("socketed")))
            return;

        var (save, _) = SaveVerifier.Load(Specimen("socketed"));
        var bonehew = save.PlayerItemList.Items.Single(item => item.SocketedItems.Any(gem => gem.Code.Trim() == "r04"));
        var games = bonehew.SocketedItems.Single();
        Assert.True(games.IsCompact);

        var host = ItemForge.PlainBase(bonehew.Code.Trim(), bonehew.TotalNumberOfSockets, games.Version).Item;
        var ours = ItemForge.Material("r04", 1, games.Version);
        Assert.False(ours.IsCompact);

        ItemForge.Socket(host, ours);

        Assert.True(ours.IsCompact);
        Assert.Equal(Bytes(games, socketed: true), Bytes(ours, socketed: true));
    }

    [CorpusFact]
    public void A_rune_taken_out_of_a_socket_in_season_14_lies_loose_as_the_tab_hands_it_out()
    {
        if (!game.Available || Game.Tables is not null || !File.Exists(Specimen("socketed")) || !File.Exists(Specimen("session2")))
            return;

        var (socketedSave, _) = SaveVerifier.Load(Specimen("socketed"));
        var socketed = socketedSave.PlayerItemList.Items.SelectMany(item => item.SocketedItems).Single(gem => gem.Code.Trim() == "r04");
        var (looseSave, _) = SaveVerifier.Load(Specimen("session2"));
        var games = looseSave.PlayerItemList.Items.Single(item => item.Code.Trim() == "r04");

        var loose = ItemForge.Loose(socketed);

        Assert.False(loose.IsCompact);
        Assert.Equal(1, loose.Quantity);
        ItemMover.Place(loose, games.Page, games.X, games.Y);
        Assert.Equal(Bytes(games, socketed: false), Bytes(loose, socketed: false));
    }

    [CorpusFact]
    public void A_jewel_keeps_its_form_in_a_socket_and_out_of_one()
    {
        if (!game.Available)
            return;

        var host = ItemForge.PlainBase("7o7", 4, "103").Item;
        var jewel = ItemForge.BaseAs("jew", ItemQuality.Magic, "103", new Random(3)).Item;

        ItemForge.Socket(host, jewel);

        Assert.False(jewel.IsCompact);
        Assert.Same(jewel, ItemForge.Loose(jewel));
    }
}
