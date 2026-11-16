using D2SLib.Model.Save;
using PD2SPCompanion.Core.Characters;
using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// What the character can use follows an edit of a worn item - a base upgraded, the item
/// past its strength requirement, went unnoticed: the tiles were judged again only when the attributes
/// or the belt changed, or the grids were rebuilt - an edit in place is none of those. Every edit asks it now
/// (CharacterSession.Edited), as the sheet's (SheetFollowsEditsTests).
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class JudgeFollowsEditsTests(Program program)
{
    private CharacterSession Session => program.Main.Session;

    [CorpusFact]
    public void A_worn_item_whose_requirement_rises_past_the_character_turns_unusable()
    {
        if (!program.Ready(changes: true))
            return;

        ItemTileViewModel? tile = null;
        ItemStat? raised = null;
        try
        {
            program.On(() => program.Panes("Inventory"));

            // A worn piece that asks for strength and is usable now.
            Assert.True(program.Until(() => Session.Equipped.Any(slot => slot.Item is { IsUnusable: false } worn
                                                                          && Requirements.Of(worn.Item, null).Strength > 0)),
                "no usable worn piece asks for strength");

            program.On(() =>
            {
                tile = Session.Equipped.Select(slot => slot.Item).OfType<ItemTileViewModel>()
                    .First(worn => !worn.IsUnusable && Requirements.Of(worn.Item, null).Strength > 0);

                // Its requirements raised twentyfold - past any character - and kept, as the Edit page keeps a change.
                raised = new ItemStat { Id = StatCatalogue.IdOf("item_req_percent"), Stat = "item_req_percent", Value = 2000 };
                tile.Item.StatLists[0].Stats.Add(raised);
                Session.MarkEdited();
            });

            Assert.True(program.Until(() => tile!.IsUnusable, 4000), "the worn piece stayed usable");
        }
        finally
        {
            program.On(() =>
            {
                if (tile is not null && raised is not null)
                {
                    tile.Item.StatLists[0].Stats.Remove(raised);
                    Session.MarkEdited();
                }
            });
        }

        Assert.True(program.Until(() => !tile!.IsUnusable, 4000), "the worn piece stayed unusable once put back");
    }
}
