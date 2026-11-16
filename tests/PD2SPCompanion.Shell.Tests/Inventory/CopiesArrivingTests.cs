using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.Tests.Edit;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// A copy of another file's item landing on the character is asked what anything arriving is asked:
/// a vault card, a shared stash item, or several of them, whose stamp does not mix with the
/// character's, are refused with the reason and nothing lands - where a withdrawal of the same card always was. A second
/// of the character's own item is a new item in its file, and is asked too. Asked as the view asks (Copy, PasteOn).
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class CopiesArrivingTests(Program program)
{
    private const string Ring = "Steel Ring of Skill", Other = "Felicitous Ring of Regrowth", Odd = "50";

    private CharacterSession Session => program.Main.Session;
    private VP_ViewModel_Inv_L Page => program.Main.Inventory!;
    private static ItemClipboard Clip => ItemClipboard.Shared;

    private void StampTheCharacter()
    {
        Session.HeldItems.First(item => item.Version is { Length: > 0 }).Version = Odd;
        Assert.Equal(Odd, Session.ItemEra);
    }

    /// <summary>Free's rule, and the clipboard and the panes as found after.</summary>
    private void Test(Action test)
    {
        using var free = program.InMode("Free");
        try
        {
            program.On(test);
        }
        finally
        {
            program.On(Clip.Clear);
            program.PanesAsFound();
        }
    }

    [CorpusFact]
    public void A_vault_card_that_does_not_mix_is_refused_on_the_character()
    {
        if (!program.Ready(changes: true))
            return;

        Test(() =>
        {
            program.Panes("Inventory");
            var card = program.Get<VaultService>().Store!.Page(PD2SPCompanion.Core.Collection.VaultQuery.Everything, "Name", false, 0, 1).First();
            StampTheCharacter();
            int held = Session.ItemCount;

            Clip.HoldCards(ClipKind.Copy, [card.Key]);
            Page.PasteOn(program.Choice("Inventory"));

            Assert.Contains("Require same version", Session.Status);
            Assert.Equal(held, Session.ItemCount);
        });
    }

    [CorpusFact]
    public void A_shared_item_pasted_on_a_character_cell_that_does_not_mix_is_refused()
    {
        if (!program.Ready(changes: true))
            return;

        Test(() =>
        {
            program.Panes("Personal Stash", "Shared Page 2");
            Page.SendAcross(program.Tile(Ring, "Personal Stash"));
            var tile = program.Tile(Ring, "Shared Page 2");
            Page.ClickTileCommand.Execute(tile);
            StampTheCharacter();
            int held = Session.ItemCount, there = program.Grid("Shared Page 2").Items.Count;

            Page.Copy();
            Page.PasteOn(program.Choice("Personal Stash"), program.FreeCell("Personal Stash"));

            Assert.Contains("Require same version", Session.Status);
            Assert.Equal(held, Session.ItemCount);
            Assert.Equal(there, program.Grid("Shared Page 2").Items.Count);
        });
    }

    [CorpusFact]
    public void Several_shared_items_pasted_on_a_character_cell_are_refused_all_of_them()
    {
        if (!program.Ready(changes: true))
            return;

        Test(() =>
        {
            program.Panes("Personal Stash", "Shared Page 2");
            Page.SendAcross(program.Tile(Ring, "Personal Stash"));
            Page.SendAcross(program.Tile(Other, "Personal Stash"));
            Page.ClearSelection();
            Assert.True(Page.BeginDragPick(keepSelected: false));
            Page.SetPicked(program.Tile(Ring, "Shared Page 2"), true);
            Page.SetPicked(program.Tile(Other, "Shared Page 2"), true);
            Page.EndPick();
            StampTheCharacter();
            int held = Session.ItemCount, there = program.Grid("Shared Page 2").Items.Count;

            Page.Copy();
            Assert.Equal(2, Clip.Items.Count);
            Page.PasteOn(program.Choice("Personal Stash"), program.FreeCell("Personal Stash"));

            Assert.Contains("Require same version", Session.Status);
            Assert.Equal(held, Session.ItemCount);
            Assert.Equal(there, program.Grid("Shared Page 2").Items.Count);
        });
    }

    [CorpusFact]
    public void A_second_of_the_characters_own_item_is_asked_as_well()
    {
        if (!program.Ready(changes: true))
            return;

        Test(() =>
        {
            program.Panes("Personal Stash");
            Page.ClickTileCommand.Execute(program.Tile(Ring, "Personal Stash"));
            StampTheCharacter();
            int held = Session.ItemCount;

            Page.Copy();
            Page.PasteOn(program.Choice("Personal Stash"));

            Assert.Contains("Require same version", Session.Status);
            Assert.Equal(held, Session.ItemCount);
        });
    }
}
