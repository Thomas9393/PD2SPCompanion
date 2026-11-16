using PD2SPCompanion.Core.Collection;
using PD2SPCompanion.Core.Items;

namespace PD2SPCompanion.Shell.Tests.Forge;

/// <summary>
/// The base's upgrade and RESET on the Forge. The upgrade is done to the item in the
/// holder and is "like a pending change": the boxes keep naming what the item is made as - a unique is offered
/// on its own base alone - a line by the Base box says where it stands, and once any search box changes
/// the pending upgrade is removed. RESET makes the item afresh from the boxes, for someone who forged a
/// changed item and wants a clean one of the same; it is not a button that empties the page.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class ForgeUpgradeTests(Program program)
{
    [CorpusFact]
    public void A_unique_is_taken_a_tier_up_in_the_holder_and_the_boxes_keep_its_own_base()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        program.On(() => bench.Find("goldwrap", "Goldwrap"));
        Assert.True(bench.Until(() => bench.Panel.Stats.Count > 0));

        string made = "", up = "";
        program.On(() =>
        {
            made = bench.Held.Code.Trim();
            up = ItemTier.Next(made)!;

            Assert.True(bench.Panel.CanUpgradeBase);
            Assert.Equal("", bench.Forge.UpgradeNote);
            Assert.False(bench.Forge.ResetCommand.CanExecute(null));

            bench.Panel.UpgradeBaseCommand.Execute(null);
        });
        Assert.True(bench.Until(() => bench.Held.Code.Trim() == up), "the base was not taken up");

        program.On(() =>
        {
            // The boxes say what it is made as; the line says where it stands.
            Assert.Equal(made, bench.Forge.Base.Code);
            Assert.Equal("Goldwrap", bench.Forge.Named!.Entry.Name);
            Assert.Equal($"Upgraded to {ItemPresenter.BaseName(up)}", bench.Forge.UpgradeNote);
            Assert.True(bench.Forge.HasUpgradeNote);

            // Still Goldwrap, with what it grants - whole: nothing left unkept, nothing the legitimacy check
            // has to say of its defense or durability on the higher base.
            Assert.Equal("Goldwrap", bench.Forge.Draft!.Name);
            Assert.Contains(bench.Panel.Stats, row => row.Name == "item_goldbonus");
            Assert.False(bench.Panel.HasChanges);
            Assert.Empty(bench.Panel.Findings.Select(row => row.Said));
            Assert.Equal($"Goldwrap upgraded to {ItemPresenter.BaseName(up)}.", bench.Session.RightMessages.Text);

            // RESET: as the boxes make it, the upgrade gone - there is no other way down.
            Assert.True(bench.Forge.ResetCommand.CanExecute(null));
            bench.Forge.ResetCommand.Execute(null);
            Assert.Equal(made, bench.Held.Code.Trim());
            Assert.Equal("", bench.Forge.UpgradeNote);
            Assert.False(bench.Forge.ResetCommand.CanExecute(null));
            Assert.Equal("Goldwrap reset.", bench.Session.RightMessages.Text);
        });
    }

    [CorpusFact]
    public void A_box_changed_makes_the_item_afresh_and_the_upgrade_is_gone()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        program.On(() => bench.Find("goldwrap", "Goldwrap"));
        Assert.True(bench.Until(() => bench.Panel.Stats.Count > 0));

        string made = "";
        program.On(() =>
        {
            made = bench.Held.Code.Trim();
            bench.Panel.UpgradeBaseCommand.Execute(null);
        });
        Assert.True(bench.Until(() => bench.Forge.HasUpgradeNote), "the upgrade was not said");

        program.On(() =>
        {
            // Another rarity of the same base, and back: the item is what the boxes make, each time.
            bench.Rarity("Normal");
            Assert.Equal(made, bench.Held.Code.Trim());
            Assert.False(bench.Forge.HasUpgradeNote);

            bench.Rarity("Unique");
            Assert.Equal(made, bench.Held.Code.Trim());
            Assert.False(bench.Forge.HasUpgradeNote);
            Assert.False(bench.Forge.ResetCommand.CanExecute(null));
        });
    }

    /// <summary>
    /// The holder keeps a copy of what was forged, changes and all - so the same thing can be forged again or
    /// changed further. RESET is the clean one of the same.
    /// </summary>
    [CorpusFact]
    public void After_FORGE_the_holder_keeps_the_changed_item_and_RESET_makes_a_clean_one()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            bench.Find("war trav", "War Traveler");
            bench.Row("item_armor_percent").Value = "160";
        });
        Assert.True(bench.Until(() => bench.Held.StatLists[0].Stats.First(stat => stat.Stat == "item_armor_percent").Value == 160));

        program.On(() =>
        {
            Assert.True(bench.Forge.ResetCommand.CanExecute(null));
            bench.Forge.ForgeCommand.Execute(null);

            // The copy in the holder: the number typed is on it, and RESET is still offered.
            Assert.Equal(160, bench.Held.StatLists[0].Stats.First(stat => stat.Stat == "item_armor_percent").Value);
            Assert.True(bench.Forge.ResetCommand.CanExecute(null));

            bench.Forge.ResetCommand.Execute(null);

            // As made: the best roll, which is not 160.
            Assert.NotEqual(160, bench.Held.StatLists[0].Stats.First(stat => stat.Stat == "item_armor_percent").Value);
            Assert.False(bench.Forge.ResetCommand.CanExecute(null));
            Assert.Equal("War Traveler", bench.Forge.Draft!.Name);
        });
    }

    /// <summary>
    /// An upgraded unique from elsewhere - pasted from the game - is on a base its row does not name: the boxes
    /// are set to the base it is made on. They were left as they stood under it.
    /// </summary>
    [CorpusFact]
    public void The_boxes_show_an_upgraded_unique_by_the_base_it_is_made_on()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            program.Main.Session.EnsureGame();
            var item = ItemForge.Unique("Goldwrap", "103").Item;
            string made = item.Code.Trim();
            BaseUpgrades.Upgrade(item, new Random(3));
            Assert.NotEqual(made, item.Code.Trim());
            Assert.True(ForgeSearch.Upgraded(made, item.Code));
            Assert.False(ForgeSearch.Upgraded(item.Code, made));

            var search = new ForgeSearch();
            search.Load();
            Assert.True(search.Show(item));
            Assert.Equal("Goldwrap", search.Named!.Name);
            Assert.Equal(made, search.Base.Code);
            Assert.Equal(ForgeSearch.Unique, search.Rarity.From);
        });
    }
}
