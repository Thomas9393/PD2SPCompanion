using System.Diagnostics;

using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Tests.Edit;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Window;

/// <summary>
/// The message strips, as the log behind each decides:
/// what is said is shown bright and at once, a refusal is a caution, the last ten are kept for the history,
/// a message dims after five seconds and the strip is put away after fifteen with nothing said
/// - and is back the moment something is. And that an action of the item panel is said on the
/// right page's strip, not the left's.
/// <para>
/// That an idle strip is drawn away and fades, that the pointer over where it lies brings it back with its
/// history, and that the right strip is drawn under the right page are the UI cases' to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class MessageStripTests(Program program)
{
    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;

    [CorpusFact]
    public void Something_said_is_shown_bright_and_at_once_and_empty_text_says_nothing()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var strip = new MessageLog("test strip");
            Assert.False(strip.HasMessage);
            Assert.Equal("", strip.Text);
            Assert.Equal("", strip.History);

            strip.Post("   ");
            strip.Post(null);
            Assert.False(strip.HasMessage);

            strip.Post(" Tome of Town Portal moved. ");
            Assert.True(strip.HasMessage);
            Assert.Equal("Tome of Town Portal moved.", strip.Text);
            Assert.True(strip.IsFresh);
            Assert.False(strip.IsIdle);
            Assert.False(strip.IsCaution);
        });
    }

    [CorpusTheory]
    [InlineData("Require vault page open.", true)]
    [InlineData("Refused: last season's format.", true)]
    [InlineData("No free space in the inventory.", true)]
    [InlineData("Cannot be undone.", true)]
    [InlineData("Not copied: clipboard busy.", true)]
    [InlineData("Folder not found.", true)]
    [InlineData("Version copied.", false)]
    [InlineData("Laying of Hands taken off.", false)]
    public void A_refusal_is_told_by_its_wording_and_shown_as_a_caution(string said, bool caution)
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var strip = new MessageLog("test strip");
            strip.Post(said);
            Assert.Equal(caution, strip.IsCaution);
        });
    }

    [CorpusFact]
    public void The_history_keeps_the_last_ten_newest_first_each_with_its_time_and_a_message_said_twice_is_two()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var strip = new MessageLog("test strip");
            strip.Post("Saved.");
            var first = strip.Latest;
            strip.Post("Saved.");
            Assert.NotSame(first, strip.Latest);
            Assert.Equal(2, strip.History.Split(Environment.NewLine).Length);

            for (int said = 1; said <= 12; said++)
                strip.Post($"Message {said}.");

            string[] lines = strip.History.Split(Environment.NewLine);
            Assert.Equal(MessageLog.Kept, lines.Length);
            Assert.Equal(10, lines.Length);
            Assert.All(lines, line => Assert.Matches(@"^\d\d:\d\d:\d\d  Message \d+\.$", line));
            Assert.EndsWith("  Message 12.", lines[0]);
            Assert.EndsWith("  Message 3.", lines[^1]);
        });
    }

    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void Nothing_said_for_a_while_the_message_dims_then_the_strip_is_put_away_and_something_said_brings_it_back()
    {
        // PD2SPC_STRIP_IDLE shortens the wait for the UI case; the times looked for here are the program's own.
        if (!program.Ready() || Environment.GetEnvironmentVariable("PD2SPC_STRIP_IDLE") is { Length: > 0 })
            return;

        var strip = program.On(() => new MessageLog("test strip"));
        var clock = Stopwatch.StartNew();
        program.On(() => strip.Post("Opened PhySpearZon."));

        // Bright for five seconds, then dimmed - and still there.
        Assert.True(program.Until(() => !strip.IsFresh, 9000), "the message never dimmed");
        Assert.InRange(clock.ElapsedMilliseconds, 4500, 9000);
        program.On(() =>
        {
            Assert.False(strip.IsIdle);
            Assert.True(strip.HasMessage);
        });

        // Fifteen seconds with nothing said: put away, its words and history kept for the pointer.
        Assert.True(program.Until(() => strip.IsIdle, 15000), "the strip was never put away");
        Assert.InRange(clock.ElapsedMilliseconds, 14500, 21000);
        program.On(() =>
        {
            Assert.True(strip.HasMessage);
            Assert.Equal("Opened PhySpearZon.", strip.Text);
            Assert.EndsWith("  Opened PhySpearZon.", strip.History);

            strip.Post("Delete Tome of Town Portal?");
            Assert.False(strip.IsIdle);
            Assert.True(strip.IsFresh);
        });
    }

    [CorpusFact]
    public void What_a_page_says_through_the_session_goes_to_the_left_strip()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var right = Session.RightMessages.Latest;

            Session.Status = "Require vault page open.";

            Assert.Equal("Require vault page open.", Session.Messages.Text);
            Assert.True(Session.Messages.IsCaution);
            Assert.False(Session.Messages.IsIdle);
            Assert.Same(right, Session.RightMessages.Latest);
        });
    }

    [CorpusFact]
    public void The_item_panels_reroll_is_said_on_the_right_strip_and_not_on_the_left()
    {
        if (!program.Ready(changes: true))
            return;

        // Free: the worn amulet is a rare, which the cube takes only there (the game's takes uniques and sets).
        using var free = program.InMode("Free");
        program.On(() =>
        {
            var page = Main.Inventory!;
            var panel = program.Get<VP_ViewModel_Inv_R>();
            var amulet = Session.Equipped.First(slot => slot.Slot.Location == ItemLocation.Neck && !slot.IsSwap).Item!;
            Assert.Equal("Beast Necklace", amulet.Name);

            // As the view does: the worn amulet clicked, then the Demonic Cube ("Reroll all stats").
            page.ClickWornCommand.Execute(amulet);
            Assert.True(panel.CanReroll);

            panel.RerollItemCommand.Execute(null);

            Assert.Matches(@"^Beast Necklace rerolled: (no change|\d+ changed)\.$", Session.RightMessages.Text);
            Assert.True(Session.RightMessages.IsFresh);
            Assert.False(Session.RightMessages.IsIdle);
            Assert.DoesNotContain("rerolled", Session.Messages.Text);
        });
    }
}
