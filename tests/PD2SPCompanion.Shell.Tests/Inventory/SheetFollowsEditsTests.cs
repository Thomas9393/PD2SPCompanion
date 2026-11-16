using D2SLib.Model.Save;
using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// Advanced Stats follows an edit of a worn item, which lagged behind
/// edits: it was asked again only when the attributes or the belt changed, so a value kept on
/// the Edit page reached it at the next of those. Every edit asks it now (CharacterSession.Edited), once for a burst.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class SheetFollowsEditsTests(Program program)
{
    private CharacterSession Session => program.Main.Session;
    private VP_ViewModel_Inv_L Page => program.Main.Inventory!;

    private string SheetText() =>
        string.Join("\n", Page.Sheet?.Lines.Select(line => string.Concat(line.Left.Concat(line.Right).Select(part => part.Text))) ?? []);

    [CorpusFact]
    public void A_worn_items_edit_reaches_the_sheet()
    {
        if (!program.Ready(changes: true))
            return;

        Item? worn = null;
        ItemStat? added = null;
        try
        {
            program.On(() =>
            {
                program.Panes("Inventory");
                Page.SheetIsOpen = true;
                Page.SheetShown();
            });
            Assert.True(program.Until(() => Page.Sheet is not null && SheetText().Length > 0), "the sheet never came");
            string before = program.On(SheetText);

            // A strength line put on a worn item and kept, as the Edit page keeps a value.
            program.On(() =>
            {
                worn = Session.Equipped.Select(slot => slot.Item?.Item).OfType<Item>().First();
                added = new ItemStat { Id = StatCatalogue.IdOf("strength"), Stat = "strength", Value = 77 };
                worn.StatLists[0].Stats.Add(added);
                Session.MarkEdited();
            });

            Assert.True(program.Until(() => SheetText() != before, 4000), "the sheet did not follow the edit");
        }
        finally
        {
            program.On(() =>
            {
                if (worn is not null && added is not null)
                {
                    worn.StatLists[0].Stats.Remove(added);
                    Session.MarkEdited();
                }
                Page.SheetIsOpen = false;
            });
        }
    }
}
