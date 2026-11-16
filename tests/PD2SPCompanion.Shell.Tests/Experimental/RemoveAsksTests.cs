using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Experimental;

/// <summary>
/// REMOVE on the Experimental page asks first: it deletes files in the game folder, and
/// no undo of the program's brings them back. Asked, and answered no: nothing in the game folder is touched.
/// The question is never answered yes here - these tests run on the installed game.
/// <para>
/// What the page decides, asked through its command and read back from the question the window would show.
/// That REMOVE cannot be pressed with nothing installed, and that Esc answers the question no, are the UI
/// case's (units/remove-asks.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class RemoveAsksTests(Program program)
{
    private VP_ViewModel_Experimental_L Page => program.Get<VP_ViewModel_Experimental_L>();
    private ConfirmService Confirm => program.Get<ConfirmService>();

    /// <summary>The marker of the page's files in the game folder: there, they are installed.</summary>
    private static string Marker => Path.Combine(Program.GameRoot, "ProjectD2", "data", "pd2spc-density.json");

    [CorpusFact]
    public void Remove_asks_before_anything_is_removed_and_no_leaves_the_game_folder_as_it_was()
    {
        if (!program.Ready())
            return;

        bool installed = File.Exists(Marker);
        DateTime? written = installed ? File.GetLastWriteTimeUtc(Marker) : null;
        try
        {
            program.On(() =>
            {
                Assert.False(Confirm.IsAsking, "a question was already up");
                Assert.Equal(installed, Page.IsInstalled);
                string status = Page.Status;

                Page.RemoveCommand.Execute(null);
                Assert.True(Confirm.IsAsking);
                Assert.Equal("Remove mod files", Confirm.Title);
                Assert.Equal("Mod files will be removed from the game folder.", Confirm.Message);
                Assert.Equal("REMOVE", Confirm.ConfirmLabel);

                // Nothing is removed while it asks.
                Assert.Equal(installed, File.Exists(Marker));

                // No - what Esc and CANCEL answer.
                Confirm.RejectCommand.Execute(null);
                Assert.False(Confirm.IsAsking);
                Assert.Equal(installed, File.Exists(Marker));
                Assert.Equal(written, installed ? File.GetLastWriteTimeUtc(Marker) : null);
                Assert.Equal(installed, Page.IsInstalled);
                Assert.Equal(status, Page.Status);
            });
        }
        finally
        {
            // Never left asking: the next test's first key would answer it.
            program.On(() =>
            {
                if (Confirm.IsAsking)
                    Confirm.RejectCommand.Execute(null);
            });
        }
    }
}
