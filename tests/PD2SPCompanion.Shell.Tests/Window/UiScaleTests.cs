using Avalonia;

using PD2SPCompanion.Core;
using PD2SPCompanion.Shell.Services.Settings;

namespace PD2SPCompanion.Shell.Tests.Window;

/// <summary>
/// The UI scale, as the settings
/// hold it: the steps Settings offers, a stored number put on the nearest of them, a step chosen in use at
/// once and written, Automatic again written as that, and which scale a screen is given - the step by hand
/// whatever the screen, Automatic's for a screen with less room.
/// <para>
/// The arithmetic of Automatic itself is the Core's (ScreenFitTests). That the window, its tiles and its
/// drop-downs are drawn at the scale, that a step chosen redraws it, and that the window stays within the
/// screen's room are the window's own doing (MainWindow), and the UI cases' to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class UiScaleTests(Program program)
{
    private EditorSettings Settings => program.Get<EditorSettings>();

    /// <summary>PD2SPC_UI_SCALE says the scale instead of the setting: with it set, nothing here is the setting's.</summary>
    private static bool Forced => Environment.GetEnvironmentVariable("PD2SPC_UI_SCALE") is { Length: > 0 };

    private static int IndexOf(int percent) => UiScale.Choices.ToList().IndexOf(percent);

    [CorpusFact]
    public void Settings_offers_Automatic_and_the_steps_from_75_to_125_percent()
    {
        if (!program.Ready())
            return;

        Assert.Equal([0, 75, 80, 85, 90, 100, 110, 125], UiScale.Choices);
        Assert.Equal(["Automatic", "75%", "80%", "85%", "90%", "100%", "110%", "125%"], UiScale.ChoiceNames);
    }

    [CorpusTheory]
    [InlineData(0, 0)]
    [InlineData(-20, 0)]
    [InlineData(80, 80)]
    [InlineData(83, 85)]
    [InlineData(104, 100)]
    [InlineData(40, 75)]
    [InlineData(300, 125)]
    public void A_stored_number_is_put_on_the_nearest_step_and_none_is_Automatic(int stored, int step)
    {
        if (!program.Ready())
            return;

        Assert.Equal(step, UiScale.Nearest(stored));
    }

    [CorpusFact]
    public void A_step_chosen_is_in_use_at_once_and_kept_and_Automatic_again_is_kept_as_that()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            int was = Settings.UiScaleIndex;
            int told = 0;
            void Told() => told++;
            UiScale.Changed += Told;
            try
            {
                // Begun from Automatic, whatever the test before left.
                Settings.UiScaleIndex = 0;
                told = 0;

                // The drop-down's own property: "80%" picked.
                Settings.UiScaleIndex = IndexOf(80);

                Assert.Equal(80, Settings.UiScale);
                Assert.Equal(80, UiScale.Percent);
                Assert.Equal(1, told);
                Assert.Equal(IndexOf(80), Settings.UiScaleIndex);
                Assert.Matches(@"""UiScale"":\s*80\b", File.ReadAllText(EditorSettings.FilePath));

                Settings.UiScaleIndex = 0;

                Assert.Equal(0, Settings.UiScale);
                Assert.Equal(0, UiScale.Percent);
                Assert.Equal(2, told);
                Assert.Matches(@"""UiScale"":\s*0\b", File.ReadAllText(EditorSettings.FilePath));

                // An entry the list does not have changes nothing.
                Settings.UiScaleIndex = UiScale.Choices.Count;
                Assert.Equal(0, Settings.UiScale);
            }
            finally
            {
                UiScale.Changed -= Told;
                Settings.UiScaleIndex = was;
            }
        });
    }

    [CorpusTheory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void A_step_by_hand_is_the_scale_whatever_the_screen_and_Automatic_draws_a_small_screen_smaller(double windows)
    {
        if (!program.Ready() || Forced)
            return;

        // The room of a 1080p screen at 150%, as the UI case tells the window: 1280 x 672 of its units.
        var small = new Size(1280, 672);
        Assert.Equal(0.8, UiScale.For(80, small, windows));
        Assert.Equal(1.25, UiScale.For(125, small, windows));

        double automatic = UiScale.For(0, small, windows);
        Assert.Equal(ScreenFit.Automatic(small.Width, small.Height, windows), automatic);
        Assert.InRange(automatic, 0.75, 0.7999);

        // The reference monitor's own room: left as it is.
        Assert.Equal(1, UiScale.For(0, new Size(ScreenFit.ReferenceWidth, ScreenFit.ReferenceHeight), windows));
    }
}
