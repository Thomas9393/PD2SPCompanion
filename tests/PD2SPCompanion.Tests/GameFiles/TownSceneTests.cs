using PD2SPCompanion.Core.Graphics;
using PD2SPCompanion.Core.Saves;

namespace PD2SPCompanion.Tests.GameFiles;

/// <summary>
/// The towns the opened character card stands its figure in (Core's TownScene): each act's map and tiles are
/// found through the install's own tables, its stash is on the map, and the backdrop is the ground around it -
/// drawn at its middle, black at its corners. And the act itself, which the save's header gives.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class TownSceneTests(Game game)
{
    [CorpusTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Every_act_has_a_town_with_a_stash_and_a_backdrop(int act)
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var scene = TownScene.Open(data, act);
        Assert.NotNull(scene);
        Assert.NotNull(scene.Stash);

        var backdrop = scene.Backdrop();
        Assert.NotNull(backdrop);
        Assert.Equal((TownScene.BackdropWidth, TownScene.BackdropHeight), (backdrop.Width, backdrop.Height));

        // Ground under the feet: the middle is drawn, and lit - no town's is left as dark as its tiles are.
        int lit = 0;
        const int Reach = 50;
        for (int row = backdrop.Height / 2 - Reach; row < backdrop.Height / 2 + Reach; row++)
            for (int column = backdrop.Width / 2 - Reach; column < backdrop.Width / 2 + Reach; column++)
                if (Light(backdrop.Bgra[row * backdrop.Width + column]) > 12)
                    lit++;
        Assert.True(lit > Reach * Reach * 4 * 0.8, $"act {act}: {lit} of {Reach * Reach * 4} pixels lit at the middle");

        // No frame of its own: it has faded out before its corners.
        Assert.Equal(0, Light(backdrop.Bgra[0]));
        Assert.Equal(0, Light(backdrop.Bgra[^1]));
    }

    [CorpusFact]
    public void No_town_past_the_fifth_act()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        Assert.Null(TownScene.Open(data, 6));
    }

    [CorpusTheory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    public void A_save_says_which_act_it_is_in(int act)
    {
        if (!Directory.Exists(Game.Corpus))
            return;

        string scratch = Path.Combine(Path.GetTempPath(), "pd2spc-tests", $"act-{act}-{Guid.NewGuid():N}");
        try
        {
            string save = Game.CopyOfCharacter(scratch);

            // The three difficulty bytes: the one in play has its high bit set, and holds the act from 0.
            byte[] bytes = File.ReadAllBytes(save);
            for (int at = 0xA8; at <= 0xAA; at++)
                if ((bytes[at] & 0x80) != 0)
                    bytes[at] = (byte)((bytes[at] & 0xF8) | (act - 1));
            File.WriteAllBytes(save, bytes);

            Assert.Equal(act, SaveSummary.TryRead(save)?.Act);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    private static int Light(uint pixel) => (int)Math.Max((pixel >> 16) & 0xFF, Math.Max((pixel >> 8) & 0xFF, pixel & 0xFF));
}
