using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// The Demonic Cube on a runeword in a superior base, a Faith rolled to
/// 600+ ED (double stacking, the superior part rolling the range as the runeword's ED does). The cube rolled one
/// number per stat over every source and wrote it into every line of that stat: the superior's Enhanced Damage and
/// the word's, two lines, each took their sum. Now each list from its own sources (AffixRanges.RollScope).
/// </summary>
[Collection(nameof(GameTables))]
public sealed class RunewordRerollTests(Game game)
{
    [GameFact]
    public void A_superior_runewords_two_Enhanced_Damage_lines_each_roll_in_their_own_range()
    {
        if (!game.Available)
            return;

        // A real Faith: the word's properties in a list of their own, after the base's (ApplyRuneword).
        var word = Runewords.All.First(each => Runewords.GameName(each) == "Faith");
        var shown = ItemForge.RunewordShown(word, "").Item;
        var faith = ItemForge.PlainBase(shown.Code.Trim(), word.Runes.Length, "103").Item;
        var words = new ItemStatList();
        words.Stats.AddRange(shown.StatLists[0].Stats.Select(stat => new ItemStat
        {
            Id = stat.Id, Stat = stat.Stat, Value = stat.Value, Param = stat.Param,
            SkillId = stat.SkillId, SkillLevel = stat.SkillLevel, MaxCharges = stat.MaxCharges,
        }));
        ItemForge.ApplyRuneword(faith, word, words);
        Assert.Equal(2, faith.StatLists.Count);

        // Made superior with an Enhanced Damage bonus, in the item's own list.
        faith.Quality = ItemQuality.Superior;
        var bonus = QualityItems.RowsFor(faith).First(row => row.Mods.Any(mod => mod.Code == "dmg%"));
        QualityItems.Put(faith, bonus);

        var ownRange = AffixRanges.OwnOf(faith)["item_maxdamage_percent"];
        var wordRange = AffixRanges.AddedTo(faith)["item_maxdamage_percent"];
        Assert.True(ownRange.Max < wordRange.Min, $"the superior's range {ownRange.Min}-{ownRange.Max} and the word's {wordRange.Min}-{wordRange.Max} meet");

        var random = new Random(3);
        for (int roll = 0; roll < 60; roll++)
        {
            DemonicCube.Reroll(faith, random);

            int own = faith.StatLists[0].Stats.Single(stat => stat.Stat == "item_maxdamage_percent").Value;
            int added = faith.StatLists[^1].Stats.Single(stat => stat.Stat == "item_maxdamage_percent").Value;
            Assert.InRange(own, ownRange.Min, ownRange.Max);
            Assert.InRange(added, wordRange.Min, wordRange.Max);
        }
    }
}
