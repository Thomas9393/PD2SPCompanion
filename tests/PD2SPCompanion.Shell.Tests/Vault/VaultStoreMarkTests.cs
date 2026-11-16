using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.Tests.Inventory;
using PD2SPCompanion.Shell.ViewModels;

using StoreAs = PD2SPCompanion.Shell.ViewModels.VP_ViewModel_Inv_L.StoreAs;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// An item marked to go into the vault, sent there again. Only Store's own button, pressed
/// again, takes a mark back; a drop or a paste only ever sends, on the vault's top level as on a folder, and the newest
/// folder named is where it goes - the one the strip said. Asked as the page's drop and paste ask (StoreInto, the
/// clipboard's cut and PasteInto), and read back from the vault after the save.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultStoreMarkTests(Program program) : IDisposable
{
    private const string Ring = "Steel Ring of Skill", Stash = "Personal Stash";

    private readonly VaultBook _book = new(program);

    private VP_ViewModel_Inv_L Items => program.Main.Inventory!;

    public void Dispose()
    {
        if (program.Available)
            program.PanesAsFound();

        _book.PutAway();
    }

    private ItemTileViewModel RingTile => program.Tile(Ring, Stash);

    [CorpusFact]
    public void Stores_own_button_pressed_again_takes_the_mark_back()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes(Stash);
            var item = RingTile.Item;
            Items.Store(StoreAs.Open, [RingTile]);
            Assert.True(program.Get<VaultMoves>().IsLeaving(item));

            Items.Store(StoreAs.Open, [RingTile]);

            Assert.False(program.Get<VaultMoves>().IsLeaving(item));
            Assert.Equal($"{Ring} stays.", _book.Session.Status);
        });
    }

    [CorpusFact]
    public void A_drop_on_the_vaults_top_level_leaves_the_mark_on()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes(Stash);
            var item = RingTile.Item;
            Items.Store(StoreAs.Open, [RingTile]);

            Assert.True(Items.StoreInto(item, null));

            Assert.True(program.Get<VaultMoves>().IsLeaving(item));
            Assert.DoesNotContain("stays", _book.Session.Status);
        });
    }

    [CorpusFact]
    public void A_cut_marked_item_pasted_on_a_vault_page_leaves_the_mark_on()
    {
        if (!program.Ready(changes: true))
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            // The vault is on the left page here: the items are found all the same.
            var tile = Items.Sections.SelectMany(section => section.Pages ?? []).Where(page => page.Grid is not null)
                .SelectMany(page => page.Grid!.Items).First(each => each.Name == Ring);
            var item = tile.Item;
            Items.Store(StoreAs.Open, [tile]);

            ItemClipboard.Shared.HoldItems(ClipKind.Cut, [item]);
            _book.Left.PasteInto(null);

            Assert.True(program.Get<VaultMoves>().IsLeaving(item));
            Assert.DoesNotContain("stays", _book.Session.Status);
        });
    }

    [CorpusFact]
    public void Dropped_on_another_folder_a_marked_item_goes_to_the_newest()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes(Stash);
            var item = RingTile.Item;

            Assert.True(Items.StoreInto(item, program.Folder("Small")));
            Assert.True(Items.StoreInto(item, program.Folder("Grand")));
            Assert.Contains("Grand", _book.Session.Status);
            Assert.True(program.Saved());
        });

        _book.Until(() => _book.Store.Page(PD2SPCompanion.Core.Collection.VaultQuery.Everything, "Name", false, 0, 1000).Any(row => row.Name == Ring), "the ring was not stored");
        program.On(() => Assert.Equal(program.Folder("Grand"), _book.Held(Ring).FolderId));
    }
}
