namespace PD2SPCompanion.Shell.Tests.Forge;

/// <summary>
/// The Caster recipe, then the Apprentice suffix from the affix picker: the item landed at a weird value and was
/// flagged. The Caster craft gives an amulet its Faster Cast Rate; "of the Apprentice" adds 10.
/// The affix writer rebuilt the line from the rolled sources alone and the craft's part went - 10, where 15-20 is
/// what the two give. The craft's part is kept, going on and coming off.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class CraftAffixShareTests(Program program)
{
    [CorpusFact]
    public void An_affix_on_a_crafted_amulet_adds_to_the_crafts_part_and_takes_only_its_own_off()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            bench.Find("amulet", "Amulet");
            bench.Rarity("Crafted");
        });
        Assert.True(bench.Until(() => bench.Forge.HasDraft && bench.Panel.Summary is not null));
        program.On(() =>
        {
            var craft = bench.Panel.Crafting;
            if (!craft.Choices.Any(choice => choice.IsHeld && choice.Family == "Caster"))
            {
                craft.OpenPickerCommand.Execute(null);
                craft.PickCommand.Execute(craft.Choices.First(choice => choice.Family == "Caster"));
                craft.ClosePickerCommand.Execute(null);
            }
        });
        bench.Settled();

        int FasterCast() => bench.Panel.Stats.FirstOrDefault(row => row.Name == "item_fastercastrate") is { } row ? int.Parse(row.Value) : 0;
        int crafts = 0;

        program.On(() =>
        {
            // Every suffix emptied first: the Forge rolls the amulet's affixes, and one may give Faster Cast Rate
            // too, or hold the Apprentice's group - the line is then the craft's alone (the test failed one run in
            // several on that).
            var affixes = bench.Panel.Affixes;
            affixes.OpenAffixPickerCommand.Execute(null);
            foreach (var pair in affixes.StagedPairs.ToList())
            {
                affixes.AimAffixCommand.Execute($"{pair.Slot}:suffix");
                affixes.ClearAffixCommand.Execute(null);
            }

            crafts = FasterCast();
            Assert.InRange(crafts, 1, 20);

            affixes.AimAffixCommand.Execute("0:suffix");
            affixes.AffixSearch = "Apprentice";
            affixes.ChooseAffixCommand.Execute(affixes.AffixChoices.First(row => row.Name == "of the Apprentice"));

            // The Apprentice's 10 on top of the craft's part.
            Assert.Equal(crafts + 10, FasterCast());

            // And off again: the craft's part alone.
            affixes.ClearAffixCommand.Execute(null);
            Assert.Equal(crafts, FasterCast());

            affixes.ChooseAffixCommand.Execute(affixes.AffixChoices.First(row => row.Name == "of the Apprentice"));
            affixes.CloseAffixPickerCommand.Execute(null);
        });
        bench.Settled();
        program.On(() =>
        {
            Assert.Equal(crafts + 10, FasterCast());
            // Nothing said of the line: the item is the Forge's random roll, emptied of its suffixes, and may be said
            // something of otherwise (one run in many) - the Faster Cast Rate is what is asked here.
            Assert.DoesNotContain(bench.Panel.Findings, row => row.Said.Contains("Faster Cast Rate"));
        });
    }
}
