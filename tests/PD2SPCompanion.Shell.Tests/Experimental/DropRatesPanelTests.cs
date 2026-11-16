using System.Text.Json;

using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Experimental;

/// <summary>
/// The Experimental page's drop rates panel:
/// the page's field names the preset; the panel opens with its presets open and its lists folded; multiples fill
/// the rows with their odds before and after; a preset is kept under a name, chosen again, updated in place and
/// deleted by its bin, which asks first - each written to the settings - and stays gone when they are read again.
/// <para>
/// What the panel decides, asked as its view asks (its commands, a row's box) and read back from the panel and
/// from the settings file. APPLY is never asked: nothing reaches the game folder. That the page's field opens
/// the panel, and that a box, the Keep button and a row's bin reach it, are the UI cases' (units/drop-panel.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class DropRatesPanelTests(Program program)
{
    private const string Kept = "UI drop preset", Puzzlebox = "Larzuk's Puzzlebox";

    private VP_ViewModel_Experimental_L Page => program.Get<VP_ViewModel_Experimental_L>();
    private DropPanelViewModel Panel => Page.DropPanel;
    private EditorSettings Settings => program.Get<EditorSettings>();

    private DropPresetRow Preset(string name) => Panel.Saved.First(row => row.Name == name);

    private bool Lists(string name) => Panel.Saved.Any(row => row.Name == name);

    private DropRowViewModel Row(string name) => Panel.Groups.SelectMany(group => group.Rows).First(row => row.Name == name);

    /// <summary>The settings file's kept presets, as the file has them.</summary>
    private static List<JsonElement> InFile()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(EditorSettings.FilePath));
        return [.. document.RootElement.GetProperty("DropPresets").EnumerateArray().Select(preset => preset.Clone())];
    }

    private static bool FileHolds(string name) => InFile().Any(preset => preset.GetProperty("Name").GetString() == name);

    /// <summary>
    /// The panel on the game as shipped, open; and, at the end, the page's multiples and the settings' presets
    /// as the test found them - both are every test's.
    /// </summary>
    private IDisposable Opened()
    {
        var (boosts, presets) = program.On(() =>
        {
            var before = (new Dictionary<string, int>(Panel.Boosts), Settings.DropPresets.ToList());
            Assert.DoesNotContain(Settings.DropPresets, preset => preset.Name == Kept);

            Panel.Take(null);
            Panel.NameText = "";
            Panel.OpenCommand.Execute(null);
            Assert.True(Panel.IsOpen, $"the panel did not open: {Panel.Problem}");
            return before;
        });

        return new Back(() => program.On(() =>
        {
            Settings.DropPresets.Clear();
            foreach (var preset in presets)
                Settings.DropPresets.Add(preset);
            Settings.Save();

            // Opened once more: the panel reads its presets as it opens.
            Panel.Take(boosts);
            Panel.NameText = "";
            Panel.OpenCommand.Execute(null);
            Panel.CloseCommand.Execute(null);
        }));
    }

    private sealed class Back(Action back) : IDisposable
    {
        public void Dispose() => back();
    }

    /// <summary>The Puzzlebox at 20x, kept under the test's name.</summary>
    private void Keep()
    {
        Row(Puzzlebox).MultipleText = "20";
        Panel.NameText = Kept;
        Panel.KeepCommand.Execute(null);
    }

    [CorpusFact]
    public void The_field_names_the_preset_and_the_panel_opens_on_its_presets_with_the_lists_folded()
    {
        if (!program.Ready())
            return;

        using var opened = Opened();
        program.On(() =>
        {
            // The page's field: the game as shipped.
            Assert.Equal("Vanilla", Panel.CurrentPresetName);

            Assert.True(Panel.PresetsOpen);
            Assert.Equal("Vanilla", Panel.Saved[0].Name);
            Assert.True(Panel.Saved[0].IsCurrent);

            Assert.Equal(["Special items", "Corrupted zones and maps", "Runes and gems"], Panel.Groups.Select(group => group.Title));
            Assert.All(Panel.Groups, group => Assert.False(group.IsOpen, $"{group.Title} is unfolded"));
        });
    }

    [CorpusFact]
    public void Multiples_fill_the_rows_with_their_odds_before_and_after()
    {
        if (!program.Ready())
            return;

        using var opened = Opened();
        program.On(() =>
        {
            Panel.Take(new Dictionary<string, int> { ["lbox"] = 84, ["rtmo"] = 23 });

            // The Puzzlebox per kill; the jawbone per undead kill, 23x.
            Assert.Contains("Nightmare 1 in 450,000 → 1 in 5,400", Row(Puzzlebox).Odds);
            Assert.Single(Panel.Groups.SelectMany(group => group.Rows),
                          row => row.Odds.StartsWith("Per undead kill: ", StringComparison.Ordinal) && row.Odds.Contains("Hell 1 in 22,500 → 1 in 980"));
        });
    }

    [CorpusFact]
    public void A_multiple_typed_makes_it_no_preset_and_kept_under_a_name_it_is_in_the_settings()
    {
        if (!program.Ready())
            return;

        using var opened = Opened();
        program.On(() =>
        {
            Row(Puzzlebox).MultipleText = "20";
            Assert.Equal(20, Row(Puzzlebox).Multiple);
            Assert.Equal("No preset", Panel.CurrentPresetName);

            Panel.NameText = Kept;
            Assert.Equal("Keep preset", Panel.KeepHint);
            Assert.True(Panel.KeepCommand.CanExecute(null));

            Panel.KeepCommand.Execute(null);
            Assert.True(Lists(Kept));
            Assert.True(Preset(Kept).IsCurrent);
            Assert.Equal(Kept, Panel.CurrentPresetName);
            Assert.True(FileHolds(Kept), "the kept preset is not in the settings file");

            // Kept under that name already: nothing more to keep.
            Assert.False(Panel.KeepCommand.CanExecute(null));
        });
    }

    [CorpusFact]
    public void A_kept_preset_chosen_again_brings_its_multiples_back_and_kept_under_its_name_is_updated_in_place()
    {
        if (!program.Ready())
            return;

        using var opened = Opened();
        program.On(() =>
        {
            Keep();

            Panel.ChooseCommand.Execute(Preset("Vanilla"));
            Assert.Equal(1, Row(Puzzlebox).Multiple);
            Assert.Equal("", Panel.NameText);

            Panel.ChooseCommand.Execute(Preset(Kept));
            Assert.Equal(20, Row(Puzzlebox).Multiple);
            Assert.Equal(Kept, Panel.NameText);

            Row(Puzzlebox).MultipleText = "3";
            Assert.Equal("Update preset", Panel.KeepHint);
            Assert.True(Panel.KeepCommand.CanExecute(null));
            Panel.KeepCommand.Execute(null);

            var kept = Assert.Single(InFile(), preset => preset.GetProperty("Name").GetString() == Kept);
            Assert.Equal(3, kept.GetProperty("Boosts").GetProperty("lbox").GetInt32());
            Assert.Single(Panel.Saved, row => row.Name == Kept);
        });
    }

    [CorpusFact]
    public void A_presets_bin_asks_first_and_deletes_on_the_second_press_and_Vanilla_has_none()
    {
        if (!program.Ready())
            return;

        using var opened = Opened();
        program.On(() =>
        {
            Keep();

            Panel.ForgetCommand.Execute(Preset(Kept));
            Assert.True(Preset(Kept).IsArmed);
            Assert.True(FileHolds(Kept), "one press deleted the preset");

            Panel.ForgetCommand.Execute(Preset(Kept));
            Assert.False(Lists(Kept));
            Assert.False(FileHolds(Kept), "the preset is still in the settings file");

            // The built-in one is neither renamed nor deleted.
            Assert.False(Preset("Vanilla").CanForget);
            Panel.ForgetCommand.Execute(Preset("Vanilla"));
            Panel.ForgetCommand.Execute(Preset("Vanilla"));
            Assert.True(Lists("Vanilla"));
        });
    }

    [CorpusFact]
    public void A_kept_preset_deleted_stays_gone_when_the_settings_are_read_again()
    {
        if (!program.Ready())
            return;

        using var opened = Opened();
        program.On(() =>
        {
            Keep();
            Assert.True(Preset(Kept).CanForget);
            Panel.ForgetCommand.Execute(Preset(Kept));
            Panel.ForgetCommand.Execute(Preset(Kept));

            Assert.False(Lists(Kept));
            Assert.False(FileHolds(Kept));

            // Closed: the page's field says what the multiples are now - those of no preset.
            Panel.CloseCommand.Execute(null);
            Assert.False(Panel.IsOpen);
            Assert.Equal("No preset", Panel.CurrentPresetName);

            // Read again, as a start reads them. Reading hands the values to the program's parts and takes the
            // folds' memory: both given back to the settings the program runs on.
            try
            {
                Assert.DoesNotContain(EditorSettings.Load().DropPresets, preset => preset.Name == Kept);
            }
            finally
            {
                FoldMemory.Settings = Settings;
                Settings.Apply();
            }
        });
    }

    [CorpusFact]
    public void A_multiples_stepper_takes_Ctrl_for_ten_times_or_a_tenth_and_Shift_for_the_end_of_the_scale()
    {
        // As every stepper takes them (the key sheet says so).
        Assert.Equal(50, DropSteps.Leap(5, up: true));
        Assert.Equal(DropSteps.Most, DropSteps.Leap(500, up: true));
        Assert.Equal(5, DropSteps.Leap(50, up: false));
        Assert.Equal(1, DropSteps.Leap(5, up: false));
        Assert.Equal(DropSteps.Most, DropSteps.End(up: true));
        Assert.Equal(1, DropSteps.End(up: false));
    }
}
