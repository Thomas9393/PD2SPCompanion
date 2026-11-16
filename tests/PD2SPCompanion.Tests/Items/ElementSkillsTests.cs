using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// An elemental skill bonus's words follow its grant (ElementSkills): the element chosen,
/// the number moved, the grant taken off - and a forged one from before that day, its grant naming no element,
/// told its element by the words beside it.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class ElementSkillsTests(Game game)
{
    private static ItemStat Grant(Item item) => item.StatLists[0].Stats.Single(stat => stat.Stat == ElementSkills.Grant);

    private static string[] Labels(Item item) =>
        [.. item.StatLists[0].Stats.Where(stat => ElementSkills.IsLabel(stat.Stat)).Select(stat => $"{stat.Stat} {stat.Value}")];

    [GameFact]
    public void The_words_follow_the_grant()
    {
        if (!game.Available)
            return;

        // The five, from properties.txt.
        Assert.Equal(
            ["item_elemskill_fire", "item_elemskill_lightning", "item_elemskill_magic", "item_elemskill_cold", "item_elemskill_poison"],
            Enumerable.Range(1, 5).Select(ElementSkills.LabelOf));

        var garb = ItemForge.Unique("Heavenly Garb", "105").Item;
        Assert.Equal(3, Grant(garb).Param);
        Assert.Equal(["item_elemskill_magic 1"], Labels(garb));
        Assert.Empty(ElementSkills.Apart(garb));
        Assert.Equal(0, ElementSkills.Settle(garb));

        // As an older forge wrote it: no element. Said, and told by its words.
        Grant(garb).Param = 0;
        Assert.Contains(ElementSkills.Apart(garb), gap => !gap.Named);
        Assert.Contains(Legitimacy.Check(garb), found => found.Said == "Names no element, so it grants nothing.");
        ElementSkills.Settle(garb);
        Assert.Equal(3, Grant(garb).Param);
        Assert.Empty(ElementSkills.Apart(garb));

        // Another element chosen: the words are that element's.
        Grant(garb).Param = 4;
        ElementSkills.Settle(garb);
        Assert.Equal(["item_elemskill_cold 1"], Labels(garb));

        // Another number.
        Grant(garb).Value = 3;
        Assert.Contains(Legitimacy.Check(garb), found => found.Said == "Shows +1 and grants +3.");
        ElementSkills.Settle(garb);
        Assert.Equal(["item_elemskill_cold 3"], Labels(garb));

        // The grant taken off: the words go with it.
        garb.StatLists[0].Stats.Remove(Grant(garb));
        ElementSkills.Settle(garb);
        Assert.Empty(Labels(garb));
        Assert.Empty(ElementSkills.Apart(garb));
    }
}
