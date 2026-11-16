namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// A forged "+N to Fire Skills" grants what it says. The property writes two stats: <c>item_elemskill</c>, whose
/// parameter is the element and which gives the levels, and a partner per element that only prints the line
/// (StatCatalogue). Written with parameter 0, the first shows the line and grants nothing - a forged
/// Heavenly Garb then reads "+1 to (none) Skills" (a test of the skill tooltip).
/// </summary>
[Collection(nameof(GameTables))]
public sealed class ForgedElementTests(Game game)
{
    private const string Stamp = "103";

    // properties.txt's val1 for fireskill, ltngskill, magskill, coldskill and poisskill.
    private static readonly Dictionary<string, int> Labels = new()
    {
        ["item_elemskill_fire"] = 1,
        ["item_elemskill_lightning"] = 2,
        ["item_elemskill_magic"] = 3,
        ["item_elemskill_cold"] = 4,
        ["item_elemskill_poison"] = 5,
    };

    [GameFact]
    public void A_forged_elemental_skill_bonus_names_its_element()
    {
        if (!game.Available)
            return;

        var wrong = new List<string>();
        int seen = 0;

        void Look(D2SLib.Model.Save.Item item)
        {
            var stats = ItemStats.Every(item).ToList();
            foreach (var label in stats.Where(stat => Labels.ContainsKey(stat.Stat ?? "")))
            {
                seen++;
                int element = Labels[label.Stat!];
                if (!stats.Any(stat => stat.Stat == "item_elemskill" && stat.Param == element && stat.Value == label.Value))
                {
                    wrong.Add($"{ItemPresenter.Describe(item).Name}: {label.Stat} {label.Value} without item_elemskill {label.Value} "
                              + $"of element {element}; holds {string.Join(", ", stats.Where(stat => stat.Stat == "item_elemskill").Select(stat => $"{stat.Value} of {stat.Param}"))}");
                }
            }
        }

        foreach (int row in ItemForge.EnabledUniques())
            Look(ItemForge.UniqueAt(row, Stamp, RollAs.Maximum).Item);

        foreach (int row in ItemForge.SetItems())
            Look(ItemForge.SetItemAt(row, Stamp, RollAs.Maximum).Item);

        // Heavenly Garb, Moonfall, Magefist's four, Frostwind, Skyfall and the rest: about thirty in S13.
        Assert.True(seen > 20, $"only {seen} elemental bonuses forged: the tables were not read");
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }
}
