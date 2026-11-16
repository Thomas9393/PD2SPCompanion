using PD2SPCompanion.Core.Characters;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Tests.Home;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Character;

/// <summary>
/// The character drawn in its gear:
/// a card chosen in the list is given its figure, drawn from its file;
/// opened, the character's own card is given one, and the home page's card of it shows the same; and no layer
/// is missing or failing in the log on the way.
/// <para>
/// That a figure is made and reaches the cards - on "Figured", the pad's Amazon in all its gear under a name
/// no figure was yet drawn for, so every one is drawn within the test and the log's lines are the test's own.
/// Whether it is drawn on the landing page and on the Character page, and what it looks like, is the UI
/// case's and a person's to see.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class FigureTests(Program program)
{
    private CharacterSession Session => program.Main.Session;
    private VP_ViewModel_Default_L HomePage => program.Main.Home;

    private CharacterCard? Card => HomePage.Characters.FirstOrDefault(card => card.Name == "Figured");

    /// <summary>The log's lines about figures: a layer the game ships no art for, or one that failed, says so there.</summary>
    private List<string> Logged()
    {
        string log = Path.Combine(program.Folder, "editor.log");
        if (!File.Exists(log))
            return [];

        using var file = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(file);
        return [.. reader.ReadToEnd().Split('\n').Where(line => line.Contains("figure", StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>
    /// Refit equipped what the character could now use, and the sprite did not
    /// update. The figure wears what the character can use; points taken back strip it of what it can no
    /// longer hold, and Refit dresses it again - each time the figure is drawn anew, with no grid rebuilt.
    /// </summary>
    [CorpusFact]
    public void Points_moved_draw_the_figure_again_as_gear_goes_out_of_use_and_comes_back()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = PD2SPCompanion.Shell.Tests.Edit.EditPad.InMode(program, "Free");
        var page = program.Get<PD2SPCompanion.Shell.ViewModels.VP_ViewModel_Char_L>();
        var card = Session.OpenCard!;
        Assert.True(program.Until(() => card.Figure is not null), "the open character has no figure");

        var dressed = card.Figure;
        program.On(() =>
        {
            page.Shown();
            page.RespecCommand.Execute(null);
        });
        Assert.True(program.Until(() => !ReferenceEquals(card.Figure, dressed)), "the figure was not drawn again when the points were taken back");

        var stripped = card.Figure;
        program.On(() =>
        {
            page.Refit();
            page.Hidden();
        });
        Assert.True(program.Until(() => !ReferenceEquals(card.Figure, stripped)), "the figure was not drawn again after Refit");
    }

    [CorpusFact]
    public void A_card_chosen_in_the_list_and_the_character_once_opened_are_given_their_figures_with_nothing_in_the_log()
    {
        if (!program.Ready(changes: true))
            return;

        int logged = Logged().Count;
        try
        {
            program.On(() =>
            {
                CharacterRename.Clone(program.SaveOf(Program.Open), "Figured");
                Session.UseMyLayout();

                // Close: the list of characters is read again, and its cards' figures drawn.
                HomePage.CloseCommand.Execute(null);
            });
            Assert.True(program.Until(() => !Session.IsLoaded && Card is not null), "Figured is not in the list");

            // The landing page: the chosen card, drawn from its file.
            program.On(() => HomePage.SelectedIndex = HomePage.Characters.IndexOf(Card!));
            Assert.True(program.Until(() => Card is { Figure: not null, AwaitsFigure: false }, 15000), "the chosen card got no figure");
            program.On(() => Assert.False(Card!.ShowsClassPicture));

            // Opened: the Character page's card, drawn from the save in memory, and the home page's card shows the same.
            program.On(() => HomePage.LoadCommand.Execute(null));
            Assert.True(program.Until(() => Session.IsLoaded && Session.CharacterName == "Figured" && Session.OpenCard is { Figure: not null, AwaitsFigure: false }, 15000),
                        "the open character got no figure");
            program.On(() => Assert.Same(Session.OpenCard!.Figure, Card!.Figure));

            Assert.Empty(Logged().Skip(logged));
        }
        finally
        {
            Made.Gone(program, "Figured");
        }
    }
}
