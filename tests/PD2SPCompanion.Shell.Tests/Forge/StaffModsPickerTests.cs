using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Forge;

/// <summary>
/// The staffmods picker: the most an item carries said beside the title, "max 3" - a ceiling,
/// not a count to fill, as the game does not always spawn 3; each held with its own brush; the rest greyed and
/// said once three are held, where a click used to answer with a message.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class StaffModsPickerTests(Program program)
{
    [CorpusFact]
    public void Three_held_grey_the_rest_and_a_brush_takes_one_off()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        program.On(() => bench.Find("wand", "Wand"));
        Assert.True(bench.Until(() => bench.Forge.HasDraft && bench.Panel.Summary is not null));

        program.On(() =>
        {
            var staff = bench.Panel.StaffSkills;
            Assert.True(staff.Shows);
            Assert.Equal("max 3", staff.Limits);

            staff.OpenPickerCommand.Execute(null);
            staff.ClearCommand.Execute(null);
            Assert.Empty(staff.HeldRows);

            // Three put on: the rest greyed, each saying why.
            foreach (var choice in staff.Choices.Where(row => !row.IsAboveLevel).Take(3).ToList())
                staff.ToggleCommand.Execute(staff.Choices.First(row => row.Skill.Id == choice.Skill.Id));
            Assert.Equal(3, staff.HeldRows.Count);
            Assert.All(staff.Choices.Where(row => !row.IsHeld), row =>
            {
                Assert.False(row.CanPick);
                Assert.Equal("max 3", row.Said);
            });
            Assert.All(staff.Choices.Where(row => row.IsHeld), row => Assert.True(row.CanPick));

            // One taken off by its brush: room again, the others kept.
            var gone = staff.HeldRows[1];
            staff.TakeOffCommand.Execute(gone);
            Assert.Equal(2, staff.HeldRows.Count);
            Assert.DoesNotContain(staff.HeldRows, row => row.Skill == gone.Skill);
            Assert.All(staff.Choices, row => Assert.True(row.CanPick));

            staff.ClosePickerCommand.Execute(null);
        });
    }
}
