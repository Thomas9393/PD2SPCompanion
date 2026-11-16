using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// What the socket die rolls (a die on the socket count, for uniques and for the affixes that
/// have a varying range, then the Puzzlebox's button merged into it): the range of what gave the item its sockets -
/// its own row or affix, its corruption - else the Puzzlebox's; and one to the most the base takes on Shift. The
/// game rolls each once: its Demonic Cube leaves the count (fifty rerolls of a Blade of Ali Baba).
/// </summary>
[Collection(nameof(GameTables))]
public sealed class SocketRangeTests(Game game)
{
    private const string Stamp = "103";

    [CorpusTheory]
    [InlineData("Blade of Ali Baba", 2, 3)]
    [InlineData("Whichwild String", 2, 4)]
    [InlineData("Tomb Reaver", 3, 5)]
    public void A_unique_whose_row_rolls_its_sockets_gives_that_range(string name, int least, int most)
    {
        if (!game.Available)
            return;

        var item = ItemForge.Unique(name, Stamp).Item;

        Assert.Equal((least, most), AffixRanges.SocketRange(item));
        Assert.Equal(new SocketRolls.Range(least, most, SocketRolls.Source.Own), SocketRolls.For(item));
        // Forged at the top of every range, the sockets with the rest.
        Assert.Equal(most, item.TotalNumberOfSockets);
    }

    [CorpusFact]
    public void A_unique_with_no_sockets_of_its_own_rolls_as_the_Puzzlebox_does()
    {
        if (!game.Available)
            return;

        // The Gnasher, a Hand Axe: one-handed, 1-2 - and the axe takes two.
        var axe = ItemForge.Unique("The Gnasher", Stamp).Item;
        Assert.Null(AffixRanges.SocketRange(axe));
        Assert.Equal(new SocketRolls.Range(1, 2, SocketRolls.Source.Puzzlebox), SocketRolls.For(axe));
    }

    [CorpusFact]
    public void Mechanists_rolls_one_to_two_and_a_fixed_affix_only_its_count()
    {
        if (!game.Available)
            return;

        // A Crown: three sockets at this level, so the affix's own range stands.
        var crown = Magic("crn", "Mechanist's");
        Assert.Equal((1, 2), AffixRanges.SocketRange(crown));
        Assert.Equal(new SocketRolls.Range(1, 2, SocketRolls.Source.Own), SocketRolls.For(crown));

        // Artificer's names its three in par: a count, not a roll - the die lands on it and nowhere else.
        var artificers = Magic("crn", "Artificer's");
        Assert.Null(AffixRanges.SocketRange(artificers));
        Assert.Equal(new SocketRolls.Range(3, 3, SocketRolls.Source.Own), SocketRolls.For(artificers));

        // A Buckler takes one socket: the ceiling closes Mechanist's 1-2 to one number.
        var buckler = Magic("buc", "Mechanist's");
        Assert.Equal(1, ItemBases.MaximumSockets(buckler));
        Assert.Null(AffixRanges.SocketRange(buckler));
        Assert.Equal(new SocketRolls.Range(1, 1, SocketRolls.Source.Own), SocketRolls.For(buckler));
    }

    [CorpusFact]
    public void A_plain_base_rolls_as_the_Puzzlebox_does_and_one_to_its_most_on_Shift()
    {
        if (!game.Available)
            return;

        // A Crown, a helm: the box's 1-2; Shift, the socket recipe's one to three.
        var crown = ItemForge.PlainBase("crn", 0, Stamp).Item;
        crown.ItemLevel = 60;
        Assert.Equal(new SocketRolls.Range(1, 2, SocketRolls.Source.Puzzlebox), SocketRolls.For(crown));
        Assert.Equal((1, 3), SocketRolls.Whole(crown));

        // A Thresher, two-handed: the box's 2-4, of the five it takes.
        var thresher = ItemForge.PlainBase("7s8", 0, Stamp).Item;
        thresher.ItemLevel = 60;
        Assert.Equal(new SocketRolls.Range(2, 4, SocketRolls.Source.Puzzlebox), SocketRolls.For(thresher));
        Assert.Equal((1, 5), SocketRolls.Whole(thresher));

        // A ring takes none: nothing to roll.
        var ring = ItemForge.PlainBase("rin", 0, Stamp).Item;
        Assert.Null(SocketRolls.For(ring));
        Assert.Null(SocketRolls.Whole(ring));
    }

    [CorpusFact]
    public void Sockets_a_corruption_gave_roll_in_the_corruptions_range()
    {
        if (!game.Available)
            return;

        // The corpus character's Duriel's Shell: a Cuirass whose corruption bored its two sockets.
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));
        var shell = save.PlayerItemList!.Items.First(item => Corruptions.IdOn(item) == Corruptions.Sockets);

        // Armour: one to three, and the Cuirass takes three.
        var range = SocketRolls.For(shell)!;
        Assert.Equal(new SocketRolls.Range(1, 3, SocketRolls.Source.Corruption), range);

        // Rolled by the corruption's own chances, not evenly (the game's odds): the
        // table's bands for armor are 12, 7 and 6 in a hundred for one, two and three sockets.
        var random = new Random(7);
        var seen = new int[4];
        for (int n = 0; n < 25000; n++)
            seen[SocketRolls.Roll(shell, range, random)]++;

        Assert.Equal(0, seen[0]);
        Assert.InRange(seen[1] / 25000.0, 0.46, 0.50);   // 12 of 25
        Assert.InRange(seen[2] / 25000.0, 0.26, 0.30);   // 7 of 25
        Assert.InRange(seen[3] / 25000.0, 0.22, 0.26);   // 6 of 25
    }

    [CorpusFact]
    public void A_range_of_the_items_own_rolls_evenly()
    {
        if (!game.Available)
            return;

        var blade = ItemForge.Unique("Blade of Ali Baba", Stamp).Item;
        var range = SocketRolls.For(blade)!;
        var random = new Random(7);
        var seen = new int[4];
        for (int n = 0; n < 10000; n++)
            seen[SocketRolls.Roll(blade, range, random)]++;

        Assert.Equal(0, seen[1]);
        Assert.InRange(seen[2] / 10000.0, 0.47, 0.53);
        Assert.InRange(seen[3] / 10000.0, 0.47, 0.53);
    }

    [CorpusFact]
    public void A_socket_corruption_taken_off_leaves_the_sockets_the_items_own_affix_gives()
    {
        if (!game.Available)
            return;

        // An item with sockets from affixes or another source that had sockets from a corruption too: a change to a
        // corruption with no sockets would override and delete
        // the sockets. A Crown of Artificer's (its three) under a corruption that bored its sockets: the clean - what
        // a change of corruption begins with - leaves the affix's three, where it closed every socket.
        // Made the same every run, and with no sockets yet - a socket corruption is offered only then; an unseeded
        // forge once gave it some, and the run found no such corruption to pick.
        var crown = ItemForge.BaseAs("crn", ItemQuality.Magic, Stamp, new Random(5)).Item;
        crown.ItemLevel = 60;
        crown.MagicPrefixIds[0] = (ushort)AffixNames.MagicIdsNamed(true, "Artificer's")[0];
        crown.TotalNumberOfSockets = 0;
        crown.IsSocketed = false;
        var offered = Corruptions.Available(crown);
        Assert.True(offered.Any(option => option.GivesSockets),
            $"no socket corruption offered: sockets {crown.TotalNumberOfSockets}, ethereal {crown.IsEthereal}, offered {string.Join(", ", offered.Select(option => option.Id))}");
        Corruptions.Apply(crown, offered.Last(option => option.GivesSockets), new Random(3));
        Assert.Equal(Corruptions.Sockets, Corruptions.IdOn(crown));

        Corruptions.Clean(crown, new Random(3));
        Assert.Equal(3, crown.TotalNumberOfSockets);
        Assert.True(crown.IsSocketed);

        // A Crown with no socket source of its own: the corruption's sockets were all it had.
        var plain = ItemForge.BaseAs("crn", ItemQuality.Magic, Stamp, new Random(5)).Item;
        plain.ItemLevel = 60;
        plain.TotalNumberOfSockets = 0;
        plain.IsSocketed = false;
        Corruptions.Apply(plain, Corruptions.Available(plain).Last(option => option.GivesSockets), new Random(3));
        Assert.True(plain.TotalNumberOfSockets > 0);
        Corruptions.Clean(plain, new Random(3));
        Assert.Equal(0, plain.TotalNumberOfSockets);
        Assert.False(plain.IsSocketed);
    }

    [CorpusFact]
    public void A_socket_corruption_changed_for_another_never_goes_under_the_items_own_sockets()
    {
        if (!game.Available)
            return;

        // The socket-to-socket form: a Crown of Artificer's (its three) under a
        // socket corruption, changed for a row of fewer: the affix's three stay. The list still names the socket rows,
        // which is how the corruption on the item is told (Corruptions.On).
        var crown = ItemForge.BaseAs("crn", ItemQuality.Magic, Stamp, new Random(5)).Item;
        crown.ItemLevel = 60;
        crown.MagicPrefixIds[0] = (ushort)AffixNames.MagicIdsNamed(true, "Artificer's")[0];
        crown.TotalNumberOfSockets = 0;
        crown.IsSocketed = false;
        var socketRows = Corruptions.Available(crown).Where(option => option.GivesSockets).ToList();
        Corruptions.Apply(crown, socketRows[^1], new Random(3));
        Assert.Equal(Corruptions.Sockets, Corruptions.IdOn(crown));

        Assert.Contains(Corruptions.Available(crown, asIfClean: true), option => option.GivesSockets);

        Corruptions.Clean(crown, new Random(3));
        Corruptions.Apply(crown, socketRows[0], new Random(3));
        Assert.True(crown.TotalNumberOfSockets >= 3, $"the affix's sockets went: {crown.TotalNumberOfSockets}");
    }

    [CorpusFact]
    public void A_socket_affix_set_on_a_socket_corruption_cleans_it_and_gives_its_own_sockets()
    {
        if (!game.Available)
            return;

        // A corruption's sockets held the affix's back until the corruption was off.
        var crown = ItemForge.BaseAs("crn", ItemQuality.Magic, Stamp, new Random(5)).Item;
        crown.ItemLevel = 60;
        crown.TotalNumberOfSockets = 0;
        crown.IsSocketed = false;
        Corruptions.Apply(crown, Corruptions.Available(crown).Last(each => each.GivesSockets), new Random(3));
        Assert.Equal(Corruptions.Sockets, Corruptions.IdOn(crown));

        int id = AffixNames.MagicIdsNamed(true, "Artificer's")[0];
        var option = MagicAffixes.Find(id, prefix: true)!;
        var picks = new Dictionary<(int Slot, bool Prefix), MagicAffixes.AffixPick>
        {
            [(0, true)] = new MagicAffixes.AffixPick(id, MagicAffixes.Roll(option, new Random(3))),
        };

        Assert.True(MagicAffixes.CleanSocketCorruptionFor(crown, picks, new Random(3)));
        MagicAffixes.Apply(crown, picks, new Random(3));

        Assert.False(Corruptions.IsCorrupted(crown));
        Assert.Equal(3, crown.TotalNumberOfSockets);
        // Nothing left to clean, and an affix with no sockets never asks.
        Assert.False(MagicAffixes.CleanSocketCorruptionFor(crown, picks, new Random(3)));
    }

    private static Item Magic(string code, string prefix)
    {
        var item = ItemForge.BaseAs(code, ItemQuality.Magic, Stamp).Item;
        item.ItemLevel = 60;
        item.MagicPrefixIds[0] = (ushort)AffixNames.MagicIdsNamed(true, prefix)[0];
        return item;
    }
}
