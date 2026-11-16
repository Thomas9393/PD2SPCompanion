using D2SLib;

namespace PD2SPCompanion.Tests.Saves;

/// <summary>
/// The parser's switches as a scope: what a read or write sets is put back after,
/// and a save writes with the layout it was read with, whatever was read since.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class ParserLayoutTests(Game game)
{
    [CorpusFact]
    public void A_scope_puts_the_switches_back()
    {
        using (Globals.Use(new ParserLayout(true, 10, false)))
        {
            using (Globals.Use(new ParserLayout(false, 9, true, Pd2Stash: true)))
            {
                Assert.Equal(new ParserLayout(false, 9, true, true), Globals.Layout);
            }

            Assert.Equal(new ParserLayout(true, 10, false), Globals.Layout);
        }
    }

    [CorpusFact]
    public void A_save_writes_with_its_own_layout_after_a_stash_was_read()
    {
        if (!game.Available)
            return;

        string scratch = Path.Combine(Path.GetTempPath(), "pd2spc-tests", nameof(ParserLayoutTests));
        string path = Game.CopyOfCharacter(scratch);
        var (save, _) = SaveVerifier.Load(path);
        byte[] original = File.ReadAllBytes(path);

        // What a shared stash read used to leave behind: stash layout, no skill bytes.
        Globals.ResetForFile(pd2Character: false, statIdBits: 9);

        Assert.Equal(original, SaveWriter.Prepared(save));
        Assert.Equal(new ParserLayout(false, 9, false), Globals.Layout);
    }

    [CorpusFact]
    public void Reading_a_shared_stash_leaves_the_switches_as_they_were()
    {
        if (!game.Available)
            return;

        string stash = Path.Combine(Game.Corpus, "saves", "pd2_shared.stash");
        if (!File.Exists(stash))
            return;

        using (Globals.Use(new ParserLayout(true, 10, false)))
        {
            SharedStash.Read(stash);
            Assert.Equal(new ParserLayout(true, 10, false), Globals.Layout);
        }
    }
}
