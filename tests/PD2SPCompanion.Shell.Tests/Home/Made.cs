using PD2SPCompanion.Core.Characters;
using PD2SPCompanion.Core.GameFiles;
using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Shell.Services.Settings;

namespace PD2SPCompanion.Shell.Tests.Home;

/// <summary>
/// What a test of the home page, the Character page or the guards needs beyond the fixture's two characters:
/// a character made for it - as the command line tool's <c>create</c> made "Grave", "Carder", "Stepper" and
/// "Quester" for the old scripts, on the fixture's own PhySpearZon as the template - in the run's save folder,
/// and gone again after; and the play mode the test runs in, put back after.
/// </summary>
internal static class Made
{
    /// <summary>
    /// A new character's file in the run's save folder. Asked on the program's thread; the parser is left in
    /// the open character's layout, as every reader of another file leaves it.
    /// </summary>
    public static string Character(Program program, string name, string className, int level, bool hardcore = false, bool dead = false)
    {
        var session = program.Main.Session;
        int classId = CharacterClasses.Names.ToList().FindIndex(each => each.Equals(className, StringComparison.OrdinalIgnoreCase));
        byte[] bytes = CharacterCreation.Build(session.EnsureGame()!, program.SaveOf(Program.Open), name, (byte)classId, level, hardcore, questsDone: false);

        // A dead hardcore character, for the revive: the flag the game sets.
        if (dead)
        {
            var killed = D2SLib.Core.ReadD2S(bytes);
            killed.Status.IsDead = true;
            bytes = SaveWriter.Prepared(killed);
        }

        File.WriteAllBytes(program.SaveOf(name), bytes);
        if (session.IsLoaded)
            session.UseMyLayout();
        return program.SaveOf(name);
    }

    /// <summary>The character opened in place of the one open, by the road the search's finds take (SwitchTo).</summary>
    public static void Open(Program program, string name)
    {
        program.On(() => program.Main.Home.SwitchTo(program.SaveOf(name)));
        Assert.True(program.Until(() => program.Main.Session.IsLoaded && program.Main.Session.CharacterName == name, 15000), $"{name} was not opened");
    }

    /// <summary>
    /// The characters made for a test gone again - their files, what lies beside them and their backups - with
    /// nothing open, and the home page's list read again: the next test's Ready opens the pad's character.
    /// </summary>
    public static void Gone(Program program, params string[] names)
    {
        program.On(() =>
        {
            program.Get<Services.Character.AutoSave>().Flush();
            if (program.Main.Confirm.IsAsking)
                program.Main.Confirm.RejectCommand.Execute(null);
            if (program.Main.Session.IsLoaded || program.Main.Session.HasAttempt)
                program.Main.Session.Close();

            string saves = Path.Combine(program.Folder, "saves");
            foreach (string name in names)
            {
                foreach (string file in Directory.GetFiles(saves, name + ".*"))
                    File.Delete(file);

                string backups = Path.Combine(SaveWriter.BackupRoot(saves), name);
                if (Directory.Exists(backups))
                    Directory.Delete(backups, recursive: true);
            }

            // Close, with nothing open: the list of characters read again, as closing reads it.
            program.Main.Home.CloseCommand.Execute(null);
        });
    }

    /// <summary>The play mode for one test ("Free", "Casual", "Legit"); disposed, the mode it found is back.</summary>
    public static IDisposable Mode(Program program, string mode) => new ModeKept(program, mode);

    private sealed class ModeKept : IDisposable
    {
        private readonly Program _program;
        private readonly string _before;

        public ModeKept(Program program, string mode)
        {
            _program = program;
            _before = program.On(() => program.Get<EditorSettings>().PlayMode);
            program.On(() => program.Get<EditorSettings>().PlayMode = mode);
        }

        public void Dispose() => _program.On(() => _program.Get<EditorSettings>().PlayMode = _before);
    }
}
