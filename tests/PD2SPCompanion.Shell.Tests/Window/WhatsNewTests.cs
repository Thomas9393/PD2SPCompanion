using System.Text.RegularExpressions;

using PD2SPCompanion.Core;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Window;

/// <summary>
/// "What's new" and the About card: started at a newer version than the last run, the
/// release notes open once, with what changed since - behind a question already on screen - and the version
/// is kept as they open; a first run, the same version and a newer one say nothing. The About card's button
/// opens every release; the card says the build, the Game folder line the season, and one line holds what a
/// bug report needs.
/// <para>
/// What the service and the Settings page decide, asked of them as the start and the buttons ask. That the
/// popup is drawn over the window at a real start, that OK closes it, and that the Copy button puts the line
/// on the clipboard - which is the window's - are the UI cases' to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class WhatsNewTests(Program program)
{
    private WhatsNewService Notes => program.Get<WhatsNewService>();
    private EditorSettings Settings => program.Get<EditorSettings>();
    private VP_ViewModel_Settings_L Page => program.Get<VP_ViewModel_Settings_L>();

    /// <summary>
    /// The check at start, as if the last run had been of that version: what it came to is looked at, then
    /// the popup is closed and the version the settings hold put back.
    /// </summary>
    private void Started(string lastRun, Action then) =>
        program.On(() =>
        {
            string was = Settings.LastRunVersion;
            try
            {
                Settings.LastRunVersion = lastRun;
                Notes.CheckAtStart();
                then();
            }
            finally
            {
                Notes.CloseCommand.Execute(null);
                Settings.LastRunVersion = was;
            }
        });

    /// <summary>
    /// With the game's folder in the settings, as a player's are: the run's own begin empty - the program is
    /// told the folder, not the settings - and the Game folder line reads it from there.
    /// </summary>
    private void WithTheGameFolder(Action then) =>
        program.On(() =>
        {
            string was = Settings.GameFolder;
            try
            {
                Settings.GameFolder = Program.GameRoot;
                then();
            }
            finally
            {
                Settings.GameFolder = was;
            }
        });

    [CorpusFact]
    public void Started_at_a_newer_version_the_release_notes_open_by_themselves_with_what_changed_since()
    {
        if (!program.Ready())
            return;

        Started("0.0.9", () =>
        {
            Assert.True(Notes.IsOpen);
            Assert.Equal($"What's new in {AppVersion.Number}", Notes.Title);
            Assert.Contains(Notes.Releases, release => release.Version == "1.0.0");
            Assert.All(Notes.Releases, release => Assert.True(Version.Parse(release.Version) > new Version(0, 0, 9), release.Version));
        });
    }

    [CorpusFact]
    public void Shown_once_the_version_is_kept_as_they_open_and_the_next_start_says_nothing()
    {
        if (!program.Ready())
            return;

        Started("0.0.9", () =>
        {
            Assert.Equal(AppVersion.Number, Settings.LastRunVersion);
            Assert.Matches(@"^\d+\.\d+\.\d+$", Settings.LastRunVersion);
            Assert.Matches($"\"LastRunVersion\":\\s*\"{Regex.Escape(AppVersion.Number)}\"", File.ReadAllText(EditorSettings.FilePath));

            Notes.CloseCommand.Execute(null);
            Assert.False(Notes.IsOpen);

            Notes.CheckAtStart();
            Assert.False(Notes.IsOpen);
        });
    }

    [CorpusFact]
    public void A_first_run_says_nothing_and_keeps_the_version()
    {
        if (!program.Ready())
            return;

        Started("", () =>
        {
            Assert.False(Notes.IsOpen);
            Assert.Equal(AppVersion.Number, Settings.LastRunVersion);
        });
    }

    [CorpusFact]
    public void Last_run_at_the_same_version_or_a_newer_one_nothing_is_said()
    {
        if (!program.Ready())
            return;

        Started(AppVersion.Number, () => Assert.False(Notes.IsOpen));

        // A build older than the one last run - a copy rolled back: nothing to tell, and it is now the last run.
        Started("99.0.0", () =>
        {
            Assert.False(Notes.IsOpen);
            Assert.Equal(AppVersion.Number, Settings.LastRunVersion);
        });
    }

    [CorpusFact]
    public void The_notes_wait_behind_a_question_already_on_screen()
    {
        if (!program.Ready())
            return;

        var confirm = program.Get<ConfirmService>();
        string was = program.On(() => Settings.LastRunVersion);
        try
        {
            program.On(() =>
            {
                confirm.Ask("A question", "Asked first.", "CONTINUE", () => { });
                Settings.LastRunVersion = "0.0.9";
                Notes.CheckAtStart();

                // Not shown, and not yet counted as shown.
                Assert.False(Notes.IsOpen);
                Assert.Equal("0.0.9", Settings.LastRunVersion);

                confirm.RejectCommand.Execute(null);
            });

            Assert.True(program.Until(() => Notes.IsOpen), "the notes did not open once the question was answered");
            program.On(() => Assert.Equal(AppVersion.Number, Settings.LastRunVersion));
        }
        finally
        {
            program.On(() =>
            {
                if (confirm.IsAsking)
                    confirm.RejectCommand.Execute(null);
                Notes.CloseCommand.Execute(null);
                Settings.LastRunVersion = was;
            });
        }
    }

    [CorpusFact]
    public void The_About_cards_button_opens_every_release_and_OK_closes_them()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            try
            {
                Page.OpenWhatsNewCommand.Execute(null);

                Assert.True(Notes.IsOpen);
                Assert.Equal("What's new", Notes.Title);
                Assert.Equal(Changelog.Bundled.Select(release => release.Version), Notes.Releases.Select(release => release.Version));
                Assert.Contains(Notes.Releases, release => release.Version == "1.0.0");

                Notes.CloseCommand.Execute(null);
                Assert.False(Notes.IsOpen);
            }
            finally
            {
                Notes.CloseCommand.Execute(null);
            }
        });
    }

    [CorpusFact]
    public void The_About_card_names_the_program_its_version_and_its_build()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            Assert.Equal("Project Diablo II Single Player Companion", Page.AppName);
            Assert.Matches(@"^\d+\.\d+\.\d+(-\w+)?$", Page.VersionLabel);
            Assert.StartsWith(AppVersion.Number, Page.VersionLabel);
            Assert.StartsWith("Built ", Page.VersionBuilt);
        });
    }

    [CorpusFact]
    public void The_Game_folder_line_says_the_mod_version_and_the_season()
    {
        if (!program.Ready())
            return;

        // "Project Diablo 2 ... \u00B7 S13": the season the open tables say, whichever it is.
        WithTheGameFolder(() => Assert.Matches(@"^Project Diablo 2 .+ \u00B7 S\d+$", Page.GameFolderVersion));
    }

    [CorpusFact]
    public void One_line_holds_what_a_bug_report_needs()
    {
        if (!program.Ready())
            return;

        WithTheGameFolder(() =>
        {
            Assert.Matches(@"^Project Diablo II Single Player Companion \d+\.\d+\.\d+.* \u00B7 Project Diablo 2 .* \u00B7 S\d+ \u00B7 ", Page.BugReportLine);
            Assert.EndsWith(Environment.OSVersion.VersionString, Page.BugReportLine);
        });
    }
}
