namespace PD2SPCompanion.Shell.Tests.Forge;

/// <summary>
/// Taking a thing off is the brush, in every picker: the automod's and the superior bonus's
/// at the top of their pickers, where a "None" row headed the list; and one beside each recipe a map carries, as
/// beside each staff mod - the row in the list still takes it off too.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class PickerBrushTests(Program program)
{
    [CorpusFact]
    public void The_automods_brush_takes_it_off_and_no_none_row_is_listed()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            // An Amazon's javelin: its group is a choice of several, where most bases have one row and no line.
            bench.Find("maiden javelin", "Maiden Javelin");
            bench.Rarity("Magic");
        });
        Assert.True(bench.Until(() => bench.Forge.HasDraft && bench.Panel.Summary is not null));

        program.On(() =>
        {
            var automod = bench.Panel.Automagic;
            Assert.True(automod.Shows);
            automod.OpenPickerCommand.Execute(null);
            Assert.DoesNotContain(automod.Choices, row => row.Row is null);

            automod.PickCommand.Execute(automod.Choices.First(row => !row.IsAboveLevel));
            Assert.True(automod.CanTakeOff);
            Assert.True(automod.IsPickerOpen);

            automod.TakeOffCommand.Execute(null);
            Assert.False(automod.CanTakeOff);
            Assert.True(automod.HasNone);
            Assert.DoesNotContain(automod.Choices, row => row.IsHeld);

            automod.ClosePickerCommand.Execute(null);
        });
    }

    [CorpusFact]
    public void A_maps_recipe_is_taken_off_by_its_own_brush_the_others_kept()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            bench.Find("demon road", "Demon Road Map");
            bench.Rarity("Magic");
        });
        Assert.True(bench.Until(() => bench.Forge.HasDraft && bench.Panel.Summary is not null));

        program.On(() =>
        {
            var maps = bench.Panel.MapRecipe;
            Assert.True(maps.Shows);
            maps.OpenPickerCommand.Execute(null);

            // Two of a different mark put on: an ear takes an ear's place, so one of each kind.
            var marks = maps.Choices.GroupBy(row => row.Recipe.Marker).Select(group => group.First()).Take(2).ToList();
            Assert.Equal(2, marks.Count);
            foreach (var mark in marks)
                maps.PickCommand.Execute(maps.Choices.First(row => row.Recipe == mark.Recipe));
            Assert.Equal(2, maps.HeldRows.Count);

            var gone = maps.HeldRows[0];
            maps.TakeOffCommand.Execute(gone);
            Assert.Single(maps.HeldRows);
            Assert.DoesNotContain(maps.HeldRows, row => row.Recipe == gone.Recipe);
            Assert.DoesNotContain(maps.Choices, row => row.IsHeld && row.Recipe == gone.Recipe);

            maps.ClosePickerCommand.Execute(null);
        });
    }
}
