using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Window;

/// <summary>
/// The Edit page's Graphic box is offered wherever the picture is picked by the item's own number (it
/// was refused to every unique and set item): a unique ring, a set amulet and a unique jewel
/// have it, as a magic ring does; Annihilus, whose row names its picture, has none.
/// <para>
/// Asked of the Edit page as the view asks it, the item clicked first. That the box is drawn where it is
/// offered is the UI case's to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class GraphicBoxTests(Program program)
{
    private CharacterSession Session => program.Main.Session;
    private VP_ViewModel_Inv_L Page => program.Main.Inventory!;
    private VP_ViewModel_Inv_R Edit => program.Get<VP_ViewModel_Inv_R>();

    private ItemTileViewModel InGrid(string name, string container) =>
        Page.Sections.First(section => section.Pages is not null).Pages!.First(page => page.Label == container).Grid!.Items.First(tile => tile.Name == name);

    [CorpusTheory]
    [InlineData("Steel Ring of Skill", "Personal Stash", true)]
    [InlineData("Skull Grip", "Personal Stash", true)]
    [InlineData("Tancred's Weird", "Personal Stash", true)]
    [InlineData("Shadow Eye", "Personal Stash", true)]
    [InlineData("Annihilus", "Inventory", false)]
    public void The_Graphic_box_is_offered_where_the_items_own_number_picks_its_picture(string name, string container, bool offered)
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            try
            {
                // The item's tile clicked: selected, and the Edit page shows it.
                Page.ClickTileCommand.Execute(InGrid(name, container));

                Assert.Equal(name, Session.SelectedItem?.Name);
                Assert.Equal(offered, Edit.Basic.HasGraphic);
                if (offered)
                    Assert.Matches(@"^max [2-8]$", Edit.Basic.GraphicLimits);
            }
            finally
            {
                Page.ClearSelection();
            }
        });
    }
}
