using Avalonia.Input;

using PD2SPCompanion.Core.Collection;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Window;

/// <summary>
/// Several items carried from the vault to the inventory keep their shape:
/// as they lie about the one held on the sheet, so they
/// lie where it is let go of. The other way there is no shape to keep:
/// two rings of the stash carried onto the vault's sheet are both stored
/// into its folder, where the rule lays them out - side by side, so that is the shape there is to carry back;
/// picked together there and carried back onto an empty shared page, they lie as the sheet had them. (That two
/// picked and let go of on the sheet are both stored is Inventory/DragBetweenPagesTests' too.)
/// <para>
/// What the two pages decide, asked of them as the views ask: the click and the Ctrl + click that pick, the
/// drag that leaves a tile (BeginCarry), and the release (DropFromInventory on the sheet, DropFromVault over a
/// cell). That a drag of the mouse reaches those, and which cell lies under the pointer, is the UI case's to
/// show (units/group-shape.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class GroupShapeTests(Program program)
{
    private const string One = "Steel Ring of Skill", Two = "Entropy Gyre";

    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Inv_L Page => Main.Inventory!;
    private VP_ViewModel_Vault_L Sheet => Main.RightVault!;

    private ContainerSectionViewModel Section => Page.Sections.First(section => section.Pages is not null);

    private StashPageChoice Choice(string label) => Section.Pages!.First(page => page.Label == label);

    private ItemTileViewModel? InGrid(string name, string container) =>
        Choice(container).Grid!.Items.FirstOrDefault(tile => tile.Name == name);

    /// <summary>The vault page's cards, its sheet read as the window reads it (a stretch reads its items when it is drawn).</summary>
    private IReadOnlyList<ItemTileViewModel> Cards() => Vault.VaultHands.CardsOf(Sheet);

    private ItemTileViewModel? Card(string name) => Cards().FirstOrDefault(card => card.Name == name);

    /// <summary>How far the second lies from the first on the sheet, in cells: across, and down through its stretches.</summary>
    private (int Dx, int Dy) Apart(ItemTileViewModel from, ItemTileViewModel to) =>
        (to.CellX - from.CellX, (Sheet.RowOf(to.Key) ?? 0) - (Sheet.RowOf(from.Key) ?? 0));

    /// <summary>
    /// The stash on the left page with the two rings in it, the vault on the right page in its empty folder,
    /// and the two rings carried there by the first, let go of on the sheet.
    /// </summary>
    private void CarriedToTheVault()
    {
        program.On(() =>
        {
            Main.ShowRightCommand.Execute(MainViewModel.VaultMark);
            Sheet.Show();
            Sheet.GoTo(program.Get<VaultService>().Store!.Folders().First(folder => folder.Name == "New folder").Id);
            Section.SelectedPage = Choice("Personal Stash");
        });
        Assert.True(program.Until(() => Sheet.PageRows.Count > 0 && Cards().Count == 0), "the vault's empty folder was not shown");

        program.On(() =>
        {
            var one = InGrid(One, "Personal Stash")!;
            var two = InGrid(Two, "Personal Stash")!;

            // A click on the first, Ctrl + click on the second: both picked.
            Page.ClickTileCommand.Execute(one);
            Assert.True(Page.BeginDragPick(keepSelected: true));
            Page.SetPicked(two, true);
            Page.EndPick();
            Assert.Equal(2, Page.PickedCount);

            // The drag leaves the first: both are carried, the second as it lies about the first.
            Assert.True(Page.BeginCarry(one));
            Assert.True(Session.IsHolding);
            Assert.Single(Session.HeldGroup);

            // Let go of on the sheet.
            Sheet.DropFromInventory(null);
        });

        Assert.True(program.Until(() => Card(One) is not null && Card(Two) is not null), "both rings are not in the vault's folder");
    }

    [CorpusFact]
    public void Two_rings_stored_together_are_laid_by_the_rule_and_carried_back_onto_an_empty_shared_page_lie_as_the_sheet_had_them()
    {
        if (!program.Ready(changes: true))
            return;

        CarriedToTheVault();

        program.On(() =>
        {
            // Laid out by the rule, in the folder that was empty: side by side from its first cell.
            var cells = new[] { Card(One)!, Card(Two)! }.Select(card => (card.CellX, card.CellY)).Order().ToList();
            Assert.Equal([(0, 0), (1, 0)], cells);

            // Moved, not copied: the stash has neither.
            Assert.Null(InGrid(One, "Personal Stash"));
            Assert.Null(InGrid(Two, "Personal Stash"));
            Assert.False(Session.IsHolding);
        });

        var lay = program.On(() =>
        {
            Section.SelectedPage = Choice("Shared Page 5");
            Assert.Empty(Choice("Shared Page 5").Grid!.Items);

            // A click on the first card's tile, Ctrl + click on the second's, and the drag leaves the first.
            var one = Card(One)!;
            var two = Card(Two)!;
            Sheet.ClickTileCommand.Execute(one);
            Assert.True(Sheet.BeginDragPick(keepSelected: true));
            Sheet.SetPicked(two, true);
            Sheet.EndPick();
            Assert.Equal(2, Sheet.PickedCount);
            var apart = Apart(one, two);
            Assert.True(Math.Abs(apart.Dx) + Math.Abs(apart.Dy) >= 1, $"the rings lie {apart} apart");
            Assert.True(Sheet.BeginCarry(one));
            Assert.True(Page.IsVaultCarrying);

            // Near the page's top left: the two lie a column apart, and the page is ten by fifteen.
            int column = Math.Max(1, 1 - apart.Dx), row = Math.Max(2, 2 - apart.Dy);
            Page.DropFromVault(Choice("Shared Page 5").Grid!.Cells.First(cell => cell.Column == column && cell.Row == row));
            return apart;
        });

        Assert.True(program.Until(() => InGrid(One, "Shared Page 5") is not null && InGrid(Two, "Shared Page 5") is not null),
            "both rings are not on the shared page");

        program.On(() =>
        {
            var one = InGrid(One, "Shared Page 5")!;
            var two = InGrid(Two, "Shared Page 5")!;
            Assert.Equal(lay, (two.Item.X - one.Item.X, two.Item.Y - one.Item.Y));
            Assert.Equal((Math.Max(1, 1 - lay.Dx), Math.Max(2, 2 - lay.Dy)), ((int)one.Item.X, (int)one.Item.Y));
            Assert.Null(Card(One));
            Assert.Null(Card(Two));
            Assert.False(Sheet.IsCarrying);
        });
    }
}
