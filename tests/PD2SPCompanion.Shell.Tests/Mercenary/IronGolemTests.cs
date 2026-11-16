using D2SLib.Model.Save;

using PD2SPCompanion.Core.Characters;
using PD2SPCompanion.Core.GameFiles;
using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Mercenary;

/// <summary>
/// The Iron Golem beside the mercenary: its button is there only while
/// a golem stands, its item is chosen as any item is and not deleted, the button puts it away and brings it
/// back, Dismiss hands the item to the character and takes the golem away, the undo stands it up again - and
/// the item that gives the golem its skill, taken off, takes the golem with it.
/// <para>
/// On two Necromancers made for each test as <c>pd2spc create --golem 7s7 [--golem-by Metalgrid]</c> makes
/// them, in the run's own folder, and removed after it. What the page and the session decide, asked as the
/// buttons ask; that the button, the item and Dismiss are drawn in the mercenary's section and a click reaches
/// them is the UI cases' to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class IronGolemTests(Program program)
{
    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Inv_L Page => Main.Inventory!;

    private GridViewModel Inventory =>
        Page.Sections.First(section => section.Pages is not null).Pages!.First(page => page.Label == "Inventory").Grid!;

    /// <summary>
    /// A Necromancer standing an Iron Golem made of a Balrog Spear, opened: its skill from points - or, named,
    /// from that unique worn with no point in it (PD2's Metalgrid). False where the corpus has no template.
    /// </summary>
    private bool Open(string name, string? by = null)
    {
        string template = Path.Combine(Program.Corpus, "saves", "PhySpearZon.d2s");
        if (!File.Exists(template))
            return false;

        program.On(() =>
        {
            var game = Session.EnsureGame()!;
            int necromancer = CharacterClasses.Names.ToList().IndexOf("Necromancer");
            var summoner = D2SLib.Core.ReadD2S(CharacterCreation.Build(game, template, name, (byte)necromancer, by is null ? 60 : 90, hardcore: false, questsDone: true));
            var skills = new SkillEditor(summoner, SkillTree.For(game, summoner.ClassId));
            if (by is not null)
            {
                var worn = ItemForge.Unique(by, "103").Item;
                worn.Mode = ItemMode.Equipped;
                worn.Location = ItemLocation.Neck;
                worn.Page = 0;
                summoner.PlayerItemList.Items.Add(worn);
                summoner.PlayerItemList.Count = (ushort)summoner.PlayerItemList.Items.Count;
            }
            else
            {
                skills.AddUnspent(30);
                var chain = new List<SkillEntry>();
                void Need(SkillEntry skill)
                {
                    foreach (int id in skill.Requires)
                        Need(skills.Tree.BySlot.Values.First(each => each.Id == id));
                    if (!chain.Contains(skill))
                        chain.Add(skill);
                }

                Need(skills.Tree.BySlot[skills.Tree.IronGolemSlot!.Value]);
                foreach (var skill in chain)
                    skills.Add(skill, 1, 99, freely: true);
            }

            summoner.Golem.Exists = true;
            summoner.Golem.Item = ItemForge.PlainBase("7s7", 0, "103").Item;
            SaveWriter.WriteSafely(program.SaveOf(name), SaveWriter.Prepared(summoner));

            Main.Home.SwitchTo(program.SaveOf(name));
        });

        Assert.True(program.Until(() => Session.IsLoaded && Session.CharacterName == name, 15000), $"{name} was not opened");
        program.On(() =>
        {
            Main.ShowPageByKey("Inventory_L");
            Main.ShowPageByKey("Inventory_R");
        });
        return true;
    }

    /// <summary>The pad's character open again, and nothing left of the one made.</summary>
    private void Remove(string name)
    {
        program.On(() =>
        {
            Page.ShowGolem = true;
            Page.DisarmDelete();
            if (Session.CharacterName == name)
                Main.Home.SwitchTo(program.SaveOf(Program.Open));
        });
        program.Until(() => Session.IsLoaded && Session.CharacterName == Program.Open, 15000);

        string saves = Path.Combine(program.Folder, "saves");
        foreach (string file in Directory.GetFiles(saves, name + ".*"))
            File.Delete(file);
        foreach (string kept in Directory.GetDirectories(saves, name, SearchOption.AllDirectories))
            Directory.Delete(kept, recursive: true);
    }

    [CorpusFact]
    public void A_character_with_no_golem_has_no_button_and_nothing_beside_the_mercenary()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            Assert.False(Page.HasGolem);
            Assert.False(Page.GolemShown);
            Assert.Empty(Page.GolemSlots);
            Assert.Equal("", Page.GolemDetail);
        });
    }

    /// <summary>
    /// The golem's slot is a pick list of the mercenary's section, and its item is among the picks:
    /// Select all there reached the mercenary's slots and passed it by.
    /// </summary>
    [CorpusFact]
    public void Select_all_in_the_mercenarys_section_reaches_the_golems_item()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            if (!Open("Golempicker"))
                return;

            program.On(() =>
            {
                var golem = Session.GolemTile;
                Assert.NotNull(golem);
                Page.SelectAllMerc();
                Assert.True(golem!.IsPicked || ReferenceEquals(Session.SelectedItem?.Item, golem.Item),
                    "Select all over the mercenary's section left the golem's item out");
                Page.ClearSelection();
            });
        }
        finally
        {
            Remove("Golempicker");
        }
    }

    [CorpusFact]
    public void The_golems_item_is_shown_to_begin_with_chosen_as_any_item_and_put_away_by_its_button()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            if (!Open("Golemancer"))
                return;

            program.On(() =>
            {
                Assert.True(Page.HasGolem);
                Assert.True(Page.GolemShown);
                Assert.Equal("Balrog Spear", Page.GolemDetail);
                var slot = Assert.Single(Page.GolemSlots);
                Assert.Equal("Balrog Spear", slot.Item!.Name);

                // Chosen: the Edit page shows it.
                Page.ClickTileCommand.Execute(slot.Item);
                Assert.Same(slot.Item, Session.SelectedItem);
                Assert.Contains(Session.SelectedFacts, line => line.StartsWith("Code: 7s7"));

                // Its menu: the golem's dismissal, and nothing that would be refused (not the menu of an item
                // not worn). The tools' Equip and Equip on mercenary are dimmed, and say why.
                var menu = Page.MenuFor(slot.Item!);
                Assert.Equal("Dismiss Iron Golem", menu.Words());
                Assert.Empty(menu.Icons);
                Assert.False(Page.CanEquip);
                Assert.EndsWith("Require Iron Golem dismissed.", Page.EquipHint);
                Assert.False(Page.CanEquipMerc);

                // Put away: its item is not shown, and lets go of the selection.
                Page.ToggleGolemCommand.Execute(null);
                Assert.False(Page.GolemShown);
                Assert.True(Page.HasGolem);
                Assert.Null(Session.SelectedItem);

                Page.ToggleGolemCommand.Execute(null);
                Assert.True(Page.GolemShown);
            });
        }
        finally
        {
            Remove("Golemancer");
        }
    }

    [CorpusFact]
    public void Deleting_the_golems_item_is_refused_it_is_the_golems_until_the_golem_is_dismissed()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            if (!Open("Golemancer"))
                return;

            program.On(() =>
            {
                Page.ClickTileCommand.Execute(Page.GolemSlots[0].Item);

                // The bin asks; its tick is what deletes.
                Page.DeleteItemCommand.Execute(null);
                Assert.True(Page.DeleteArmed);
                Page.DeleteItemCommand.Execute(null);

                Assert.Equal("Require Iron Golem dismissed.", Session.Status);
                Assert.True(Page.GolemShown);
                Assert.Equal("Balrog Spear", Page.GolemDetail);
            });
        }
        finally
        {
            Remove("Golemancer");
        }
    }

    [CorpusFact]
    public void Dismiss_is_refused_with_no_room_for_the_golems_item_and_the_golem_stands()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            if (!Open("Golemancer"))
                return;

            program.On(() =>
            {
                // Inventory, cube and stash filled with its own shape, until nothing more goes in.
                for (int made = 0; made < 200 && program.Give("Balrog Spear", "Balrog Spear") is not null; made++)
                {
                }

                Assert.NotNull(Session.GolemHasNoRoom());
                int held = Inventory.Items.Count;

                Page.DismissGolemCommand.Execute(null);

                Assert.StartsWith("Require room for the Iron Golem's", Session.Status);
                Assert.True(Page.HasGolem);
                Assert.True(Pad.File(program, "Golemancer").Golem.Exists);
                Assert.Equal(held, Inventory.Items.Count);
            });
        }
        finally
        {
            Remove("Golemancer");
        }
    }

    [CorpusFact]
    public void Dismiss_hands_its_item_to_the_character_and_the_undo_stands_the_golem_up_again()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            if (!Open("Golemancer"))
                return;

            program.On(() =>
            {
                Assert.DoesNotContain(Inventory.Items, tile => tile.Name == "Balrog Spear");

                Page.DismissGolemCommand.Execute(null);
                Assert.Equal("Iron Golem dismissed: Balrog Spear in inventory.", Session.Status);
                Assert.False(Page.HasGolem);
                Assert.False(Page.GolemShown);
                Assert.Empty(Page.GolemSlots);
                Assert.Contains(Inventory.Items, tile => tile.Name == "Balrog Spear");
                Assert.False(Pad.File(program, "Golemancer").Golem.Exists);

                Main.Home.UndoCommand.Execute(null);
            });

            Assert.True(program.Until(() => Page.HasGolem), "the undo did not stand the golem up again");
            program.On(() =>
            {
                Assert.True(Page.GolemShown);
                Assert.DoesNotContain(Inventory.Items, tile => tile.Name == "Balrog Spear");
            });
        }
        finally
        {
            Remove("Golemancer");
        }
    }

    [CorpusFact]
    public void The_item_that_gives_the_golem_its_skill_taken_off_takes_the_golem_with_it()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            if (!Open("Gridmancer", by: "Metalgrid"))
                return;

            program.On(() =>
            {
                // The golem stands by the Metalgrid alone.
                Assert.True(Page.HasGolem);
                var worn = Session.Equipped.First(slot => slot.Slot.Location == ItemLocation.Neck && !slot.IsSwap).Item!;
                Assert.Equal("Metalgrid", worn.Name);

                // Off, by a drag onto a free cell of the inventory.
                Assert.True(Page.BeginCarry(worn));
                Assert.True(Session.IsHolding, Session.Status);
                Page.DropCarried(new GridCell(Inventory.Page, Inventory.Columns - 1, 0), pack: false);

                Assert.Equal("Iron Golem dismissed: Balrog Spear in inventory.", Session.Status);
                Assert.False(Page.HasGolem);
                Assert.False(Page.GolemShown);
                Assert.Contains(Inventory.Items, tile => tile.Name == "Metalgrid");
                Assert.Contains(Inventory.Items, tile => tile.Name == "Balrog Spear");
            });
        }
        finally
        {
            Remove("Gridmancer");
        }
    }
}
