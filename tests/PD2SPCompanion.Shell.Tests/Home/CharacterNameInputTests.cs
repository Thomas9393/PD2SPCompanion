using PD2SPCompanion.Core.Characters;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

using PD2SPCompanion.Shell.Helper;

namespace PD2SPCompanion.Shell.Tests.Home;

/// <summary>
/// A character's name box takes what the game's own takes as it is typed, no looser than the game's, which
/// allows no digit: letters and one - or _; a digit, a
/// space, a second dash are not there.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class CharacterNameInputTests(Program program)
{
    [CorpusFact]
    public void A_name_box_takes_letters_and_one_dash_and_nothing_else()
    {
        if (!program.Ready())
            return;

        Assert.Equal("Abc", CharacterRename.Typable("", "A1b c!"));
        Assert.Equal("a-bc", CharacterRename.Typable("", "a-b_c"));
        Assert.Equal("", CharacterRename.Typable("Ab-", "_"));
        Assert.Equal("_", CharacterRename.Typable("Ab", "_"));

        program.On(() =>
        {
            var box = new TextBox { MaxLength = 15 };
            CharacterNameInput.SetIsOn(box, true);
            var window = new Avalonia.Controls.Window { Width = 300, Height = 100, Content = box };
            window.Show();
            try
            {
                box.Focus();
                foreach (string typed in new[] { "B", "o", "1", " ", "-", "_", "-", "b", "9" })
                    window.KeyTextInput(typed);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal("Bo-b", box.Text);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
