using D2SLib.Model.Save;

using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Edit;

/// <summary>
/// The link button on rows a corruption writes together: a corruption that gained all res lacked it, and one
/// changed from all res to a single res kept it: what links the rows was asked
/// once per item, and a corruption gained or changed while the item was shown is kept into the same item - so the
/// rows showed the item as it was when it was picked. Asked again at every build of the rows now.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class CorruptionLinkTests(Program program)
{
    private VP_ViewModel_Inv_R Edit => program.EditPage();
    private CorruptionEditor Corruption => Edit.Corruption;

    /// <summary>All resistances (cubemain's res-all), and fire resistance alone.</summary>
    private const int AllRes = 51, FireRes = 36;

    private static readonly string[] Resists = ["fireresist", "coldresist", "lightresist", "poisonresist"];

    private bool Linked(string stat) => Edit.Stats.Any(row => !row.IsDeleted && row.Stat.Stat == stat && row.HasFellows);

    private void Pick(int id) => program.On(() =>
    {
        Corruption.OpenPickerCommand.Execute(null);
        var row = Corruption.Choices.SelectMany(tier => tier.Options).FirstOrDefault(option => option.Option.Id == id);
        Assert.True(row is not null, $"corruption {id} is not offered here");
        Corruption.PickCommand.Execute(row!);
        Corruption.ClosePickerCommand.Execute(null);
    });

    [CorpusFact]
    public void All_res_gained_links_the_four_rows_and_a_single_res_after_it_does_not()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        Item item = null!;
        program.On(() =>
        {
            item = program.Select("Beast Necklace");
            Assert.All(Resists, stat => Assert.False(Linked(stat), $"{stat} linked before any corruption"));
        });

        Pick(AllRes);
        Assert.True(program.Until(() => item.Every(Corruptions.IdStat) is [AllRes]), "the all res corruption was not kept");
        program.Settled();
        program.On(() => Assert.All(Resists, stat => Assert.True(Linked(stat), $"{stat} not linked after all res")));

        Pick(FireRes);
        Assert.True(program.Until(() => item.Every(Corruptions.IdStat) is [FireRes]), "the fire res corruption was not kept");
        program.Settled();
        program.On(() => Assert.False(Linked("fireresist"), "fire res still linked after the single res took all res' place"));
    }
}
