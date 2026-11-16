using PD2SPCompanion.Core.Collection;
using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.Tests.Inventory;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Extra;

/// <summary>
/// The Extra page's two wizards.
/// The crafting wizard: the chart of the craft recipes, each cell how many can be built from the shared
/// stash's materials tab and the bases in the vault's Crafting Bases folder; the recipe picked told of under
/// it - the base, then the jewel, rune and gem in a bracket, or the infusion - and let go again. The rune
/// wizard's rows say what their runeword grants.
/// <para>
/// What the page decides. That the chart and the strip are drawn, that a click elsewhere lets a recipe go,
/// that a row's tooltip comes up and that both cards fold, are the UI cases' (units/extra-wizards.ps1); the
/// counting itself is Core's (CraftPlanTests).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class CraftWizardTests(Program program)
{
    private VP_ViewModel_Extra_R Page => program.Get<VP_ViewModel_Extra_L>().Tools;
    private CraftWizardViewModel Crafting => Page.Crafting;

    private CraftCellViewModel Cell(string family, string type) =>
        Crafting.Rows.SelectMany(row => row.Cells).Single(cell =>
            cell.Cell.Card.Family.Equals(family, StringComparison.OrdinalIgnoreCase)
            && cell.Cell.Card.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

    [CorpusFact]
    public void The_chart_counts_from_the_materials_tab_and_the_vaults_crafting_bases_and_tells_of_the_recipe_picked()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            Crafting.Refresh();
            Assert.Equal(GrailCatalog.TypeOrder.Count, Crafting.Rows.Count);
            Assert.Equal(GrailCatalog.FamilyOrder.Count, Crafting.Families.Count);
            Assert.All(Crafting.Rows, row => Assert.Equal(GrailCatalog.FamilyOrder.Count, row.Cells.Count));
            Assert.Equal("Ring", Crafting.Rows[1].Label);

            // With no Crafting Bases folder in the vault there is nothing to build: every cell a dot.
            var blood = Cell("blood", "ring");
            Assert.Equal(0, blood.Count);
            Assert.Equal("·", blood.Text);
            Assert.False(blood.CanMake);

            // The rings of the stash and the inventory, stored where Store all's crafting-base sorting puts them.
            var store = program.Get<VaultService>().Store!;
            long rings = store.FolderFor([SmartSort.CraftingBasesFolder, "Rings"]);
            int stored = 0;
            foreach (var tile in program.Grid("Personal Stash").Items.Concat(program.Grid("Inventory").Items).ToList())
            {
                if (CraftPlan.Takes(blood.Cell.Card, tile.Item) && store.Add(tile.Item, rings, out _) is not null)
                    stored++;
            }

            Assert.True(stored >= 2, $"{stored} rings stored as bases");

            // Materials for two by the ingredients - a jewel is the tab's Jewel Fragments - and one infusion.
            var stash = program.Get<SharedStashSession>();
            foreach (var need in blood.Cell.Needs)
                Assert.Null(stash.SetMaterial(CraftPlan.SlotOf(need.Code)!.Value, 2));
            Assert.Null(stash.SetMaterial(Materials.SlotFor("jewf")!.Value, 2));
            Assert.Null(stash.SetMaterial(CraftPlan.SlotOf(blood.Cell.Infusion!.Code)!.Value, 1));

            Crafting.Refresh();
            int expected = Math.Min(3, stored);
            Assert.Equal(expected, blood.Count);
            Assert.Equal(expected.ToString(), blood.Text);
            Assert.EndsWith(" of 70 can be built", Crafting.Summary);
            Assert.NotEqual("0 of 70 can be built", Crafting.Summary);

            // Picked: its name, what it would build, and the parts - the base first, the three in a bracket, "or"
            // before the infusion - each with how many there are.
            Crafting.PickCommand.Execute(blood);
            Assert.True(blood.IsSelected);
            Assert.Equal("Blood Ring", Crafting.Title);
            Assert.Equal($"Can build {expected}", Crafting.Verdict);
            Assert.True(Crafting.VerdictIsGood);

            var parts = Crafting.Parts;
            Assert.Equal(5, parts.Count);
            Assert.Equal(("Crafting bases in vault", stored.ToString(), ""), (parts[0].Name, parts[0].Count, parts[0].Before));
            Assert.Equal(("Jewel", "2", "+"), (parts[1].Name, parts[1].Count, parts[1].Before));
            Assert.True(parts[1].Opens && !parts[1].Closes);
            Assert.True(parts[3].Closes && !parts[3].Opens);
            Assert.Equal(("or", "1"), (parts[4].Before, parts[4].Count));
            Assert.All(parts, part => Assert.NotNull(part.Sprite));
            Assert.All(parts, part => Assert.False(part.IsShort));
            Assert.NotEmpty(Crafting.Grants);

            // Another recipe picked: the first is let go of. One with nothing for it says why not.
            var amulet = Cell("blood", "amul");
            Crafting.PickCommand.Execute(amulet);
            Assert.False(blood.IsSelected);
            Assert.Equal("Blood Amulet", Crafting.Title);
            Assert.False(Crafting.VerdictIsGood);
            Assert.Equal("No base", Crafting.Verdict);
            Assert.True(Crafting.Parts[0].IsShort);

            // Let go: nothing is told of, and a count made again picks none.
            Crafting.Release();
            Assert.False(Crafting.HasSelected);
            Assert.False(amulet.IsSelected);
            Assert.Empty(Crafting.Parts);
            Crafting.Refresh();
            Assert.False(Crafting.HasSelected);
        });
    }

    [CorpusFact]
    public void The_mark_behind_the_heading_says_where_the_bases_are_counted_from()
    {
        if (!program.Ready())
            return;

        program.On(() => Assert.Equal(
            "Bases counted from vault folder Crafting Bases" + Environment.NewLine + "Fast inventory storing with Shift puts bases there",
            Crafting.BasesTip));
    }

    [CorpusFact]
    public void A_rune_wizards_row_says_what_its_runeword_grants()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            Page.Wizard.Refresh();

            var steel = Page.Wizard.Rows.Single(row => row.Name == "Steel");
            Assert.NotEmpty(steel.Bonus);
            Assert.All(steel.Bonus, line => Assert.True(line.IsMagic));
            Assert.Contains(steel.Bonus, line => line.Text.Contains("Increased Attack Speed"));

            // A word the table keeps with different properties for different bases says each under its bases.
            var spirit = Page.Wizard.Rows.Single(row => row.Name == "Spirit");
            Assert.Contains(spirit.Bonus, line => line.IsRule);
            Assert.True(spirit.Bonus.Count(line => line.IsFact) >= 2);
            Assert.True(spirit.Bonus[0].IsFact);
        });
    }

    /// <summary>
    /// "Hand to Hand, Hand to Hand 2": a base's name must read as the game displays it.
    /// And the lines under them, which the same look found: a line the game prints bare had its number
    /// after it, a class's skills were all the Amazon's and a tree her first, Indestructible was "indestruct 1".
    /// </summary>
    [CorpusFact]
    public void A_runewords_bases_and_lines_are_said_as_a_player_knows_them()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            // The names alone: the bases' counts are the next test's.
            bool counted = Page.Wizard.CountsBases;
            Page.Wizard.CountsBases = false;
            try
            {
            Page.Wizard.Refresh();
            RunewordRow Row(string name) => Page.Wizard.Rows.First(row => row.Name == name);
            var lines = Page.Wizard.Rows.SelectMany(row => row.Bonus).Where(line => line.IsMagic).Select(line => line.Text).ToList();

            // The bases: claws once, an orb, a chest, a shield - not the type table's own names.
            Assert.Equal("Scepter, Club, Orb, Claw, Staff", Row("Neophyte").Bases);
            Assert.Equal("Chest", Row("Stealth").Bases);
            Assert.Equal("Shield", Row("Rhyme").Bases);
            Assert.DoesNotContain(Page.Wizard.Rows, row => row.Bases.Contains("Hand to Hand") || row.Bases.Contains("Any Shield")
                                                           || row.Bases.Contains("Sorceress Item") || row.Bases.Contains("2H "));

            // The lines.
            Assert.Contains("+2 to Necromancer Skill Levels", Row("Bone").Bonus.Select(line => line.Text));
            Assert.Contains("+2 to Poison and Bone Skills (Necromancer Only)", Row("White").Bonus.Select(line => line.Text));
            Assert.Contains("+1-2 to Barbarian Skill Levels", Row("Myth").Bonus.Select(line => line.Text));
            Assert.Contains("Half Freeze Duration", Row("Neophyte").Bonus.Select(line => line.Text));
            Assert.Contains("Cannot Be Frozen", lines);
            Assert.Contains("Indestructible", lines);
            Assert.DoesNotContain(lines, line => line.StartsWith("to ", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, line => System.Text.RegularExpressions.Regex.IsMatch(line, @"^[A-Za-z' ]+ 1$"));
            }
            finally
            {
                Page.Wizard.CountsBases = counted;
            }
        });
    }

    /// <summary>
    /// The crafting wizard shows the gap of the old runeword wizard, which did not count the bases: a toggle to count
    /// them or not, with the number behind each base - bow (1), sword (0) and so on - and on the runeword's title
    /// itself, Infinity (2).
    /// </summary>
    [CorpusFact]
    public void With_the_bases_counted_a_row_says_how_many_of_each_there_are_and_how_many_can_be_made()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            var wizard = Page.Wizard;
            RunewordRow Steel() => wizard.Rows.First(row => row.Name == "Steel");
            bool counted = wizard.CountsBases;
            try
            {
                // Not counted: by its runes alone, as it was, and no number anywhere.
                wizard.CountsBases = false;
                Assert.Null(Steel().Count);
                Assert.Equal("Steel", Steel().Title);
                Assert.StartsWith("Sword, Axe, Mace, Claw", Steel().Bases);
                Assert.True(Steel().CanMake, "the pad's runes make a Steel");

                // Counted, with no Bases folder in the vault: none of any kind, and none to make.
                wizard.CountsBases = true;
                Assert.Equal("Steel (0)", Steel().Title);
                Assert.StartsWith("Sword (0), Axe (0), Mace (0), Claw (0)", Steel().Bases);
                Assert.False(Steel().CanMake);

                // Two long swords of two sockets and one of three - no base of a two-rune word - stored where
                // sorted storing puts white items.
                var store = program.Get<VaultService>().Store!;
                long weapons = store.FolderFor([SmartSort.BasesFolder, "Weapons"]);
                foreach (int sockets in new[] { 2, 2, 3 })
                    Assert.NotNull(store.Add(ItemForge.PlainBase("lsd", sockets, program.Main.Session.ItemEra!).Item, weapons, out _));

                wizard.Refresh();
                Assert.Equal("Steel (2)", Steel().Title);
                Assert.StartsWith("Sword (2), Axe (0), Mace (0), Claw (0)", Steel().Bases);
                Assert.True(Steel().CanMake);
                Assert.EndsWith(" can be made", wizard.Summary);

                // The toggle says what it does, where the bases are counted from and how they get there.
                Assert.Equal(
                    string.Join(Environment.NewLine, "Count bases", "Bases counted from vault folder Bases", "Fast inventory storing with Ctrl puts bases there"),
                    wizard.BasesTip);

                // The folder renamed, and its Weapons moved out to the top and renamed: the sorter still delivers
                // there, and so the wizard still counts there - and says the folder by the name it wears now.
                store.RenameFolder(store.Folders().First(folder => folder.ParentId is null && folder.Name == SmartSort.BasesFolder).Id, "White things");
                store.MoveFolder(weapons, null);
                store.RenameFolder(weapons, "Sticks");
                Assert.Equal(weapons, store.FolderFor([SmartSort.BasesFolder, "Weapons"]));

                wizard.Refresh();
                Assert.Equal("Steel (2)", Steel().Title);
                Assert.Contains("Bases counted from vault folder White things", wizard.BasesTip);
            }
            finally
            {
                wizard.CountsBases = counted;
            }
        });
    }

    [CorpusFact]
    public void Both_wizards_fold()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            bool runes = Page.Wizard.IsOpen, crafting = Crafting.IsOpen;
            try
            {
                Page.Wizard.ToggleOpenCommand.Execute(null);
                Crafting.ToggleOpenCommand.Execute(null);
                Assert.Equal((!runes, !crafting), (Page.Wizard.IsOpen, Crafting.IsOpen));
            }
            finally
            {
                (Page.Wizard.IsOpen, Crafting.IsOpen) = (runes, crafting);
            }
        });
    }
}
