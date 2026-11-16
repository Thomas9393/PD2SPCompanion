using PD2SPCompanion.Core.Storage;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Search;

/// <summary>
/// The search's presets (in the panel, with no second one opening up): the
/// current preset on the section's header - "No preset" while the search is none of them; the name box and its
/// Keep, which keeps the search as it stands, updates the kept preset of that name, or renames the preset the
/// search already is; a kept preset clicked is the search at once, and nothing closes; a preset's bin asks on
/// its first press and deletes on its second. Kept, updated and forgotten in the settings as it is done.
/// <para>
/// What the panel decides, asked through its commands and read back from the search, the list and the settings
/// - the run's own, and the presets a test keeps are forgotten again. Where the section is drawn in the panel,
/// and that a click reaches Keep, a kept row and its bin, are the UI cases' to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class SearchPresetsTests(Program program)
{
    private const string Kept = "Unit test charms", Renamed = "Unit test charms, renamed";

    private MainViewModel Main => program.Main;
    private SearchWindowViewModel Window => Main.SearchWindow!;
    private SearchPanelViewModel Panel => Window.Panel;
    private EditorSettings Settings => Panel.Settings;

    private SearchPresetRow? Row(string name) => Panel.Saved.FirstOrDefault(row => row.Name == name);

    /// <summary>The search asking for a name, and kept as a preset under the test's name.</summary>
    private void Keep(string asked, string name = Kept)
    {
        Panel.Filter.Name = asked;
        Panel.NameText = name;
        Assert.True(Panel.KeepCommand.CanExecute(null));
        Panel.KeepCommand.Execute(null);
    }

    /// <summary>Done on the program's thread with the search's page up, and the presets it kept forgotten after.</summary>
    private void WithThePanel(Action work) => program.On(() =>
    {
        try
        {
            Main.ShowSearch(search => search.Open());
            work();
        }
        finally
        {
            // By the bin, as a player forgets one: its first press asks, its second deletes.
            foreach (string name in new[] { Kept, Renamed })
            {
                for (int press = 0; press < 2 && Row(name) is { } mine; press++)
                    Panel.ForgetCommand.Execute(mine);
            }

            Panel.NameText = "";
            Window.End();
        }
    });

    [CorpusFact]
    public void With_nothing_kept_the_header_says_No_preset_and_Keep_waits_for_a_name()
    {
        if (!program.Ready())
            return;

        WithThePanel(() =>
        {
            Assert.Equal("No preset", Panel.CurrentPresetName);
            Assert.Equal("", Panel.Asked);

            // A search to keep, and no name for it yet.
            Panel.Filter.Name = "Charm";
            // What is asked, in a line: the Conditions heading says it, not the presets' own section.
            Assert.Contains("Charm", Panel.Asked);
            Assert.False(Panel.HasSummary);
            Assert.False(Panel.KeepCommand.CanExecute(null));

            Panel.NameText = Kept;
            Assert.True(Panel.KeepCommand.CanExecute(null));
            Assert.Equal("Keep preset", Panel.KeepHint);
        });
    }

    [CorpusFact]
    public void A_search_kept_is_in_the_list_on_the_header_and_in_the_settings()
    {
        if (!program.Ready())
            return;

        WithThePanel(() =>
        {
            Keep("Charm");

            var row = Row(Kept);
            Assert.NotNull(row);
            Assert.True(row.IsCurrent);
            Assert.Contains("Charm", row.Summary);
            Assert.Equal(Kept, Panel.CurrentPresetName);
            Assert.True(Panel.HasSaved);

            // The search is this preset now: nothing left for Keep to do under the same name.
            Assert.False(Panel.KeepCommand.CanExecute(null));

            // Written as it is kept.
            Assert.Single(Settings.SearchPresets, preset => preset.Name == Kept);
            Assert.Contains(Kept, File.ReadAllText(EditorData.SettingsFile));
        });
    }

    [CorpusFact]
    public void A_kept_preset_clicked_is_the_search_at_once_and_nothing_closes()
    {
        if (!program.Ready())
            return;

        WithThePanel(() =>
        {
            Keep("Charm");

            // The search cleared: no preset.
            Panel.Filter.Name = "";
            Assert.Equal("No preset", Panel.CurrentPresetName);
            Assert.False(Row(Kept)!.IsCurrent);
            Panel.NameText = "";

            Panel.ChooseCommand.Execute(Row(Kept));

            Assert.Equal("Charm", Panel.Filter.Name);
            Assert.Equal(Kept, Panel.CurrentPresetName);
            Assert.True(Row(Kept)!.IsCurrent);
            Assert.Equal(Kept, Panel.NameText);

            // Nothing closes: the page, its conditions and the presets' section are as they were.
            Assert.True(Window.IsShown);
            Assert.True(Window.ShowsConditions);
            Assert.True(Panel.PresetsOpen);
        });
    }

    [CorpusFact]
    public void Another_search_kept_under_the_same_name_updates_the_preset_in_place()
    {
        if (!program.Ready())
            return;

        WithThePanel(() =>
        {
            Keep("Charm");

            // The name box still names it, and the search is another: Keep says Update.
            Panel.Filter.Name = "Ring";
            Assert.Equal("Update preset", Panel.KeepHint);
            Assert.True(Panel.KeepCommand.CanExecute(null));
            Panel.KeepCommand.Execute(null);

            Panel.Filter.Name = "";
            Panel.ChooseCommand.Execute(Row(Kept));
            Assert.Equal("Ring", Panel.Filter.Name);

            // In place: one preset of that name, in the list and in the settings.
            Assert.Single(Panel.Saved, row => row.Name == Kept);
            Assert.Single(Settings.SearchPresets, preset => preset.Name == Kept);
            string written = File.ReadAllText(EditorData.SettingsFile);
            Assert.Equal(written.IndexOf(Kept, StringComparison.Ordinal), written.LastIndexOf(Kept, StringComparison.Ordinal));
            Assert.True(written.Contains(Kept, StringComparison.Ordinal));
        });
    }

    [CorpusFact]
    public void Another_name_for_the_preset_the_search_already_is_renames_it()
    {
        if (!program.Ready())
            return;

        WithThePanel(() =>
        {
            Keep("Charm");

            Panel.NameText = Renamed;
            Assert.Equal("Rename preset", Panel.KeepHint);
            Panel.KeepCommand.Execute(null);

            Assert.Null(Row(Kept));
            Assert.NotNull(Row(Renamed));
            Assert.Equal(Renamed, Panel.CurrentPresetName);
            Assert.Single(Settings.SearchPresets, preset => preset.Name is Kept or Renamed);
        });
    }

    [CorpusFact]
    public void A_presets_bin_asks_on_its_first_press_and_deletes_on_its_second()
    {
        if (!program.Ready())
            return;

        WithThePanel(() =>
        {
            Keep("Charm");
            var row = Row(Kept)!;
            Assert.False(row.IsArmed);

            Panel.ForgetCommand.Execute(row);
            Assert.True(row.IsArmed);
            Assert.NotNull(Row(Kept));
            Assert.Single(Settings.SearchPresets, preset => preset.Name == Kept);

            Panel.ForgetCommand.Execute(row);
            Assert.Null(Row(Kept));
            Assert.DoesNotContain(Settings.SearchPresets, preset => preset.Name == Kept);
            Assert.DoesNotContain(Kept, File.ReadAllText(EditorData.SettingsFile));

            // The search it was stays as it is, and is no preset now.
            Assert.Equal("Charm", Panel.Filter.Name);
            Assert.Equal("No preset", Panel.CurrentPresetName);
        });
    }
}
