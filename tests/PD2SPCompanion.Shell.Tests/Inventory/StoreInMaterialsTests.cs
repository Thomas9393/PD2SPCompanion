using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// Deposit materials (the game's name and key R): the item menu's row for anything the tab counts, on a right click
/// of any item the tab can store - and the tools' button, always available, which seeks
/// all qualified items from the inventory to the shared pages and dumps them in the tab.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class StoreInMaterialsTests(Program program)
{
    private CharacterSession Session => program.Main.Session;
    private VP_ViewModel_Inv_L Page => program.Main.Inventory!;

    /// <summary>Every tile on every page the panes can show.</summary>
    private IEnumerable<ItemTileViewModel> AllTiles() =>
        program.Section().Pages!.Where(page => page.Grid is not null).SelectMany(page => page.Grid!.Items);

    /// <summary>A Perfect Ruby, made as the tab hands one out, landed where a made item lands.</summary>
    private D2SLib.Model.Save.Item Gem(uint id)
    {
        Session.UseMyLayout();
        var gem = PD2SPCompanion.Core.Items.ItemForge.Material("gpr", 1, Session.ItemEra!);
        gem.Id = id;
        Assert.Null(program.Get<ItemLanding>().Receive(gem, "no room", out _));
        return gem;
    }

    [CorpusFact]
    public void The_menu_row_is_there_for_a_material_and_stores_it()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            var gem = Gem(0x7E57_6E01);
            var tile = AllTiles().First(each => ReferenceEquals(each.Item, gem));
            var (slot, quantity) = Page.Stash.Takes(gem)!.Value;
            int before = Page.Stash.MaterialCount(slot);

            var other = AllTiles().First(each => each.TabSlot is null && Page.Stash.Takes(each.Item) is null);
            Assert.DoesNotContain("Deposit materials", Page.MenuFor(other).Words());

            Page.ClickTileCommand.Execute(tile);
            Page.MenuFor(tile).Pick("Deposit materials");

            Assert.Equal(before + quantity, Page.Stash.MaterialCount(slot));
            Assert.DoesNotContain(AllTiles(), each => ReferenceEquals(each.Item, gem));
            Assert.Equal($"Moved {quantity} Perfect Ruby to materials tab.", Session.Status);
        });
    }

    [CorpusFact]
    public void The_tools_button_is_always_there_and_sweeps_every_material_into_the_tab()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            var first = Gem(0x7E57_6E02);
            var second = Gem(0x7E57_6E03);
            var (slot, _) = Page.Stash.Takes(first)!.Value;
            int before = Page.Stash.MaterialCount(slot);

            // Whatever is selected - here something the tab does not count - the button acts.
            Page.ClickTileCommand.Execute(AllTiles().First(each => each.TabSlot is null && Page.Stash.Takes(each.Item) is null));
            Assert.True(Page.CanStoreInMaterials);
            Assert.Equal(ShortcutText.With("Deposit materials", "R"), Page.StoreInMaterialsHint);

            Page.StoreInMaterialsSelectedCommand.Execute(null);

            Assert.Equal(before + 2, Page.Stash.MaterialCount(slot));
            Assert.DoesNotContain(AllTiles(), each => ReferenceEquals(each.Item, first) || ReferenceEquals(each.Item, second));
            Assert.StartsWith("Moved ", Session.Status);

            // Pressed again, the pages hold nothing more it would take than what a lock keeps.
            Page.StoreInMaterialsSelectedCommand.Execute(null);
            Assert.True(Session.Status is "No materials to deposit." || Session.Status.EndsWith("locked in the inventory.")
                        || Session.Status.EndsWith("on a locked page."), Session.Status);
        });
    }
}
