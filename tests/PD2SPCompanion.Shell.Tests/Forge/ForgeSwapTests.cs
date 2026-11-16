using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Forge;

/// <summary>
/// A staff, through the Forge's own panels: a magic Shillelagh "of Ages" - Indestructible - whose
/// suffix is then picked "of Teleportation" in the affix picker. What the panel stages and the page shows must be the
/// new suffix's alone: no Indestructible row, nothing flagged. The Core sweeps (AffixSweepTests, EditSweepTests) hold
/// the writers; this holds the panel's staging over them.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class ForgeSwapTests(Program program)
{
    private const int OfAges = 404, OfTeleportation = 532;

    private static void Choose(VP_ViewModel_Inv_R panel, int id)
    {
        var row = panel.Affixes.AffixChoices.First(choice => choice.Option.Id == id);
        panel.Affixes.ChooseAffixCommand.Execute(row);
    }

    [CorpusFact]
    public void A_suffix_picked_over_of_Ages_takes_its_Indestructible_with_it()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            bench.Find("shillelagh", "Shillelagh");
            bench.Rarity("Magic");
        });
        Assert.True(bench.Until(() => bench.Forge.HasDraft && bench.Panel.Summary is not null));

        program.On(() =>
        {
            var panel = bench.Panel;
            panel.Affixes.OpenAffixPickerCommand.Execute(null);
            panel.Affixes.AimAffixCommand.Execute("0:suffix");

            Choose(panel, OfAges);
        });

        // The rows are built again a moment after a pick.
        Assert.True(bench.Until(() => bench.Panel.Stats.Any(row => !row.IsDeleted && row.Name == "item_indesctructible")),
            $"of Ages gave no Indestructible row: {program.On(() => string.Join(", ", bench.Panel.Stats.Select(row => row.Name)))}");

        program.On(() =>
        {
            Choose(bench.Panel, OfTeleportation);
            bench.Panel.Affixes.CloseAffixPickerCommand.Execute(null);
        });

        Assert.True(bench.Until(() => bench.Panel.Stats.Any(row => !row.IsDeleted && row.Name == "item_charged_skill")),
            "the Teleport charges never came");
        program.On(() =>
        {
            var panel = bench.Panel;
            Assert.DoesNotContain(panel.Stats, row => !row.IsDeleted && row.Name == "item_indesctructible");
            Assert.DoesNotContain(bench.Lines(), line => line == "Indestructible");
            Assert.Empty(panel.Findings.Select(row => row.Said));
        });
    }
}
