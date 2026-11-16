using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Characters;

/// <summary>
/// Refit's target counts both weapon sets, I and II.
/// <see cref="Requirements.Least"/> is what Refit moves points
/// to: the strength and dexterity every worn item needs, of the pair in hand and of the pair put away - a
/// weapon on the switch is unusable the moment it is swapped to if the points do not reach it.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class RefitBothSetsTests(Game game)
{
    /// <summary>The corpus's own character, read and never written: the test changes what it holds in memory alone.</summary>
    private static D2S Character()
    {
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));

        // Neither pair in the way of what the test puts there.
        save.PlayerItemList.Items.RemoveAll(item => item.Mode == ItemMode.Equipped
                                                    && item.Location is ItemLocation.SwapRight or ItemLocation.SwapLeft);
        save.PlayerItemList.Count = (ushort)save.PlayerItemList.Items.Count;
        return save;
    }

    private static Item Worn(D2S save, string code, ItemLocation where)
    {
        var item = ItemForge.PlainBase(code, 0, "103").Item;
        item.Mode = ItemMode.Equipped;
        item.Location = where;
        save.PlayerItemList.Items.Add(item);
        save.PlayerItemList.Count = (ushort)save.PlayerItemList.Items.Count;
        return item;
    }

    [CorpusFact]
    public void A_weapon_on_the_switch_raises_what_Refit_aims_for()
    {
        if (!game.Available)
            return;

        {
            var save = Character();
            string? classCode = CharacterClasses.CodeOf(save.ClassId);
            var before = Requirements.Least(save, countGear: false);

            // A Thunder Maul, the heaviest of the mauls, on the pair put away.
            var maul = Worn(save, "7gm", ItemLocation.SwapRight);
            int needs = Requirements.Of(maul, classCode).Strength;
            Assert.True(needs > before.Strength, $"the maul asks {needs}, no more than the rest's {before.Strength}: the test proves nothing");

            // Refit's target reaches it: the points alone (Ctrl + click), and - the plain click - less what the
            // rest of that pair's gear gives, the worn pieces and the charms.
            Assert.Equal(needs, Requirements.Least(save, countGear: false).Strength);
            Assert.Equal(before.Dexterity, Requirements.Least(save, countGear: false).Dexterity);

            int given = Requirements.TotalOf(Requirements.Counted(save, 1).Where(item => !ReferenceEquals(item, maul)), save.Level).Strength;
            Assert.True(given > 0, "the character's gear gives no strength: the gear's part is not shown");
            Assert.Equal(needs - given, Requirements.Least(save, countGear: true).Strength);

            // It is the put-away pair's: judged as in hand, the pair in hand does not hold it; the other does.
            Assert.DoesNotContain(Requirements.Judge(save, 0, 999, 999), judged => ReferenceEquals(judged.Item, maul));
            Assert.Contains(Requirements.Judge(save, 1, 999, 999), judged => ReferenceEquals(judged.Item, maul));

            // Taken off again, the target is what it was.
            save.PlayerItemList.Items.Remove(maul);
            Assert.Equal(before, Requirements.Least(save, countGear: false));
        }
    }

    /// <summary>
    /// Refit's target follows the pieces as they are put on, one at a time, a piece by piece
    /// building path. A heavy armour and a heavy helm that each give strength - a
    /// helm, worn with either weapon set: counted all at once, each was helped on by the other and the target came
    /// out at what neither can be put on with. The helm first, then the armour on the helm's strength, is the
    /// least that does.
    /// </summary>
    [CorpusFact]
    public void Two_pieces_that_need_each_others_strength_are_put_on_one_after_the_other()
    {
        if (!game.Available)
            return;

        var save = Character();
        save.PlayerItemList.Items.RemoveAll(item => item.Mode == ItemMode.Equipped);
        save.PlayerItemList.Count = (ushort)save.PlayerItemList.Items.Count;
        string? classCode = CharacterClasses.CodeOf(save.ClassId);

        static void Gives(Item item, int strength) =>
            ItemStats.Home(item).Stats.Add(new ItemStat { Id = StatCatalogue.IdOf("strength"), Stat = "strength", Value = strength });

        // A Sacred Armor and a Spired Helm, each giving 100 strength.
        var armor = Worn(save, "uar", ItemLocation.Torso);
        var helm = Worn(save, "uhm", ItemLocation.Head);
        Gives(armor, 100);
        Gives(helm, 100);
        int armorNeeds = Requirements.Of(armor, classCode).Strength, helmNeeds = Requirements.Of(helm, classCode).Strength;
        int charms = Requirements.TotalOf(Requirements.Counted(save, 0).Where(Requirements.IsCharm), save.Level).Strength;

        // The helm on the base alone, the armour on the base and the helm's 100: the most of the two. Each helped by
        // the other at once, it was the most of the two less a hundred - neither goes on with that.
        int least = Math.Max(helmNeeds - charms, armorNeeds - charms - 100);
        int atOnce = Math.Max(helmNeeds, armorNeeds) - charms - 100;
        Assert.True(least > atOnce, "the two do not lean on each other: the test proves nothing");
        Assert.Equal(least, Requirements.Least(save, countGear: true).Strength);
    }

    /// <summary>
    /// A base that already lets every piece go on costs nothing: the armour first, needing
    /// strength and giving dexterity, then the bow on it. The pair of fewest points in all is another - less strength,
    /// more dexterity - and aiming at it moved points for nothing.
    /// </summary>
    [CorpusFact]
    public void A_base_that_already_fits_asks_for_no_point()
    {
        if (!game.Available)
            return;

        var save = Character();
        save.PlayerItemList.Items.RemoveAll(item => item.Mode == ItemMode.Equipped);
        save.PlayerItemList.Count = (ushort)save.PlayerItemList.Items.Count;
        string? classCode = CharacterClasses.CodeOf(save.ClassId);

        var armor = Worn(save, "uar", ItemLocation.Torso);
        var bow = Worn(save, "am6", ItemLocation.RightHand);
        var bowOnSwitch = Worn(save, "am6", ItemLocation.SwapRight);
        var armorNeeds = Requirements.Of(armor, classCode);
        var bowNeeds = Requirements.Of(bow, classCode);
        Assert.True(bowNeeds.Dexterity > 20 && armorNeeds.Strength >= bowNeeds.Strength + 50,
                    $"the two do not lean on each other, the armor {armorNeeds} and the bow {bowNeeds}: the test proves nothing");

        ItemStats.Home(armor).Stats.Add(new ItemStat { Id = StatCatalogue.IdOf("dexterity"), Stat = "dexterity", Value = 20 });
        ItemStats.Home(bow).Stats.Add(new ItemStat { Id = StatCatalogue.IdOf("strength"), Stat = "strength", Value = 50 });
        ItemStats.Home(bowOnSwitch).Stats.Add(new ItemStat { Id = StatCatalogue.IdOf("strength"), Stat = "strength", Value = 50 });

        var charms = Requirements.TotalOf(Requirements.Counted(save, 0).Where(Requirements.IsCharm), save.Level);
        (int Strength, int Dexterity) held = (armorNeeds.Strength - charms.Strength, bowNeeds.Dexterity - 20 - charms.Dexterity);

        // Fewest in all is more dexterity than the base has, and that was the target.
        Assert.True(Requirements.Least(save, countGear: true).Dexterity > held.Dexterity);

        var aimed = Requirements.Least(save, countGear: true, from: held);
        Assert.True(aimed.Strength <= held.Strength && aimed.Dexterity <= held.Dexterity, $"aimed at {aimed} from {held}");
    }
}
