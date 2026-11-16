using D2SLib.Model.Save;

using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Edit;

/// <summary>
/// With every row made its own, one-hand damage, throw damage and two-hand damage: may they come out
/// independently? They do: the three copies of a flat damage are three
/// entries in the save, not a run as Enhanced Damage's halves are, so a bin on one takes that one alone and each
/// keeps the number typed into it.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class FlatDamageCopiesTests(Program program)
{
    private VP_ViewModel_Inv_R Edit => program.EditPage();

    [CorpusFact]
    public void A_flat_damages_copies_are_each_their_own_numbered_and_taken_off()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        Item eye = null!;
        string gone = "";
        program.On(() =>
        {
            program.Panes("Personal Stash");
            eye = program.Select("Shadow Eye", "Personal Stash");
            var copies = Edit.Stats.Where(row => row.Stat.Stat is "mindamage" or "secondary_mindamage" or "item_throw_mindamage").ToList();
            Assert.True(copies.Count >= 2, $"Shadow Eye holds {copies.Count} copies of its minimum damage");
            Assert.All(copies, row => Assert.True(row.HasFellows));

            // A number of its own in one copy.
            copies[0].Value = (int.Parse(copies[0].Value) + 1).ToString();
            Assert.NotEqual(copies[0].Value, copies[1].Value);

            // The last copy off, alone.
            gone = copies[^1].Stat.Stat!;
            copies[^1].DeleteCommand.Execute(null);
            Assert.True(copies[^1].IsDeleted);
            Assert.All(copies.Take(copies.Count - 1), row => Assert.False(row.IsDeleted));
        });

        Assert.True(program.Until(() => eye.StatLists[0].Stats.All(stat => stat.Stat != gone)
                                        && eye.StatLists[0].Stats.Any(stat => stat.Stat is "mindamage" or "secondary_mindamage" or "item_throw_mindamage")),
            $"{gone} is still on the item, or took the others with it");
    }
}
