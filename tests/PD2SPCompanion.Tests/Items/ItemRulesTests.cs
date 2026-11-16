namespace PD2SPCompanion.Tests.Items;

/// <summary>Rules with no tables at all, and small ones that need them.</summary>
[Collection(nameof(GameTables))]
public sealed class ItemRulesTests(Game game)
{
    [GameTheory]
    [InlineData(2, 3, 4, "(0.5,1) (1.5,2) (0.5,2) (1.5,1)")]      // four on a 2x3: TL, BR, BL, TR
    [InlineData(2, 4, 2, "(1,1) (1,3)")]                           // two on a 2x4 sit apart
    [InlineData(1, 4, 2, "(0.5,1.5) (0.5,2.5)")]                   // two on a 1x4 are packed
    [InlineData(2, 3, 5, "(1,1.5) (0.5,0.5) (1.5,2.5) (0.5,2.5) (1.5,0.5)")]
    [InlineData(1, 1, 1, "(0.5,0.5)")]
    [InlineData(2, 1, 1, "(1,0.5)")]
    public void Sockets_sit_where_the_game_draws_them(int width, int height, int count, string expected)
    {
        string got = string.Join(" ", SocketLayout.Of(width, height, count)
            .Select(spot => $"({spot.X.ToString(System.Globalization.CultureInfo.InvariantCulture)},{spot.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)})"));
        Assert.Equal(expected, got);
    }

    [GameFact]
    public void Every_measured_shape_is_seen()
    {
        foreach (var (w, h, most) in new[] { (1, 1, 1), (2, 1, 1), (1, 2, 2), (1, 3, 3), (1, 4, 4), (2, 2, 4), (2, 3, 6), (2, 4, 6) })
        {
            for (int n = 1; n <= most; n++)
                Assert.True(SocketLayout.IsSeen(w, h, n), $"{w}x{h} with {n}");
        }
    }

    [GameTheory]
    [InlineData("Ab", null)]
    [InlineData("Spear-Zon", null)]
    [InlineData("A", "Require 2 to 15 characters.")]
    [InlineData("Spear9", "Require letters only, with one - or _ at most.")]
    [InlineData("A-b-c", "Require one - or _ at most.")]
    [InlineData("-Ab", "Require a letter first and last.")]
    public void Character_names_follow_the_game(string name, string? problem) =>
        Assert.Equal(problem, CharacterRename.Problem(name));

    [GameTheory]
    [InlineData("amd", 2, 4)]   // Matriarchal Spear: a two-hander by its second type (file order)
    [InlineData("7b7", 2, 4)]   // Champion Sword
    [InlineData("wsd", 1, 2)]   // War Sword, one-handed
    public void The_puzzlebox_takes_the_first_recipe_that_fits(string code, int least, int most)
    {
        if (!game.Available)
            return;

        var item = new D2SLib.Model.Save.Item { Code = code.PadRight(4, ' ') };
        Assert.Equal((least, most), Puzzlebox.RangeFor(item));
    }
}
