using D2SLib.Model.Save;

using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;

namespace PD2SPCompanion.Shell.Tests.Mercenary;

/// <summary>
/// What the tests of the mercenary and of the keys ask of the pad beside the fixture's own: a play mode for the
/// length of one test, a character's file as it lies on disk, and that file changed under the program as the
/// game saving it changes it. But for <see cref="In"/>, which waits, everything here is asked on the program's
/// thread.
/// </summary>
public static class Pad
{
    /// <summary>
    /// The play mode made <paramref name="mode"/> as the Settings page makes it, for what <paramref name="test"/>
    /// does, and the mode the test found put back after it.
    /// </summary>
    public static void In(Program program, string mode, Action test)
    {
        var settings = program.On(() => program.Get<EditorSettings>());
        string found = program.On(() => settings.PlayMode);
        program.On(() => settings.ChoosePolicy(PlayPolicy.Preset(mode)!));
        try
        {
            test();
        }
        finally
        {
            program.On(() => settings.ChoosePolicy(PlayPolicy.Preset(found)!));
        }
    }

    /// <summary>
    /// A character's file read as the command line tool reads it, once what waits to be saved is written.
    /// </summary>
    public static D2S File(Program program, string name)
    {
        program.Get<AutoSave>().Flush();
        var save = SaveVerifier.Load(program.SaveOf(name)).Save;

        // Reading a file sets the parser to that file's layout: back to the open character's.
        program.Main.Session.UseMyLayout();
        return save;
    }

    /// <summary>Whether a character's file reads back byte for byte, as <c>pd2spc verify</c> asks.</summary>
    public static bool ReadsBack(Program program, string name)
    {
        program.Get<AutoSave>().Flush();
        return SaveVerifier.Verify(program.SaveOf(name)).RoundTripped;
    }

    /// <summary>
    /// The open character's file changed under the program, as the game saving it does, and read again: what
    /// <paramref name="change"/> does to the save is what the program then holds.
    /// </summary>
    public static void SavedInGame(Program program, Action<D2S> change)
    {
        program.On(() =>
        {
            var save = File(program, Program.Open);
            change(save);
            SaveWriter.WriteSafely(program.SaveOf(Program.Open), SaveWriter.Prepared(save));
            program.Main.Sync!.Check();
        });
    }
}
