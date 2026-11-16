namespace PD2SPCompanion.Tests.Saves;

/// <summary>
/// A file that only round-trips as Vanilla or as PD2 up to S8 reads back under today's tables, which are not its own.
/// The editor's way in refuses it; the command line can still name its layout, and what
/// reads a pack under the pack's own tables can still open it. On a blank character of an old item pack.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class OldLayoutTests(Game game)
{
    private static string Blank => Path.Combine(Game.Corpus, "packs", "betweenwalls-s7", "PD2-Singleplayer-main", "Diablo II",
                                                "Save", "Library", "Blank Characters", "Level 30s", "Amazon.d2s");

    [CorpusFact]
    public void A_file_of_an_old_layout_is_refused_by_Load_and_named_by_Verify_and_Explain()
    {
        if (!game.Available || !File.Exists(Blank))
            return;

        var verified = SaveVerifier.Verify(Blank);
        Assert.True(verified.RoundTripped, verified.Summary);
        Assert.True(verified.Format is SaveFormat.Vanilla or SaveFormat.Pd2Legacy, verified.Format.ToString());

        Assert.Contains(SaveVerifier.Explain(Blank), attempt => attempt.Format == verified.Format && attempt.RoundTripped);

        var refused = Assert.Throws<InvalidDataException>(() => SaveVerifier.Load(Blank));
        Assert.Equal("'Amazon.d2s' not readable: an older PD2 season's file, or something this version cannot read yet.", refused.Message);

        var (save, result) = SaveVerifier.LoadAnyLayout(Blank);
        Assert.Equal(verified.Format, result.Format);
        Assert.NotNull(save);

        // Put back by the refusal: a character of today's layout reads after it, read through Load as ever.
        Assert.Throws<InvalidDataException>(() => SaveVerifier.Load(Blank));
        Assert.Equal(SaveFormat.Pd2, SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s")).Result.Format);
    }
}
