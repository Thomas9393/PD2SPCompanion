using System.Text.RegularExpressions;

using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Core.Storage;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Items;

/// <summary>
/// The item panel's property picker, one line per class, tree and skill as the trade site has them:
/// a skill-tree line, a class line and a single-skill line picked on a ring, each row arriving
/// with its tree, class or skill already chosen; searching the id still finds every line; the line that named
/// one tree for the whole stat is gone. And the play mode's "Raw property list" (Custom's alone): off in Free,
/// kept with the mode when it is switched on, and then the picker lists each stat once by its engine name,
/// the game's words still finding it, a pick adding the stat with its tree to choose.
/// <para>
/// What the picker lists and what a pick adds, asked of the Edit page's own panel over the pad's ring, and
/// the Custom panel's switch asked as the Settings page asks it. That the picker opens over the page, a click
/// reaches a line and the switch, and the rows are drawn, is the UI cases' to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class PropertyLinesTests(Program program)
{
    private const string Ring = "Steel Ring of Skill";

    private MainViewModel Main => program.Main;
    private VP_ViewModel_Inv_L Page => Main.Inventory!;
    private EditorSettings Settings => program.Get<EditorSettings>();

    /// <summary>The Edit page's panel beside the inventory: the character's own.</summary>
    private VP_ViewModel_Inv_R Panel => program.Get<VP_ViewModel_Vault_R>().Editor;

    private PropertyPickerViewModel Picker => Panel.StatPicker;

    private List<StatChoice> Listed(string typed)
    {
        Panel.OpenStatPickerCommand.Execute(null);
        Picker.Search = typed;
        return [.. Picker.Choices.Cast<StatChoice>()];
    }

    /// <summary>The picker opened, searched and the line of those words picked - which adds it and closes the picker.</summary>
    private void Pick(string typed, string line)
    {
        var choice = Listed(typed).First(each => each.Text == line);
        Picker.PickCommand.Execute(choice);
        Assert.False(Picker.IsOpen);
    }

    /// <summary>The pad's ring, in the personal stash, selected: the Edit page is over it.</summary>
    private void SelectTheRing() =>
        Page.ClickTileCommand.Execute(
            Page.Sections.First(section => section.Pages is not null)
                .Pages!.First(page => page.Label == "Personal Stash").Grid!.Items.First(tile => tile.Name == Ring));

    /// <summary>
    /// Done in a play mode, and the mode the run had put back after - with any custom mode the test kept
    /// forgotten again, and nothing left selected.
    /// </summary>
    private void In(string mode, Action work)
    {
        var (was, name, kept) = program.On(() => (Settings.PlayMode, Settings.CustomName, Settings.CustomPresets.ToList()));
        try
        {
            program.On(() => Settings.ChoosePolicy(PlayPolicy.Preset(mode)!));
            work();
        }
        finally
        {
            program.On(() =>
            {
                Picker.IsOpen = false;
                Page.ClearSelection();

                if (PlayPolicy.Preset(was) is { } preset)
                    Settings.ChoosePolicy(preset);
                else
                    Settings.ChooseCustom(name);

                foreach (var made in Settings.CustomPresets.Except(kept).ToList())
                    Settings.ForgetCustom(made);

                Settings.CustomName = name;
                Settings.Save();
            });
        }
    }

    /// <summary>The Custom panel opened on the mode as it is, its "Raw property list" switch turned on, and confirmed.</summary>
    private void SwitchTheRawListOn()
    {
        var mode = program.Get<VP_ViewModel_Settings_R>().PlayMode;
        mode.OpenCustomCommand.Execute(null);
        mode.Rows.First(row => row.Title == "Raw property list").IsOn = true;
        mode.ConfirmCustomCommand.Execute(null);
    }

    [CorpusFact]
    public void Searching_an_id_lists_every_line_of_the_stat_each_saying_its_id_and_none_names_one_tree_for_them_all()
    {
        if (!program.Ready())
            return;

        In(PlayPolicy.FreeName, () => program.On(() =>
        {
            SelectTheRing();
            Assert.True(Panel.CanAddStat);

            var lines = Listed("188");

            // The stat's own lines first, a line a tree; whatever else begins with 188 after them.
            var trees = lines.Where(line => line.Id == 188).ToList();
            Assert.True(trees.Count >= 3, $"{trees.Count} lines of stat 188");
            Assert.All(trees, line => Assert.Equal("stat 188", line.IdText));
            Assert.All(trees, line => Assert.Matches(@"^\+X to .+ \(.+ Only\)$", line.Text));
            Assert.Equal(trees.Count, trees.Select(line => line.Text).Distinct().Count());
            Assert.Equal(188, lines[0].Id);

            Assert.DoesNotContain(lines, line => line.Text == "+X to Javelin and Spear Skills");
            Assert.Contains(lines, line => line.Text == "+X to Javelin and Spear Skills (Amazon Only)");
        }));
    }

    [CorpusFact]
    public void A_tree_line_a_class_line_and_a_skill_line_each_arrive_with_theirs_chosen()
    {
        if (!program.Ready(changes: true))
            return;

        In(PlayPolicy.FreeName, () =>
        {
            program.On(() =>
            {
                SelectTheRing();
                Pick("Bow and Crossbow", "+X to Bow and Crossbow Skills (Amazon Only)");
            });
            Assert.True(program.Until(() => Panel.Stats.Any(row => row.InGame == "+1 to Bow and Crossbow Skills (Amazon Only)")), "the tree line did not add its tree");

            program.On(() => Pick("Sorceress Skill Levels", "+X to Sorceress Skill Levels"));
            Assert.True(program.Until(() => Panel.Stats.Any(row => row.InGame == "+1 to Sorceress Skill Levels")), "the class line did not add its class");

            program.On(() => Pick("Blizzard Sorceress Only", "+X to Blizzard (Sorceress Only)"));
            Assert.True(program.Until(() => Panel.Stats.Any(row => row.InGame == "+1 to Blizzard (Sorceress Only)")), "the skill line did not add its skill");
            // Written a moment after: the page's write waits its turn behind the change.
            PD2SPCompanion.Shell.Tests.Edit.EditPad.Settled(program, Panel);

            program.On(() =>
            {
                // The warning above the rows is worded from the same stat, so it says the skill as plainly.
                Assert.Contains(Panel.Findings, finding => finding.Said == "Nothing on this item accounts for +1 to Blizzard (Sorceress Only).");

                // Each is the ring's own at once: nothing waits to be kept.
                Assert.False(Panel.HasChanges);
                var held = Panel.Summary!.Summed.Select(line => line.Text).ToList();
                Assert.Contains("+1 to Bow and Crossbow Skills (Amazon Only)", held);
                Assert.Contains("+1 to Sorceress Skill Levels", held);
                Assert.Contains("+1 to Blizzard (Sorceress Only)", held);
            });
        });
    }

    [CorpusFact]
    public void The_raw_property_list_is_Customs_alone_off_in_Free_and_kept_with_the_mode_when_switched_on()
    {
        if (!program.Ready())
            return;

        In(PlayPolicy.FreeName, () => program.On(() =>
        {
            var mode = program.Get<VP_ViewModel_Settings_R>().PlayMode;
            Assert.False(Panel.Rules.ListsRawProperties);

            mode.OpenCustomCommand.Execute(null);
            var raw = mode.Rows.First(row => row.Title == "Raw property list");
            Assert.True(raw.IsSwitch);
            Assert.False(raw.IsOn);

            raw.IsOn = true;
            Assert.True(raw.IsChanged);
            Assert.Equal("Free, 1 change", mode.ChangesText);

            mode.ConfirmCustomCommand.Execute(null);

            // The mode is Custom then, as no preset has it - live, with no restart.
            Assert.False(mode.IsPanelOpen);
            Assert.Equal(PlayPolicy.CustomName, Settings.PlayMode);
            Assert.True(mode.IsCustom);
            Assert.True(Panel.Rules.ListsRawProperties);
            Assert.Matches("\"RawProperties\": true", File.ReadAllText(EditorData.SettingsFile));
        }));
    }

    [CorpusFact]
    public void With_the_raw_list_on_the_picker_lists_each_stat_once_by_its_engine_name_and_a_pick_adds_it()
    {
        if (!program.Ready(changes: true))
            return;

        In(PlayPolicy.FreeName, () =>
        {
            program.On(() =>
            {
                SwitchTheRawListOn();
                SelectTheRing();

                var lines = Listed("188");
                var stat = Assert.Single(lines, line => line.Id == 188);
                Assert.Equal("item_addskill_tab", stat.Text);
                Assert.DoesNotContain(lines, line => Regex.IsMatch(line.Text, @"^\+X to .+ Skills \(.+ Only\)$"));

                // The game's words still find it.
                Assert.Contains(Listed("Javelin and Spear"), line => line.Text == "item_addskill_tab");

                Pick("Javelin and Spear", "item_addskill_tab");
            });

            Assert.True(program.Until(() => Panel.Stats.Any(row => row.Name == "item_addskill_tab")), "the pick did not add the stat");
            program.On(() =>
            {
                // Its tree is the row's to choose.
                var row = Panel.Stats.First(each => each.Name == "item_addskill_tab");
                Assert.True(row.HasIndex);
                Assert.Equal("Skill tree", row.IndexLabel);
            });
        });
    }
}
