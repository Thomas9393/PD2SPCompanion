using D2SLib.Model.Save;

using PD2SPCompanion.Core.Saves;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// A gem, a rune or a jewel put into an item's empty socket by hand - dragged
/// onto the item, making a runeword this way too: what counts as a filler, a
/// stack standing for its single, and the runeword forming on the rune that fills the last socket - every word
/// of the table, set in rune by rune, read back from its bytes and silent to the legitimacy check.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class SocketingTests(Game game)
{
    private const string Stamp = "103";

    private static Item Rune(string code) => ItemForge.Material(code, 1, Stamp);

    [GameTheory]
    [InlineData("r01", "r01")]     // a rune
    [InlineData("r01s", "r01")]    // a stack of them: one of it, as the single
    [InlineData("gpw", "gpw")]     // a gem
    [InlineData("gpws", "gpw")]
    [InlineData("jew", "jew")]     // a jewel
    [InlineData("hax", null)]      // a weapon
    [InlineData("pk1", null)]      // a key: a material, and no filler
    [InlineData("rvs", null)]      // a potion whose code ends in s
    public void A_filler_is_what_the_table_says_goes_in_a_socket(string code, string? socketedAs)
    {
        if (!game.Available)
            return;

        Assert.Equal(socketedAs, Socketing.FillerCode(code));
        Assert.Equal(socketedAs, Socketing.FillerCode(code.PadRight(4, ' ')));
    }

    [GameFact]
    public void A_single_is_taken_whole_and_leaves_its_file()
    {
        if (!game.Available)
            return;

        var gem = Rune("gpw");

        var taken = Socketing.Take(gem, "Host", Stamp, ItemSeason.Mix, new Random(1));

        Assert.Same(gem, taken.Filler);
        Assert.Equal(0, taken.Left);
        Assert.Null(taken.Refused);
    }

    [GameFact]
    public void One_of_a_stack_is_a_new_single_stamped_as_its_host_and_the_stack_is_left_alone()
    {
        if (!game.Available)
            return;

        var stack = ItemForge.Material("gpw", 3, "105");
        uint id = stack.Id;
        Assert.True(Stacks.IsStackable("gpw"), "a plain gem no longer stacks: this no longer tests a stack of itself");

        var taken = Socketing.Take(stack, "Host", Stamp, ItemSeason.Mix, new Random(1));

        Assert.NotSame(stack, taken.Filler);
        Assert.Equal("gpw", taken.Filler!.Code.Trim());
        Assert.Equal(1, Stacks.Shown(taken.Filler.Quantity));
        Assert.Equal(Stamp, taken.Filler.Version);
        Assert.NotEqual(0u, taken.Filler.Id);
        Assert.Equal(2, taken.Left);
        Assert.Null(taken.Refused);

        // The caller's to change: told, not done.
        Assert.Equal(3, Stacks.Shown(stack.Quantity));
        Assert.Equal(id, stack.Id);
    }

    [GameTheory]
    [InlineData("gpws")]
    [InlineData("r01s")]
    public void The_last_of_a_stack_of_another_code_gives_a_single_and_leaves_its_file(string code)
    {
        if (!game.Available)
            return;

        var last = ItemForge.Material(code, 1, Stamp);

        var taken = Socketing.Take(last, "Host", Stamp, ItemSeason.Mix, new Random(1));

        Assert.NotSame(last, taken.Filler);
        Assert.Equal(code[..^1], taken.Filler!.Code.Trim());
        Assert.Equal(0, taken.Left);
    }

    [GameFact]
    public void A_single_of_a_stamp_that_does_not_mix_is_refused_a_stack_stamped_anew_is_not()
    {
        if (!game.Available)
            return;

        var odd = ItemForge.Material("gpw", 1, "50");
        var refused = Socketing.Take(odd, "Host", Stamp, ItemSeason.Mix, new Random(1));
        Assert.Null(refused.Filler);
        Assert.Equal($"Require same version: item v50, Host v{Stamp}.", refused.Refused);

        // With no stamp to judge by, it goes in.
        Assert.Same(odd, Socketing.Take(odd, "Host", "", ItemSeason.Mix, new Random(1)).Filler);

        var stack = ItemForge.Material("gpw", 2, "50");
        var taken = Socketing.Take(stack, "Host", Stamp, ItemSeason.Mix, new Random(1));
        Assert.Null(taken.Refused);
        Assert.Equal(Stamp, taken.Filler!.Version);
    }

    [GameFact]
    public void A_gem_goes_into_the_next_socket_and_makes_no_word()
    {
        if (!game.Available)
            return;

        var armour = ItemForge.PlainBase("xpl", 2, Stamp).Item;
        Assert.True(Socketing.HasRoom(armour));

        Assert.Null(Socketing.Put(armour, Rune("gpw"), new Random(1)));
        Assert.Null(Socketing.Put(armour, Rune("r01"), new Random(1)));

        Assert.False(Socketing.HasRoom(armour));
        Assert.False(armour.IsRuneword);
        Assert.Equal([0, 1], armour.SocketedItems.Select(each => (int)each.X));
        Assert.All(armour.SocketedItems, each => Assert.Equal(ItemMode.Socket, each.Mode));
        Assert.Equal(2, armour.NumberOfSocketedItems);
        Assert.Throws<InvalidOperationException>(() => Socketing.Put(armour, Rune("r01"), new Random(1)));
    }

    /// <summary>Every word, in the best base it goes in: no word until the last rune, then that word, with the runes set in kept.</summary>
    [GameFact]
    public void Every_runeword_forms_on_its_last_rune()
    {
        if (!game.Available)
            return;

        int made = 0;
        var random = new Random(7);
        foreach (var word in Runewords.All)
        {
            if (ItemForge.BestBaseFor(word.Types, word.ExcludedTypes, word.Runes.Length) is not { } code)
                continue;

            var basis = ItemForge.PlainBase(code, word.Runes.Length, Stamp).Item;
            var runes = word.Runes.Select(Rune).ToList();
            for (int at = 0; at < runes.Count; at++)
            {
                var formed = Socketing.Put(basis, runes[at], random);
                if (at < runes.Count - 1)
                    Assert.Null(formed);
                else
                    Assert.True(formed is not null && formed.Runes.SequenceEqual(word.Runes), $"{word.Name} in {code}: {formed?.Name ?? "no word"}");
            }

            Assert.True(basis.IsRuneword, $"{word.Name} in {code}");
            Assert.True(basis.SocketedItems.SequenceEqual(runes), $"{word.Name}: the runes set in are the ones it holds");

            var copy = ItemBlob.Copy(basis, D2SLib.Globals.stat_id_bits);
            Assert.True(Runewords.On(copy)?.Runes.SequenceEqual(word.Runes), $"{word.Name} in {code} reads back as {Runewords.On(copy)?.Name ?? "nothing"}");
            Assert.True(Legitimacy.Check(copy) is { Count: 0 }, $"{word.Name} in {code}: {Legitimacy.Check(copy).FirstOrDefault()?.Said}");
            made++;
        }

        Assert.True(made > 50, $"{made} words made");
    }

    /// <summary>The game's conditions: the runes in the word's order, as many sockets as runes, a plain base.</summary>
    [GameFact]
    public void No_word_out_of_order_with_a_socket_over_or_on_a_magic_base()
    {
        if (!game.Available)
            return;

        var word = Runewords.All.First(each => each.Runes.Length == 2 && each.Runes[0] != each.Runes[1]);
        string code = ItemForge.BestBaseFor(word.Types, word.ExcludedTypes, 3)!;

        var backwards = ItemForge.PlainBase(code, 2, Stamp).Item;
        Socketing.Put(backwards, Rune(word.Runes[1]), new Random(1));
        Assert.Null(Socketing.Put(backwards, Rune(word.Runes[0]), new Random(1)));
        Assert.False(backwards.IsRuneword);

        var roomy = ItemForge.PlainBase(code, 3, Stamp).Item;
        Socketing.Put(roomy, Rune(word.Runes[0]), new Random(1));
        Assert.Null(Socketing.Put(roomy, Rune(word.Runes[1]), new Random(1)));
        Assert.False(roomy.IsRuneword);

        var magic = ItemForge.PlainBase(code, 2, Stamp).Item;
        magic.Quality = ItemQuality.Magic;
        Socketing.Put(magic, Rune(word.Runes[0]), new Random(1));
        Assert.Null(Socketing.Put(magic, Rune(word.Runes[1]), new Random(1)));
        Assert.False(magic.IsRuneword);
    }
}
