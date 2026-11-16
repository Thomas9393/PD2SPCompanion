using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Core.Saves;

using PD2SPCompanion.Shell.Tests.Edit;

namespace PD2SPCompanion.Shell.Tests.Forge;

/// <summary>
/// A legacy item - the season before's, kept as it made it (<see cref="ItemSeason"/>) - says so in
/// its lines, and an edit makes it this season's (the edit panel's Write). The item is made legacy by the stamps: the
/// Forge's item at this season's stamp, which is then told to be the season before's.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class ForgeLegacyTests(Program program)
{
    [CorpusFact]
    public void A_legacy_item_says_so_and_an_edit_makes_it_this_seasons()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            bench.Find("matriarchal spear", "Matriarchal Spear");
            bench.Rarity("Magic");
        });
        Assert.True(bench.Until(() => bench.Forge.HasDraft && bench.Panel.Summary is not null));
        program.Settled(bench.Panel);

        var kept = (SeasonReading.CurrentStamp, SeasonReading.PreviousStamp);
        try
        {
            string made = program.On(() => bench.Held.Version);
            SeasonReading.SetStamps("999", made);
            program.On(() =>
            {
                Assert.True(ItemSeason.IsLegacy(bench.Held));
                bench.Panel.Summary!.Rebuilt();
                Assert.Contains(bench.Lines(), line => line.StartsWith("Legacy", StringComparison.Ordinal));
                Assert.Contains("Legacy", bench.Panel.Summary.SummaryFacts);
            });

            program.On(() => bench.Panel.Affixes.RerollAllAffixesNowCommand.Execute(null));
            program.Settled(bench.Panel);

            program.On(() =>
            {
                Assert.Equal("999", bench.Held.Version);
                Assert.False(ItemSeason.IsLegacy(bench.Held));
            });
        }
        finally
        {
            SeasonReading.SetStamps(kept.CurrentStamp, kept.PreviousStamp);
        }
    }

    [CorpusFact]
    public void A_corruption_alone_leaves_a_legacy_item_as_it_was()
    {
        if (!program.Ready(changes: true))
            return;

        var kept = (SeasonReading.CurrentStamp, SeasonReading.PreviousStamp);
        try
        {
            D2SLib.Model.Save.Item item = null!;
            string stamp = "";
            program.On(() =>
            {
                item = program.Select(ItemName);
                stamp = item.Version!;
                SeasonReading.SetStamps("999", stamp);
                Assert.True(ItemSeason.IsLegacy(item));

                var corruption = program.EditPage().Corruption;
                corruption.OpenPickerCommand.Execute(null);
                corruption.RollCommand.Execute(null);
                corruption.ClosePickerCommand.Execute(null);
            });

            // As LegacyTest's five read after the game's cube: the season's stamp kept.
            Assert.True(program.Until(() => Corruptions.IsCorrupted(item), 10000), "the corruption was not kept");
            program.On(() =>
            {
                Assert.Equal(stamp, item.Version);
                Assert.True(ItemSeason.IsLegacy(item));
            });
        }
        finally
        {
            SeasonReading.SetStamps(kept.CurrentStamp, kept.PreviousStamp);
        }
    }

    [CorpusFact]
    public void The_cube_draws_ormus_robes_skill_again_on_both_lines()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        program.On(() => bench.Find("ormus", "Ormus' Robes"));
        Assert.True(bench.Until(() => bench.Forge.HasDraft && bench.Panel.Summary is not null));
        program.Settled(bench.Panel);

        (int Grant, int Cast, int Chance, int Level) Lines() => program.On(() =>
        {
            var stats = bench.Held.StatLists.SelectMany(list => list.Stats).ToList();
            var grant = stats.Single(stat => stat.Stat == "item_singleskill");
            var cast = stats.Single(stat => stat.Stat == "item_skilloncast");
            return (grant.Param!.Value, cast.SkillId!.Value, cast.Value, cast.SkillLevel!.Value);
        });

        var seen = new HashSet<int>();
        for (int roll = 0; roll < 6; roll++)
        {
            program.On(() => bench.Panel.RerollItemCommand.Execute(null));
            program.Settled(bench.Panel);

            var now = Lines();
            Assert.Equal(now.Grant, now.Cast);
            Assert.Contains(now.Grant, RandomSkillCast.Pool);
            Assert.Equal(RandomSkillCast.Chance(now.Grant, 12), now.Chance);
            Assert.Equal(RandomSkillCast.Level(now.Grant, 26), now.Level);
            seen.Add(now.Grant);
        }

        Assert.True(seen.Count > 1, "the skill never moved");
    }

    private const string ItemName = "Beast Necklace";
}
