using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Home;

/// <summary>
/// The character health mark, subtle, shown when something is wrong and
/// not otherwise: no mark on a clean
/// character and stash; with a shared stash holding two items the game could not hold - the UI pad's fixture
/// pd2_shared_impossible.stash (Season 14's), Pus Spitter and Toothrow with two sockets more than their bases take - the
/// mark, naming them; and judged only while the home page is shown, or once when it is shown next.
/// <para>
/// What the home page decides, the stash written under the program as the game writes it and noticed as the
/// window's coming back notices it (GameSync.Check). That the mark is drawn on the card, and that the page's
/// coming into sight reaches it, is the UI cases' to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class HealthMarkTests(Program program)
{
    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Default_L HomePage => Main.Home;

    private string Stash => Path.Combine(program.Folder, "saves", SharedStashSession.SoftcoreFile);

    private static string Impossible => Path.Combine(Program.Corpus, "ui-pad", "fixtures", "pd2_shared_impossible.stash");

    /// <summary>The stash written by another hand, and seen as the window's coming back sees it: read again.</summary>
    private void Write(string from)
    {
        File.Copy(from, Stash, overwrite: true);
        program.On(() =>
        {
            Main.Sync!.Check();
            Assert.Equal("Shared stash reloaded. Saved in game.", Session.Status);
        });
    }

    [CorpusFact]
    public void Two_impossible_items_in_the_shared_stash_are_marked_by_name_and_a_clean_stash_is_not()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            program.On(HomePage.Shown);

            Write(Impossible);
            Assert.True(program.Until(() => HomePage.HasHealthIssue), "no mark");
            program.On(() =>
            {
                Assert.StartsWith("2 illegal items in shared stash: ", HomePage.HealthIssue);
                Assert.Contains("Pus Spitter", HomePage.HealthIssue);
                Assert.Contains("Toothrow", HomePage.HealthIssue);
            });

            // Clean again: no mark at all.
            Write(Path.Combine(program.Folder, "fresh", "saves", SharedStashSession.SoftcoreFile));
            Assert.True(program.Until(() => !HomePage.HasHealthIssue), "the mark stayed on a clean stash");
            program.On(() => Assert.Equal("", HomePage.HealthIssue));
        }
        finally
        {
            program.On(HomePage.Hidden);
        }
    }

    [CorpusFact]
    public void Written_while_the_home_page_is_hidden_the_stash_is_judged_when_the_page_is_shown_again()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            program.On(() =>
            {
                HomePage.Shown();
                HomePage.Hidden();
            });

            Write(Impossible);

            // Behind another page nothing is judged, however long it is left.
            Assert.False(program.Until(() => HomePage.HasHealthIssue, 300), "judged while hidden");

            program.On(HomePage.Shown);
            Assert.True(program.Until(() => HomePage.HasHealthIssue), "no mark on coming back");
            program.On(() => Assert.Contains("Pus Spitter", HomePage.HealthIssue));
        }
        finally
        {
            program.On(HomePage.Hidden);
        }
    }
}
