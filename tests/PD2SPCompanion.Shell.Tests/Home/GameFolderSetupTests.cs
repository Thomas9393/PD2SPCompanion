using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;

namespace PD2SPCompanion.Shell.Tests.Home;

/// <summary>
/// The first-run screen (GameFolderSetup): what it says of a folder, the play mode chosen beside it, CONTINUE settling
/// the folder, and the screen a folder that has since gone brings back. The fixture says the game's folder for itself,
/// so this is the one place a first run's own road is asked.
/// <para>
/// On settings and a session of the test's own: the program's stay as they are, and only the settings file, which a
/// fresh settings object writes, is put back. CONTINUE only points a session at the folder; that the tables then open
/// is the same EnsureGame every test's character has been through, and is not asked again here. That the overlay is
/// drawn and CONTINUE takes a click is a window's to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class GameFolderSetupTests(Program program)
{
    /// <summary>A setup over settings that hold <paramref name="folder"/>, and the program's settings file and statics put back when it goes.</summary>
    private sealed class Screen : IDisposable
    {
        private readonly Program _program;

        public EditorSettings Settings { get; }
        public CharacterSession Session { get; }
        public GameFolderSetup Setup { get; }

        public Screen(Program program, string folder = "")
        {
            _program = program;
            Settings = new EditorSettings { GameFolder = folder };
            Session = new CharacterSession(Settings);
            Setup = new GameFolderSetup(Settings, Session);
        }

        /// <summary>Let go of on the program's thread, or from the test's: asking the program from its own thread would wait on itself.</summary>
        public void Dispose()
        {
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                PutBack();
            else
                _program.On(PutBack);
        }

        /// <summary>The save folder's watcher let go; the file a new settings object saved over and the statics its mode set.</summary>
        private void PutBack()
        {
            Session.UseGameFolder("");
            var own = _program.Get<EditorSettings>();
            own.Save();
            own.Apply();
        }
    }

    /// <summary>A new folder under the run's own: nothing of the game's, and the game's real folder never asked to change.</summary>
    private string Scratch()
    {
        string folder = Path.Combine(program.Folder, "first-run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    [CorpusFact]
    public void A_first_run_asks_with_the_mode_beside_it_and_no_reason_line()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            using var screen = new Screen(program);
            var setup = screen.Setup;

            Assert.True(setup.IsAsking);
            Assert.True(setup.IsShown);
            Assert.Equal(1, setup.Fade);
            Assert.True(setup.AsksMode);
            Assert.Equal("", setup.Reason);
            Assert.False(setup.HasReason);

            // The best guess in the box: the first found, or nothing; one found is no list.
            Assert.Equal(setup.Found.FirstOrDefault() ?? "", setup.Folder);
            Assert.Equal(setup.Found.Count > 1, setup.ShowsFound);
        });
    }

    [CorpusFact]
    public void A_folder_that_will_not_do_says_why_and_CONTINUE_settles_nothing()
    {
        if (!program.Ready())
            return;

        string empty = Scratch();
        string baseOnly = Scratch();
        File.WriteAllText(Path.Combine(baseOnly, "d2data.mpq"), "");

        program.On(() =>
        {
            using var screen = new Screen(program);
            var setup = screen.Setup;

            void Refused(string typed, string why, bool shown = true)
            {
                setup.Folder = typed;
                Assert.Equal(why, setup.Problem);
                Assert.Equal(shown, setup.HasProblem);
                Assert.False(setup.CanContinue, typed);
                Assert.Equal("", setup.FolderVersion);

                setup.AcceptCommand.Execute(null);
                Assert.True(setup.IsAsking, $"{typed} was settled on");
                Assert.Equal("", screen.Settings.GameFolder);
                Assert.Equal("", screen.Session.GameRoot);
            }

            // Nothing typed: dim, with nothing said under the box yet.
            Refused("", "No folder.", shown: false);
            Refused(Path.Combine(empty, "not-there"), "Folder not found.");
            Refused(empty, "Invalid folder, requires folder containing d2data.mpq.");
            Refused(baseOnly, "Project Diablo 2 installation not found.");
        });
    }

    [CorpusFact]
    public void The_ProjectD2_folder_picked_is_the_game_folder_around_it()
    {
        if (!program.Ready())
            return;

        using var screen = program.On(() => new Screen(program));
        program.On(() => screen.Setup.Folder = Path.Combine(Program.GameRoot, "ProjectD2"));

        // Posted, as a box still pushing what was typed would ignore it otherwise.
        Assert.True(program.Until(() => screen.Setup.Folder == Program.GameRoot), "the box stayed on ProjectD2");
        program.On(() =>
        {
            Assert.Equal("", screen.Setup.Problem);
            Assert.True(screen.Setup.CanContinue);
        });
    }

    [CorpusFact]
    public void Choosing_a_mode_stores_its_preset_and_says_what_it_is()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            using var screen = new Screen(program);
            var setup = screen.Setup;

            Assert.True(setup.IsCasual);
            Assert.Equal("Game-legal editing.", setup.ModeNote);

            setup.ChooseModeCommand.Execute(PlayPolicy.LegitName);
            Assert.Equal(PlayPolicy.LegitName, screen.Settings.PlayMode);
            Assert.Equal((true, false, false), (setup.IsLegit, setup.IsCasual, setup.IsFree));
            Assert.Equal("Vault, compendium and trade. No editing.", setup.ModeNote);

            setup.ChooseModeCommand.Execute(PlayPolicy.FreeName);
            Assert.Equal(PlayPolicy.FreeName, screen.Settings.PlayMode);
            Assert.Equal((false, false, true), (setup.IsLegit, setup.IsCasual, setup.IsFree));
            Assert.Equal("Unrestricted editing.", setup.ModeNote);

            // No preset of that name: the mode stands.
            setup.ChooseModeCommand.Execute("Nonsense");
            Assert.Equal(PlayPolicy.FreeName, screen.Settings.PlayMode);
        });
    }

    [CorpusFact]
    public void CONTINUE_settles_the_folder_tidied_and_the_screen_goes()
    {
        if (!program.Ready())
            return;

        using var screen = program.On(() => new Screen(program));
        program.On(() =>
        {
            var setup = screen.Setup;
            setup.Folder = Program.GameRoot + Path.DirectorySeparatorChar;
            Assert.True(setup.CanContinue);

            setup.AcceptCommand.Execute(null);

            // The launchers write the separator, a picker does not: settled without it.
            Assert.Equal(Program.GameRoot, screen.Settings.GameFolder);
            Assert.True(screen.Settings.HasGameFolder);
            Assert.Equal(Program.GameRoot, screen.Session.GameRoot);
            Assert.True(screen.Session.IsConfigured);
            Assert.Equal(Program.GameRoot, EditorSettings.StoredGameFolder());

            // The question goes at once; the overlay with its fade.
            Assert.False(setup.IsAsking);
            Assert.Equal(0, setup.Fade);
        });
        Assert.True(program.Until(() => !screen.Setup.IsShown), "the overlay stayed");
    }

    [CorpusFact]
    public void A_settled_folder_that_has_gone_asks_again_says_which_and_leaves_the_mode_alone()
    {
        if (!program.Ready())
            return;

        string gone = Path.Combine(program.Folder, "was-the-game");
        program.On(() =>
        {
            using var screen = new Screen(program, gone);
            var setup = screen.Setup;

            Assert.True(setup.IsAsking);
            Assert.Equal($"{gone} not found.", setup.Reason);
            Assert.True(setup.HasReason);
            Assert.False(setup.AsksMode);

            // Nothing settled until it is answered.
            Assert.Equal(gone, screen.Settings.GameFolder);
        });
    }

    [CorpusFact]
    public void A_settled_folder_that_is_there_asks_nothing()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            using var screen = new Screen(program, Program.GameRoot);
            Assert.False(screen.Setup.IsAsking);
            Assert.False(screen.Setup.IsShown);
        });
    }
}
