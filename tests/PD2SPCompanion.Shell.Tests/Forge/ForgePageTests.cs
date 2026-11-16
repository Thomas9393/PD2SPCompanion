using PD2SPCompanion.Core.Collection;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Forge;

/// <summary>
/// The Forge's page: a
/// bookmark of Free's alone; a name or a base typed lists forty finds and the next forty at the list's end; a
/// find chosen fills the four boxes and is made in the holder, the item panel's sections under it; nothing
/// waits on a KEEP - a number typed, a bonus picked, a socket added are the holder's at once; the buttons that
/// make an item another kind of item are not offered here; and FORGE lands the item in the inventory, or in
/// the vault while the vault is the left page, the holder keeping a copy.
/// <para>
/// What the page decides over the boxes, asked of it as the view asks (its commands, its boxes' properties)
/// and read back from the holder's lines, the strips, the character and the vault. What the boxes offer and
/// how they narrow each other is the Core's (ForgeSearchTests). That a click reaches a find, a box and FORGE,
/// that the boxes draw what was chosen, and that the list's scrolling reaches its end are the UI cases'.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class ForgePageTests(Program program)
{
    private MainViewModel Main => program.Main;

    private int InVault => program.Get<VaultService>().Store!.Count();

    private bool InInventory(string name) =>
        Main.Inventory!.Sections.First(section => section.Pages is not null)
            .Pages!.First(page => page.Label == "Inventory").Grid!.Items.Any(tile => tile.Name == name);

    [CorpusFact]
    public void The_Forge_is_Frees_alone_Casual_has_no_bookmark_for_it_and_cannot_be_taken_to_it()
    {
        if (!program.Ready())
            return;

        using (var casual = new ForgeBench(program, PlayPolicy.CasualName, shown: false))
        {
            program.On(() =>
            {
                Assert.False(Main.CanForge);

                Main.ShowRightCommand.Execute(MainViewModel.ForgeMark);
                Assert.False(Main.IsRightForge);
                Assert.True(Main.IsRightEdit);
            });
        }

        using var free = new ForgeBench(program);
        program.On(() =>
        {
            Assert.True(Main.CanForge);
            Assert.True(Main.IsRightForge);
            Assert.True(free.Right.IsForge);

            // The mode changed with the Forge up: its bookmark goes, and Edit takes the page back.
            free.Settings.PlayMode = PlayPolicy.CasualName;
            Assert.False(Main.CanForge);
            Assert.True(Main.IsRightEdit);
        });
    }

    [CorpusFact]
    public void The_Forge_opens_with_nothing_in_its_holder_and_FORGE_waits_for_something_to_forge()
    {
        if (!program.Ready())
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            Assert.False(bench.Forge.HasDraft);
            Assert.False(bench.Panel.HasItem);
            Assert.False(bench.Forge.ForgeCommand.CanExecute(null));
            Assert.Equal("Forge to character", bench.Forge.ForgeTip);
        });
    }

    [CorpusFact]
    public void A_search_lists_forty_says_of_how_many_and_the_next_forty_join_at_the_lists_end()
    {
        if (!program.Ready())
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            var forge = bench.Forge;
            forge.Search = "bow";

            Assert.True(forge.HasMatches);
            Assert.Equal(40, forge.Matches.Count);
            var said = System.Text.RegularExpressions.Regex.Match(forge.CountText, @"^40 of (\d+)$");
            Assert.True(said.Success, forge.CountText);
            int total = int.Parse(said.Groups[1].Value);
            Assert.True(forge.IsCapped);

            // The list scrolled to its end, as the view says it (LoadMore), until nothing is left to join.
            forge.LoadMore();
            Assert.Equal(Math.Min(80, total), forge.Matches.Count);
            for (int more = 0; more < 100 && forge.IsCapped; more++)
                forge.LoadMore();

            Assert.Equal(total, forge.Matches.Count);
            Assert.Equal(total.ToString(), forge.CountText);
            Assert.Equal(forge.Matches.Count, forge.Matches.Distinct().Count());

            // At its end there is nothing more to ask for.
            forge.LoadMore();
            Assert.Equal(total, forge.Matches.Count);

            // Another search starts at the first forty again; nothing typed lists nothing.
            forge.Search = "bo";
            Assert.True(forge.Matches.Count <= 40);
            forge.Search = "";
            Assert.False(forge.HasMatches);
            Assert.Empty(forge.Matches);
            Assert.Equal("", forge.CountText);
        });
    }

    [CorpusFact]
    public void A_unique_found_by_its_name_fills_the_four_boxes_and_is_made_in_the_holder()
    {
        if (!program.Ready())
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            var forge = bench.Forge;
            bench.Find("war trav", "War Traveler");

            Assert.Equal("Boots", forge.Type.Shown);
            Assert.Equal("Battle Boots", forge.Base.Shown);
            Assert.Equal("Unique", forge.Rarity.Shown);
            Assert.Equal("War Traveler", forge.Named?.Name);

            // The find chosen, the search box is empty and its list gone.
            Assert.Equal("", forge.Search);
            Assert.False(forge.HasMatches);

            Assert.True(forge.HasDraft);
            Assert.Equal("War Traveler", forge.Draft!.Name);
            Assert.Same(forge.Draft, bench.Panel.Summary);
            Assert.Contains("Adds 15-25 damage", bench.Lines());

            // Boots kick: no weapon's damage line.
            Assert.Contains(bench.Lines(), line => line.StartsWith("Kick Damage", StringComparison.Ordinal));
            Assert.DoesNotContain(bench.Lines(), line => line.StartsWith("One-Hand Damage", StringComparison.Ordinal));

            // The Edit page's sections under it, a unique's affixes locked out.
            Assert.True(bench.Panel.HasStats);
            Assert.StartsWith("Properties", bench.Panel.StatsHeader);
            Assert.False(bench.Panel.Affixes.ShowsAffixes);
        });
    }

    [CorpusFact]
    public void A_unique_is_found_by_the_base_it_is_on()
    {
        if (!program.Ready())
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            bench.Forge.Search = "battle boots";

            var boots = Assert.Single(bench.Forge.Matches, match => match.Name == "War Traveler");
            Assert.Equal("Battle Boots", boots.Detail);
            Assert.True(boots.HasAside);
        });
    }

    [CorpusFact]
    public void A_number_typed_is_the_items_at_once_the_holder_follows_and_nothing_reads_as_pending()
    {
        if (!program.Ready())
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            bench.Find("war trav", "War Traveler");
            Assert.True(bench.Panel.KeepsAtOnce);
            Assert.Equal("Defense: 139", bench.Line("Defense: "));
            Assert.Equal("190", bench.Row("item_armor_percent").Value);

            bench.Row("item_armor_percent").Value = "160";
        });

        Assert.True(bench.Until(() => bench.Line("Defense: ") == "Defense: 124"), "the holder did not follow the number typed");
        program.On(() =>
        {
            Assert.Equal(160, bench.Held.StatLists[0].Stats.First(stat => stat.Stat == "item_armor_percent").Value);
            Assert.False(bench.Panel.HasChanges);
            Assert.True(bench.Forge.ForgeCommand.CanExecute(null));
        });
    }

    [CorpusFact]
    public void FORGE_puts_the_item_in_the_inventory_says_so_and_the_holder_keeps_one_to_forge_again()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            bench.Find("war trav", "War Traveler");
            bench.Row("item_armor_percent").Value = "160";
        });
        Assert.True(bench.Until(() => bench.Line("Defense: ") == "Defense: 124"));

        program.On(() =>
        {
            Assert.False(InInventory("War Traveler"));
            var made = bench.Held;

            bench.Forge.ForgeCommand.Execute(null);

            Assert.Equal("War Traveler forged to inventory.", bench.Session.Messages.Text);
            Assert.True(InInventory("War Traveler"));

            // The holder's is a copy under an id of its own, as it was changed.
            Assert.True(bench.Forge.HasDraft);
            Assert.NotSame(made, bench.Held);
            Assert.NotEqual(made.Id, bench.Held.Id);
            Assert.Contains("Adds 15-25 damage", bench.Lines());
            Assert.Equal("Defense: 124", bench.Line("Defense: "));
            Assert.True(bench.Forge.ForgeCommand.CanExecute(null));

            // The same find chosen again: made afresh, the change gone - the page's way back to the item as made.
            bench.Find("war trav", "War Traveler");
            Assert.Equal("Defense: 139", bench.Line("Defense: "));
        });
    }

    [CorpusFact]
    public void A_base_is_made_plain_and_its_upgrade_is_done_in_the_holder_the_Base_box_keeping_what_it_is_made_as()
    {
        if (!program.Ready())
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            var forge = bench.Forge;
            bench.Find("pike", "Pike");

            Assert.Equal("Weapons: Spears", forge.Type.Shown);
            Assert.Equal("Pike", forge.Base.Shown);
            Assert.Equal("Normal", forge.Rarity.Shown);
            Assert.Null(forge.Named);
            Assert.False(forge.CanPickName);
            Assert.Equal("Pike", forge.Draft!.Name);
            Assert.Contains(bench.Lines(), line => line.StartsWith("Two-Hand Damage", StringComparison.Ordinal));

            // It has a tier above it, and the Horadric Cube's button takes it there - on the Forge too, with the
            // upgrade said beside the Base box, which keeps naming what the item is made as.
            Assert.Equal("Upgrade to Lance", bench.Panel.UpgradeHint);
            Assert.True(bench.Panel.CanUpgradeBase);

            bench.Panel.UpgradeBaseCommand.Execute(null);
            Assert.Equal("Lance", forge.Draft!.Name);
            Assert.Equal("Pike", forge.Base.Shown);
            Assert.Equal("Upgraded to Lance", forge.UpgradeNote);
        });
    }

    [CorpusFact]
    public void One_find_after_another_each_box_follows_a_rare_names_no_item_and_a_runeword_asks_which()
    {
        if (!program.Ready())
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            var forge = bench.Forge;
            bench.Find("pike", "Pike");
            bench.Find("tannr", "The Tannr Gorerod");

            Assert.Equal("Pike", forge.Base.Shown);
            Assert.Equal("Unique", forge.Rarity.Shown);
            Assert.Equal("The Tannr Gorerod", forge.Named?.Name);
            Assert.True(forge.CanPickName);
            Assert.False(bench.Panel.Affixes.ShowsAffixes);

            // Rolled as a drop, every number at its top: a rare at 99 carries six affixes' properties.
            bench.Rarity("Rare");
            Assert.True(bench.Panel.Affixes.ShowsAffixes);
            var header = System.Text.RegularExpressions.Regex.Match(bench.Panel.StatsHeader, @"^Properties  \((\d+)\)$");
            Assert.True(header.Success && int.Parse(header.Groups[1].Value) >= 6, bench.Panel.StatsHeader);
            Assert.False(forge.CanPickName);
            Assert.Null(forge.Named);
            Assert.Equal("", forge.NameHint);

            bench.Rarity("Runeword");
            Assert.True(forge.CanPickName);
            Assert.Null(forge.Named);
            Assert.True(forge.NameChoices.Count > 1);
            Assert.Equal("Choose runeword", forge.NameHint);

            // Several to choose from and none chosen: nothing to make, and nothing in the holder.
            Assert.False(forge.HasDraft);
        });
    }

    [CorpusFact]
    public void A_ring_is_never_Normal_its_Rarity_box_does_not_offer_it()
    {
        if (!program.Ready())
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            var forge = bench.Forge;
            Assert.Contains(forge.RarityChoices, line => line.Shown == "Normal");

            forge.Type = forge.TypeChoices.First(line => line.Shown == "Rings");
            forge.Base = forge.BaseChoices.First(line => line.Shown == "Ring");

            var offered = forge.RarityChoices.Select(line => line.Shown).ToList();
            Assert.Contains("Magic", offered);
            Assert.Contains("Rare", offered);
            Assert.DoesNotContain("Normal", offered);
            Assert.DoesNotContain("Superior", offered);
            Assert.Contains(forge.Rarity, forge.RarityChoices);

            // The type back to Any: no base to choose, and every rarity again.
            forge.Type = ForgeSearch.Any;
            Assert.Equal("Any", forge.Type.Shown);
            Assert.False(forge.CanPickBase);
            Assert.Contains(forge.RarityChoices, line => line.Shown == "Normal");
        });
    }

    [CorpusFact]
    public void A_runeword_chosen_with_no_type_is_made_in_the_best_base_of_its_kinds_then_in_the_base_chosen()
    {
        if (!program.Ready())
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            var forge = bench.Forge;
            bench.Rarity("Runeword");
            Assert.False(forge.CanPickBase);

            forge.Named = forge.NameChoices.First(word => word.Name == "Black");
            Assert.True(forge.CanPickBase);
            Assert.Equal("Black", forge.Draft?.Name);
            Assert.Contains("Thunder Maul", bench.Lines());

            var offered = forge.BaseChoices.Select(line => line.Shown).ToList();
            Assert.Equal("Any", offered[0]);
            Assert.Contains("Flail", offered);
            Assert.DoesNotContain("Ring", offered);

            forge.Base = forge.BaseChoices.First(line => line.Shown == "Flail");
            Assert.Equal("Black", forge.Named?.Name);
            Assert.Equal("Black", forge.Draft?.Name);
            Assert.Contains("Flail", bench.Lines());
            Assert.DoesNotContain("Thunder Maul", bench.Lines());
        });
    }

    [CorpusFact]
    public void A_superior_base_is_made_with_no_bonus_and_takes_one_from_its_Superior_line()
    {
        if (!program.Ready())
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            var superior = bench.Panel.Superior;
            bench.Find("sacred armor", "Sacred Armor");
            Assert.Equal("Defense: 730", bench.Line("Defense: "));

            // The Rarity box says what the item is: no Make superior on a normal base here, and no line yet.
            Assert.False(superior.CanMake);
            Assert.False(superior.Shows);

            bench.Rarity("Superior");
            Assert.True(superior.Shows);
            Assert.Equal("None", superior.Now);
            Assert.Equal("Defense: 730", bench.Line("Defense: "));

            superior.OpenPickerCommand.Execute(null);
            Assert.True(superior.IsPickerOpen);
            Assert.Equal(
                ["+10-20% Enhanced Defense", "Increase Maximum Durability 15-25%", "+5-15% Enhanced Defense + Increase Maximum Durability 10-20%"],
                superior.Choices.Select(row => string.Join(" + ", row.Lines)));

            superior.PickCommand.Execute(superior.Choices.First(row => row.Lines is ["+10-20% Enhanced Defense"]));
            // Open still, as every picker is after a pick; written when it closes.
            Assert.True(superior.IsPickerOpen);
            superior.ClosePickerCommand.Execute(null);
        });

        // At its top, over the base's top defence plus one.
        Assert.True(bench.Until(() => bench.Line("Defense: ") == "Defense: 877"), "the bonus picked is not on the item");
        program.On(() =>
        {
            var superior = bench.Panel.Superior;
            Assert.Contains("+20% Enhanced Defense", bench.Lines());
            Assert.Equal("+10-20% Enhanced Defense", superior.Now);
            Assert.Equal("Sacred Armor: now +10-20% Enhanced Defense.", bench.Session.RightMessages.Text);
            Assert.False(bench.Panel.HasChanges);

            superior.OpenPickerCommand.Execute(null);
            Assert.True(superior.Choices.First(row => row.Lines is ["+10-20% Enhanced Defense"]).IsHeld);
            superior.TakeOffCommand.Execute(null);
            // Written when the picker closes: it stays open after a pick.
            superior.ClosePickerCommand.Execute(null);
        });

        // None takes the bonus off; the base's defence stays.
        Assert.True(bench.Until(() => bench.Line("Defense: ") == "Defense: 731"), "None did not take the bonus off");
        program.On(() =>
        {
            Assert.DoesNotContain("+20% Enhanced Defense", bench.Lines());
            Assert.Equal("None", bench.Panel.Superior.Now);
            Assert.Equal("Superior", bench.Forge.Rarity.Shown);
        });
    }

    [CorpusFact]
    public void A_runeword_is_made_in_the_base_chosen_and_its_Superior_line_makes_that_base_superior_or_normal()
    {
        if (!program.Ready())
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            var forge = bench.Forge;
            var superior = bench.Panel.Superior;
            bench.Find("pike", "Pike");
            bench.Find("insight", "Insight");

            Assert.Contains(bench.Lines(), line => line.StartsWith("Level 17 Meditation Aura When Equipped", StringComparison.Ordinal));
            Assert.Equal("Pike", forge.Base.Shown);
            Assert.Equal("Runeword", forge.Rarity.Shown);
            Assert.Equal("Insight", forge.Named?.Name);

            // Made on a normal base.
            Assert.True(superior.Shows);
            Assert.Equal("None", superior.Now);
            Assert.Equal("Durability: 50 of 50", bench.Line("Durability: "));
            Assert.Equal("Two-Hand Damage: 56 to 214", bench.Line("Two-Hand Damage: "));

            superior.OpenPickerCommand.Execute(null);
            superior.PickCommand.Execute(superior.Choices.First(row => row.Lines is ["+5-15% Enhanced Damage", "Increase Maximum Durability 10-20%"]));
            // Written when the picker closes: it stays open after a pick.
            superior.ClosePickerCommand.Execute(null);
        });

        // A bonus makes the base superior, full at the maximum the bonus raises. The damage counts the Sol
        // rune's +9 to minimum damage: what a socket gives is in the item's lines, as in game.
        Assert.True(bench.Until(() => bench.Line("Durability: ") == "Durability: 60 of 60"),
            program.On(() => $"the base was not made superior: {bench.Line("Durability: ")}, quality {bench.Held.Quality}, changes {bench.Panel.HasChanges}, superior '{bench.Panel.Superior.Now}', open {bench.Panel.Superior.IsPickerOpen}, eth {bench.Held.IsEthereal}, findings {string.Join("|", bench.Panel.Findings.Select(f => f.Said))}"));
        program.On(() =>
        {
            Assert.Equal("Two-Hand Damage: 58 to 223", bench.Line("Two-Hand Damage: "));
            Assert.Equal(D2SLib.Model.Save.ItemQuality.Superior, bench.Held.Quality);
            Assert.Equal("Runeword", bench.Forge.Rarity.Shown);
            Assert.False(bench.Panel.HasChanges);

            bench.Panel.Superior.OpenPickerCommand.Execute(null);
            bench.Panel.Superior.TakeOffCommand.Execute(null);
            // Written when the picker closes: it stays open after a pick.
            bench.Panel.Superior.ClosePickerCommand.Execute(null);
        });

        Assert.True(bench.Until(() => bench.Line("Durability: ") == "Durability: 50 of 50"), "the base was not made normal again");
        program.On(() =>
        {
            Assert.Equal("Two-Hand Damage: 56 to 214", bench.Line("Two-Hand Damage: "));
            Assert.Equal(D2SLib.Model.Save.ItemQuality.Normal, bench.Held.Quality);
            Assert.Equal("Insight: normal base.", bench.Session.RightMessages.Text);
        });
    }

    [CorpusFact]
    public void A_socket_added_is_the_holders_at_once_and_with_the_vault_on_the_left_FORGE_goes_to_the_vault()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            var sockets = bench.Panel.Sockets;
            bench.Find("pike", "Pike");
            Assert.Equal("Sockets  (0 / 0)", sockets.Header);
            Assert.DoesNotContain(bench.Lines(), line => line.StartsWith("Socketed", StringComparison.Ordinal));
            Assert.StartsWith("Add socket (max 6)", sockets.AddHint);

            sockets.AddCommand.Execute(null);
        });

        Assert.True(bench.Until(() => bench.Lines().Contains("Socketed (1)")), "the socket added does not show in the holder");
        int stored = 0;
        program.On(() =>
        {
            Assert.Equal("Sockets  (0 / 1)", bench.Panel.Sockets.Header);
            Assert.Equal(1, bench.Held.TotalNumberOfSockets);
            Assert.False(bench.Panel.HasChanges);

            // The left page turned to the vault: FORGE is aimed at it.
            Assert.False(bench.Forge.KeepsToVault);
            Main.ShowLeftCommand.Execute(MainViewModel.VaultMark);
            Assert.True(Main.IsRightForge, "the Forge stays beside the vault");
            Assert.True(bench.Forge.KeepsToVault);
            Assert.Equal("Forge to vault", bench.Forge.ForgeTip);

            stored = InVault;
            bench.Forge.ForgeCommand.Execute(null);

            Assert.Equal("Pike forged to vault.", bench.Session.Messages.Text);
            Assert.Equal(stored + 1, InVault);
            Assert.False(InInventory("Pike"));

            // The holder's copy has its socket, nothing pending.
            Assert.Equal("Sockets  (0 / 1)", bench.Panel.Sockets.Header);
            Assert.Contains("Socketed (1)", bench.Lines());
            Assert.False(bench.Panel.HasChanges);

            // And back to the inventory with the left page.
            Main.ShowLeftCommand.Execute(MainViewModel.InventoryMark);
            Assert.False(bench.Forge.KeepsToVault);
            Assert.Equal("Forge to character", bench.Forge.ForgeTip);
        });
    }

    [CorpusFact]
    public void What_makes_an_item_another_kind_is_the_boxes_no_runeword_builder_and_no_craft_on_this_page()
    {
        if (!program.Ready())
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            Assert.False(bench.Panel.Transforms);

            // A socketed base some runeword fits: the Edit page offers to build one, the Forge does not.
            bench.Find("pike", "Pike");
            bench.Panel.Sockets.Count = "4";
        });

        Assert.True(bench.Until(() => bench.Lines().Contains("Socketed (4)")));
        program.On(() =>
        {
            Assert.True(bench.Panel.Rules.BuildsRunewords);
            Assert.True(PD2SPCompanion.Core.Items.RunewordBuilder.WordsFor(bench.Held).Count > 0, "no runeword fits the four sockets: the check below proves nothing");
            Assert.False(bench.Panel.Runeword.CanBuild);
            Assert.False(bench.Panel.Superior.CanMake);

            // A rare whose base some recipe takes: no craft.
            bench.Rarity("Rare");
            Assert.True(bench.Panel.Affixes.ShowsAffixes);
            Assert.True(PD2SPCompanion.Core.Items.AffixRanges.CraftFamilies(bench.Held).Count > 0, "no recipe takes a rare Pike: the check below proves nothing");
            Assert.False(bench.Panel.Crafting.CanCraftFrom);
        });
    }

    [CorpusFact]
    public void The_search_section_folds_and_unfolds()
    {
        if (!program.Ready())
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            Assert.True(bench.Forge.SearchOpen);

            bench.Forge.ToggleSearchCommand.Execute(null);
            Assert.False(bench.Forge.SearchOpen);

            bench.Forge.ToggleSearchCommand.Execute(null);
            Assert.True(bench.Forge.SearchOpen);
        });
    }
}
