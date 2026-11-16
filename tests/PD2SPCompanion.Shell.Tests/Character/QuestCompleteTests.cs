using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Tests.Home;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Character;

/// <summary>
/// The Quests card completes with one click and takes back what this session completed (no marks,
/// no confirm): what completing does not give is said on the button before and on the
/// strip after; a revert leaves the file byte for byte as it was; and a quest whose points have been spent
/// is not reverted, and says why, its revert still offered.
/// <para>
/// What the card decides, asked through each row's own commands and read back from the strip and the file -
/// on "Quester", a questless level 10 Sorceress made for each test, in Free. That a row's two squares are
/// drawn on its line and their clicks arrive is the UI case's to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class QuestCompleteTests(Program program)
{
    private CharacterSession Session => program.Main.Session;
    private VP_ViewModel_Char_L Page => program.Get<VP_ViewModel_Char_L>();
    private VP_ViewModel_Char_R Skills => program.Get<VP_ViewModel_Char_R>();

    /// <summary>A quest's row as it stands now: the rows are made anew after every completion.</summary>
    private QuestRow Row(string quest) => Page.Quests.Rows.First(row => row.Name == quest);

    private byte[] OnDisk => File.ReadAllBytes(program.SaveOf("Quester"));

    private void Quester()
    {
        program.On(() => Made.Character(program, "Quester", "Sorceress", 10));
        Made.Open(program, "Quester");
        program.On(() =>
        {
            Page.Shown();
            Skills.Shown();
        });
    }

    private void Leave()
    {
        program.On(() =>
        {
            Page.Hidden();
            Skills.Hidden();
        });
        Made.Gone(program, "Quester");
    }

    [CorpusFact]
    public void Completing_says_what_is_not_given_on_the_button_before_and_on_the_strip_after()
    {
        if (!program.Ready(changes: true))
            return;

        using var mode = Made.Mode(program, "Free");
        try
        {
            Quester();
            program.On(() =>
            {
                var cain = Row("The Search for Cain");
                Assert.Equal("Not started", cain.StateText);
                Assert.True(cain.CanComplete);
                Assert.False(cain.CanRevert);
                Assert.StartsWith("Complete", cain.Hint);
                Assert.Contains("Not given: Akara's ring.", cain.Hint);

                cain.CompleteCommand.Execute(null);

                Assert.Equal("The Search for Cain completed. Not given: Akara's ring.", Session.Status);
                Assert.Equal("Done", Row("The Search for Cain").StateText);
                Assert.True(Row("The Search for Cain").CanRevert);
                Assert.False(Row("The Search for Cain").CanComplete);
            });
        }
        finally
        {
            Leave();
        }
    }

    [CorpusFact]
    public void A_quest_completed_this_session_is_reverted_and_the_file_is_byte_for_byte_as_it_was()
    {
        if (!program.Ready(changes: true))
            return;

        using var mode = Made.Mode(program, "Free");
        try
        {
            Quester();
            program.On(() =>
            {
                byte[] original = OnDisk;

                Row("The Search for Cain").CompleteCommand.Execute(null);
                Assert.True(program.Get<AutoSave>().Flush());
                Assert.NotEqual(original, OnDisk);

                Row("The Search for Cain").RevertCommand.Execute(null);
                Assert.Equal("The Search for Cain reverted.", Session.Status);
                Assert.False(Row("The Search for Cain").CanRevert);
                Assert.True(Row("The Search for Cain").CanComplete);

                Assert.True(program.Get<AutoSave>().Flush());
                Assert.Equal(original, OnDisk);
            });
        }
        finally
        {
            Leave();
        }
    }

    [CorpusFact]
    public void With_its_skill_point_spent_a_quest_is_not_reverted_and_says_why()
    {
        if (!program.Ready(changes: true))
            return;

        using var mode = Made.Mode(program, "Free");
        try
        {
            Quester();
            program.On(() =>
            {
                int points = Skills.Unspent;
                Row("Den of Evil").CompleteCommand.Execute(null);
                Assert.StartsWith("Den of Evil completed.", Session.Status);
                Assert.Equal(points + 1, Skills.Unspent);

                // Its point gone: the skills' points to spend typed down to none.
                Skills.UnspentText = "0";
                Skills.CommitUnspent();
                Assert.Equal(0, Skills.Unspent);

                Row("Den of Evil").RevertCommand.Execute(null);
                Assert.Equal("Den of Evil: Points spent. Require respec.", Session.Status);
                Assert.True(Row("Den of Evil").CanRevert);
                Assert.Equal("Done", Row("Den of Evil").StateText);
            });
        }
        finally
        {
            Leave();
        }
    }
}
