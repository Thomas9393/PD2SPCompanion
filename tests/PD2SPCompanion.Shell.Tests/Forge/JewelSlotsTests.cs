using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Forge;

/// <summary>
/// A rare jewel on the Forge: the delete button in the chosen affix part closed up the slots when the pair was
/// empty, with no way to add it back within the panel short of reopening it.
/// A pair emptied through the chosen affix's Empty slot stays on the list, empty, where it was - each
/// act writes at once now, and nothing moves for it; the - takes it off, and the + is told it may open one again.
/// Asked as the button asks - by what the panel says has changed.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class JewelSlotsTests(Program program)
{
    [CorpusFact]
    public void A_pair_emptied_by_its_affixes_Empty_slot_stays_and_the_minus_takes_it_off()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            bench.Find("jewel", "Jewel");
            bench.Rarity("Rare");
        });
        Assert.True(bench.Until(() => bench.Forge.HasDraft && bench.Panel.Summary is not null));

        program.On(() =>
        {
            AffixEditor Affixes() => bench.Panel.Affixes;

            // A jewel made with three pairs: the slots full, no + to press.
            for (int made = 0; made < 60 && Affixes().StagedPairs.Count < 3; made++)
            {
                if (!bench.Forge.ResetCommand.CanExecute(null))
                    bench.Panel.RerollItemCommand.Execute(null);
                bench.Forge.ResetCommand.Execute(null);
            }

            var affixes = Affixes();
            Assert.Equal(3, affixes.StagedPairs.Count);
            affixes.OpenAffixPickerCommand.Execute(null);
            Assert.False(affixes.CanAddSlot);

            bool told = false;
            affixes.PropertyChanged += (_, changed) => told |= changed.PropertyName == nameof(AffixEditor.CanAddSlot);

            // The last pair's two sides emptied, each by the chosen affix's Empty slot.
            foreach (string side in new[] { "prefix", "suffix" })
            {
                affixes.AimAffixCommand.Execute($"2:{side}");
                affixes.ClearAffixCommand.Execute(null);
            }

            Assert.Equal(3, affixes.StagedPairs.Count);
            Assert.Equal("", affixes.StagedPairs[2].PrefixName);
            Assert.Equal("", affixes.StagedPairs[2].SuffixName);
            Assert.True(affixes.IsAffixPickerOpen);

            affixes.RemoveSlotCommand.Execute(null);
            Assert.Equal(2, affixes.StagedPairs.Count);
            Assert.True(told, "the + was not told the slots changed");
            Assert.True(affixes.CanAddSlot);

            // The + opens an empty slot and aims the list at it.
            affixes.AddSlotCommand.Execute(null);
            Assert.Equal(3, affixes.StagedPairs.Count);
            Assert.True(affixes.StagedPairs[2].PrefixAimed);

            affixes.CloseAffixPickerCommand.Execute(null);
        });
    }
}
