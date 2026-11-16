using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Grail;

/// <summary>
/// The grail's copies held, on the other characters too:
/// a card counts the copies the other characters of the open one's
/// mode hold, read from their saves without opening them. The fixture's second character, Finder, is the open
/// one's save under another name - so it wears what the open one wears - and the shared stash is nobody else's.
/// <para>
/// What the grail counts. That the card says it in a line of its own - "Other characters 1" - is the UI case's
/// to show (units/grail-others.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class GrailOthersTests(Program program)
{
    private string Stamps() => string.Join(";", Directory.GetFiles(Path.Combine(program.Folder, "saves"), "*.d2s").Order()
        .Select(save => $"{Path.GetFileName(save)}|{File.GetLastWriteTimeUtc(save).Ticks}|{new FileInfo(save).Length}"));

    [CorpusFact]
    public void A_card_counts_the_copies_the_other_characters_hold_from_their_saves_and_writes_none()
    {
        if (!program.Ready())
            return;

        string stamps = Stamps();
        var grail = program.On(() => GrailHands.OnRight(program));
        try
        {
            // Worn by the open character, and so by Finder: one copy elsewhere.
            var shell = program.On(() => GrailHands.Card(grail, GrailViewModel.Uniques, "Duriel's Shell"));
            Assert.True(program.Until(() => shell.HeldOnOthers == 1, 10000), $"Other characters {shell.HeldOnOthers} on Duriel's Shell's card");
            program.On(() =>
            {
                Assert.True(shell.ShowsHeld);
                Assert.Equal(1, shell.HeldOnCharacter);
                Assert.Equal(shell.HeldOnCharacter + shell.HeldInVault + 1, shell.TotalHeld);

                // In the shared stash only, which is no other character's.
                var cleaver = GrailHands.Card(grail, GrailViewModel.Uniques, "Death Cleaver");
                Assert.True(cleaver.HeldOnCharacter >= 1);
                Assert.Equal(0, cleaver.HeldOnOthers);
            });

            Assert.Equal(stamps, Stamps());
        }
        finally
        {
            program.On(() => GrailHands.Rest(grail));
        }
    }
}
