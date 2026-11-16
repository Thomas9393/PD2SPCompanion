using System.ComponentModel;

using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Character;

/// <summary>
/// The Character page follows the session only while it is shown, or when it is next shown (WhenShown):
/// behind the Items page it does not rebuild its cards on every move. The character is
/// changed under it as the game saving would - another character's save copied over the open one, which the
/// program notices by itself and reads again - and on coming back the page shows that character, not the one
/// it last drew.
/// <para>
/// The page told of being shown and hidden as its view tells it. That the view does tell it, as the page
/// comes to the front, is the UI case's to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class PageFollowTests(Program program)
{
    private CharacterSession Session => program.Main.Session;
    private VP_ViewModel_Char_L Page => program.Get<VP_ViewModel_Char_L>();

    [CorpusFact]
    public void Read_again_behind_another_page_the_character_is_drawn_when_the_page_is_shown_again()
    {
        if (!program.Ready(changes: true))
            return;

        int drawn = 0;
        void Told(object? sender, PropertyChangedEventArgs changed)
        {
            if (changed.PropertyName == nameof(VP_ViewModel_Char_L.CardDescription))
                drawn++;
        }

        try
        {
            program.On(() =>
            {
                // The save folder watched, as when the program starts: the fixture makes the folder anew after a
                // test that changed it, and a watcher does not outlive its folder.
                Session.UseGameFolder(Program.GameRoot);

                Page.Shown();
                Assert.Equal("Level 86 Amazon", Page.CardDescription);
                Page.Hidden();
                Page.PropertyChanged += Told;
            });

            // The game saving another character's state into this one's file: seen by the folder's watcher.
            File.Copy(Path.Combine(Program.Corpus, "saves", "FigureTest.d2s"), program.SaveOf(Program.Open), overwrite: true);
            Assert.True(program.Until(() => Session.Status == "PhySpearZon reloaded. Saved in game.", 8000), "the file was not read again");

            program.On(() =>
            {
                Assert.Equal(0, drawn);

                Page.Shown();
                Assert.True(drawn > 0, "the page did not draw the character read");
                Assert.Equal("Level 40 Barbarian", Page.CardDescription);
            });
        }
        finally
        {
            program.On(() =>
            {
                Page.PropertyChanged -= Told;
                Page.Hidden();
            });
        }
    }
}
