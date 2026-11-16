using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Window;

/// <summary>
/// The Experimental page's online notice and the side bar's mark: while the page's
/// files are in the game folder and the notice was never dismissed, the button's tooltip gives the advice,
/// the button is marked and the page shows the notice; its cross dismisses all three for good, written to
/// the settings - and files installed again later do not bring them back.
/// <para>
/// The game folder is never written: "installed" is the page's own word for what it last saw there, said
/// here in its place. That the mark is drawn as a red dot on the button's corner, the notice under the
/// page's buttons, and that the cross takes a click are the UI cases' to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class ExperimentalMarkTests(Program program)
{
    private const string Advice = "Mod files installed. Remove them before playing online.";

    private MainViewModel Main => program.Main;
    private VP_ViewModel_Experimental_L Page => program.Get<VP_ViewModel_Experimental_L>();
    private EditorSettings Settings => program.Get<EditorSettings>();

    /// <summary>The page having seen its files in the game folder, or not, and the notice dismissed before, or not; put back after.</summary>
    private void With(bool installed, bool dismissed, Action then) =>
        program.On(() =>
        {
            bool wasInstalled = Page.IsInstalled, wasDismissed = Settings.OnlineWarningDismissed;
            try
            {
                Settings.OnlineWarningDismissed = dismissed;
                Page.IsInstalled = installed;
                then();
            }
            finally
            {
                Page.IsInstalled = wasInstalled;
                Settings.OnlineWarningDismissed = wasDismissed;
            }
        });

    [CorpusFact]
    public void With_nothing_installed_the_button_says_only_its_name_and_key_and_is_not_marked()
    {
        if (!program.Ready())
            return;

        With(installed: false, dismissed: false, () =>
        {
            Assert.Equal("Experimental (Ctrl + 5)", Main.ExperimentalTip);
            Assert.False(Main.ExperimentalMarked);
            Assert.False(Page.ShowsOnlineNotice);
        });
    }

    [CorpusFact]
    public void While_installed_and_never_dismissed_the_tooltip_gives_the_advice_the_button_is_marked_and_the_page_shows_it()
    {
        if (!program.Ready())
            return;

        With(installed: true, dismissed: false, () =>
        {
            Assert.Equal(Advice, VP_ViewModel_Experimental_L.OnlineNotice);
            Assert.Equal("Experimental (Ctrl + 5)" + Environment.NewLine + Advice, Main.ExperimentalTip);
            Assert.True(Main.ExperimentalMarked);
            Assert.True(Page.ShowsOnlineNotice);
        });
    }

    [CorpusFact]
    public void The_cross_dismisses_the_notice_the_mark_and_the_advice_for_good_and_the_settings_say_so()
    {
        if (!program.Ready())
            return;

        With(installed: true, dismissed: false, () =>
        {
            var told = new List<string?>();
            void Told(object? sender, System.ComponentModel.PropertyChangedEventArgs changed) => told.Add(changed.PropertyName);
            Main.PropertyChanged += Told;
            try
            {
                Page.DismissOnlineNoticeCommand.Execute(null);
            }
            finally
            {
                Main.PropertyChanged -= Told;
            }

            Assert.False(Page.ShowsOnlineNotice);
            Assert.False(Main.ExperimentalMarked);
            Assert.Equal("Experimental (Ctrl + 5)", Main.ExperimentalTip);

            // The side bar is told, so the dot and the tooltip's second line go as the notice does.
            Assert.Contains(nameof(MainViewModel.ExperimentalMarked), told);
            Assert.Contains(nameof(MainViewModel.ExperimentalTip), told);

            Assert.True(Settings.OnlineWarningDismissed);
            Assert.Matches(@"""OnlineWarningDismissed"":\s*true", File.ReadAllText(EditorSettings.FilePath));

            // Removed and applied again later: said once, ever.
            Page.IsInstalled = false;
            Page.IsInstalled = true;
            Assert.False(Page.ShowsOnlineNotice);
            Assert.False(Main.ExperimentalMarked);
        });
    }
}
