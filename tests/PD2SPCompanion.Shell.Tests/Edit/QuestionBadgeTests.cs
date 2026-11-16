using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Edit;

/// <summary>
/// The orange "?" on the Edit page - by a property row, by Defense, by Max durability - says why on hover (it
/// said nothing; the reason was worded and shown nowhere). Asked of what its tooltip binds.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class QuestionBadgeTests(Program program)
{
    private VP_ViewModel_Inv_R Edit => program.EditPage();

    [CorpusFact]
    public void A_property_past_its_range_and_a_durability_past_the_bases_say_why()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            program.Select("Beast Necklace");
            var mana = Edit.Stats.First(row => row.Name == "maxmana");
            Assert.Null(mana.ConcernsText);

            mana.Value = "200";
            Assert.True(mana.IsQuestionable);
            Assert.False(string.IsNullOrWhiteSpace(mana.ConcernsText));

            program.Select("Stone Barb");
            Assert.False(Edit.Basic.DurabilityIsQuestionable);
            Edit.Basic.MaxDurability = "250";
            Assert.True(Edit.Basic.DurabilityIsQuestionable);
            Assert.Contains("250 is above", Edit.Basic.DurabilityNote);
        });
    }
}
