using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Tests.Edit;
using PD2SPCompanion.Shell.Tests.Inventory;
using PD2SPCompanion.Shell.Tests.Mercenary;
using PD2SPCompanion.Shell.Tests.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Items;

/// <summary>
/// The clipboard of items and folders (ItemClipboard):
/// a cut marks and moves nothing until it is pasted - on the page the
/// first pane shows, into the vault's folder, out of the vault onto the character, a folder into another - and
/// is let go without a paste; a copy pasted is a second of the item, where the play mode makes seconds; the
/// clipboard is emptied of a character's items as the character closes; and an item's menu and a folder's have
/// the three at their head.
/// <para>
/// What the pages decide, asked of them as the keys and the buttons ask (Cut, Copy, Paste). That Ctrl + X, C and
/// V reach a page, that a cut item is drawn faint and that Esc lets it go are the UI cases' to show
/// (units/clipboard.ps1).
/// </para>
/// On the pad's PhySpearZon: an Annihilus in its inventory, which the inventory lock keeps; a Steel Ring of
/// Skill in its personal stash; a Tome of Town Portal at the vault's top, the folders Charms (holding Grand and
/// Small) and Jewels.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class ItemClipboardTests(Program program)
{
    private const string Charm = "Annihilus", Ring = "Steel Ring of Skill", Tome = "Tome of Town Portal";
    private const string Stash = "Personal Stash", Shared = "Shared Page 5";

    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Inv_L Page => Main.Inventory!;
    private static ItemClipboard Clip => ItemClipboard.Shared;

    private bool Ready() => program.Ready(changes: true);

    private ItemTileViewModel InGrid(string name, string container = "Inventory") => VaultHands.Tile(program, name, container);

    private bool IsIn(string name, string container) => VaultHands.TileOrNone(program, name, container) is not null;

    [CorpusFact]
    public void A_cut_moves_nothing_until_it_is_pasted_on_the_page_the_first_pane_shows()
    {
        if (!Ready())
            return;

        program.On(() =>
        {
            try
            {
                // What the inventory lock keeps is not cut, and says so.
                Page.InventoryLock = true;
                Page.ClickTileCommand.Execute(InGrid(Charm));
                Page.CutCommand.Execute(null);
                Assert.Equal("Annihilus is locked in the inventory.", Session.Status);
                Assert.True(Clip.IsEmpty);
                Page.InventoryLock = false;

                VaultHands.Section(program).SelectedPage = VaultHands.Choice(program, Stash);
                var ring = InGrid(Ring, Stash);
                Page.ClickTileCommand.Execute(ring);
                Assert.True(Page.CanCut);
                Assert.False(Page.CanPaste);

                Page.CutCommand.Execute(null);
                Assert.Equal("1 item cut. Paste to move it.", Session.Status);
                Assert.Equal(ClipKind.Cut, Clip.Kind);
                Assert.True(Clip.IsCut(ring));
                Assert.True(IsIn(Ring, Stash));
                Assert.Equal("Paste (Ctrl + V)" + Environment.NewLine + "1 item cut", Page.PasteHint);

                // Pasted where it lies: it stays, and so does the cut.
                Page.PasteCommand.Execute(null);
                Assert.Equal("Already on this page.", Session.Status);
                Assert.Equal(ClipKind.Cut, Clip.Kind);

                // Another page shown, and pasted: there, and the clipboard empty.
                VaultHands.Section(program).SelectedPage = VaultHands.Choice(program, Shared);
                Page.PasteCommand.Execute(null);
                Assert.True(IsIn(Ring, Shared));
                Assert.False(IsIn(Ring, Stash));
                Assert.True(Clip.IsEmpty);
                Assert.False(Page.CanPaste);

                Page.PasteCommand.Execute(null);
                Assert.Equal("Nothing to paste.", Session.Status);
            }
            finally
            {
                VaultHands.Section(program).SelectedPage = VaultHands.Choice(program, "Inventory");
            }
        });
    }

    [CorpusFact]
    public void A_cut_let_go_leaves_everything_where_it_was()
    {
        if (!Ready())
            return;

        program.On(() =>
        {
            var ring = InGrid(Ring, Stash);
            Page.ClickTileCommand.Execute(ring);
            Page.CutCommand.Execute(null);
            Assert.True(Clip.IsCut(ring));

            // What Esc does.
            Clip.Clear();
            Assert.False(Clip.IsCut(ring));
            Assert.True(IsIn(Ring, Stash));
            Assert.False(Page.CanPaste);
        });
    }

    [CorpusFact]
    public void A_cut_of_the_characters_pasted_on_a_vault_page_is_stored_in_the_folder_shown_and_back()
    {
        if (!Ready())
            return;

        var vault = program.On(() => VaultHands.OnRight(program));
        Assert.True(program.Until(() => VaultHands.CardOrNone(vault, Tome) is not null), "the vault's cards were not listed");

        int stored = 0;
        program.On(() =>
        {
            VaultHands.GoTo(vault, "Jewels");
            stored = VaultHands.Held(program);

            Page.ClickTileCommand.Execute(InGrid(Ring, Stash));
            Page.CutCommand.Execute(null);
            vault.PasteCommand.Execute(null);
            Assert.True(Clip.IsEmpty);
        });

        Assert.True(program.Until(() => VaultHands.Held(program) == stored + 1 && VaultHands.CardOrNone(vault, Ring) is not null),
                    "the ring was not stored in the folder pasted in");

        // And back: cut in the vault, pasted on the inventory page - though the vault page has gone elsewhere.
        program.On(() =>
        {
            vault.ClickTileCommand.Execute(VaultHands.Card(vault, Ring));
            Assert.True(vault.CanCut);
            vault.CutCommand.Execute(null);
            Assert.Equal("1 item cut. Paste to move it.", vault.Notice);
            Assert.True(Clip.IsCut(VaultHands.Card(vault, Ring)));

            VaultHands.GoTo(vault, "Charms");
            Page.PasteCommand.Execute(null);
            Assert.True(Clip.IsEmpty);

            // The line the cut left under the vault page went with the cut.
            Assert.NotEqual("1 item cut. Paste to move it.", vault.Notice);
        });

        Assert.True(program.Until(() => VaultHands.Held(program) == stored), "the ring was not withdrawn");
        program.On(() => Assert.StartsWith("1 item moved to character.", vault.Notice));
    }

    [CorpusFact]
    public void A_cut_card_is_moved_into_the_folder_pasted_in_and_a_cut_folder_into_another()
    {
        if (!Ready())
            return;

        var vault = program.On(() => VaultHands.OnLeft(program));
        Assert.True(program.Until(() => VaultHands.CardOrNone(vault, Tome) is not null), "the vault's cards were not listed");

        program.On(() =>
        {
            vault.ClickTileCommand.Execute(VaultHands.Card(vault, Tome));
            vault.CutCommand.Execute(null);

            // Pasted where it lies: said, and nothing moved.
            vault.PasteCommand.Execute(null);
            Assert.Equal("Already in this folder.", vault.Notice);

            vault.ClickTileCommand.Execute(VaultHands.Card(vault, Tome));
            vault.CutCommand.Execute(null);
            VaultHands.GoTo(vault, "Jewels");
            vault.PasteCommand.Execute(null);
            Assert.Equal("Moved to Jewels.", vault.Notice);
            Assert.NotNull(VaultHands.CardOrNone(vault, Tome));
            Assert.True(Clip.IsEmpty);

            // A folder, from its menu: cut, and pasted into another by that one's menu.
            long jewels = VaultHands.Place(vault, "Jewels").Id!.Value, charms = VaultHands.Place(vault, "Charms").Id!.Value;
            var menu = vault.MenuFor(jewels);
            Assert.Equal(["Cut", "Paste", "Rename", "Delete"], menu.Icons.Select(icon => icon.Label));
            Assert.False(menu.Row("Paste").IsEnabled);

            menu.Pick("Cut");
            Assert.True(VaultHands.Place(vault, "Jewels").IsCut);
            Assert.False(VaultHands.Place(vault, "Charms").IsCut);

            vault.MenuFor(charms).Pick("Paste");
            Assert.True(Clip.IsEmpty);
            Assert.Equal(charms, VaultHands.Place(vault, "Jewels").Parent);
            Assert.False(VaultHands.Place(vault, "Jewels").IsCut);
        });
    }

    [CorpusFact]
    public void A_copy_pasted_in_another_folder_is_a_second_there_and_none_where_the_play_mode_makes_no_copies()
    {
        if (!Ready())
            return;

        var vault = program.On(() => VaultHands.OnLeft(program));
        Assert.True(program.Until(() => VaultHands.CardOrNone(vault, Tome) is not null), "the vault's cards were not listed");

        Pad.In(program, "Free", () => program.On(() =>
        {
            int stored = VaultHands.Held(program);
            vault.ClickTileCommand.Execute(VaultHands.Card(vault, Tome));
            vault.Copy();
            Assert.Equal(ClipKind.Copy, Clip.Kind);

            VaultHands.GoTo(vault, "Jewels");
            vault.PasteCommand.Execute(null);
            Assert.Equal(stored + 1, VaultHands.Held(program));
            Assert.NotNull(VaultHands.CardOrNone(vault, Tome));

            // A copy stays held: pasted again, another.
            vault.PasteCommand.Execute(null);
            Assert.Equal(stored + 2, VaultHands.Held(program));
            Assert.Equal(ClipKind.Copy, Clip.Kind);
        }));

        program.On(Clip.Clear);
        Pad.In(program, "Legit", () => program.On(() =>
        {
            VaultHands.GoTo(vault, "Jewels");
            vault.ClickTileCommand.Execute(VaultHands.Card(vault, Tome));
            vault.Copy();
            Assert.True(Clip.IsEmpty);

            // The menu has no Copy to offer there; Cut and Paste are every mode's.
            Assert.Equal(["Cut", "Paste", "Delete"], vault.MenuFor(VaultHands.Card(vault, Tome)).Icons.Select(icon => icon.Label));
        }));
    }

    [CorpusFact]
    public void An_items_menu_has_the_clipboards_three_at_its_head()
    {
        if (!Ready())
            return;

        Pad.In(program, "Free", () => program.On(() =>
        {
            var menu = Page.Menu(InGrid(Ring, Stash));
            Assert.Equal(["Cut", "Copy", "Paste", "Delete"], menu.Icons.Select(icon => icon.Label));
            Assert.Equal("Ctrl + X", menu.Row("Cut").Keys);
            Assert.False(menu.Row("Paste").IsEnabled);

            menu.Pick("Cut");
            Assert.True(Clip.IsCut(InGrid(Ring, Stash)));
            Assert.True(Page.Menu(InGrid(Ring, Stash)).Row("Paste").IsEnabled);
        }));
    }

    [CorpusFact]
    public void Several_cut_are_pasted_together_though_their_page_is_out_of_sight_and_its_picks_let_go()
    {
        if (!Ready())
            return;

        program.On(() =>
        {
            var section = VaultHands.Section(program);
            try
            {
                section.SelectedPage = VaultHands.Choice(program, Stash);
                var first = VaultHands.Tile(program, Ring, Stash);
                var second = VaultHands.Tile(program, "Felicitous Ring of Regrowth", Stash);
                Page.ClickTileCommand.Execute(first);
                VaultHands.CtrlClick(Page, second);
                Assert.Equal(2, Page.PickedCount);

                Page.CutCommand.Execute(null);
                Assert.Equal("2 items cut. Paste to move them.", Session.Status);
                Assert.True(Clip.IsCut(first) && Clip.IsCut(second));

                // Another page shown: the picks go with their page, the cut does not.
                section.SelectedPage = VaultHands.Choice(program, Shared);
                Page.PasteCommand.Execute(null);

                Assert.True(IsIn(Ring, Shared) && IsIn("Felicitous Ring of Regrowth", Shared));
                Assert.False(IsIn(Ring, Stash) || IsIn("Felicitous Ring of Regrowth", Stash));
                Assert.True(Clip.IsEmpty);
            }
            finally
            {
                Page.ClearSelection();
                section.SelectedPage = VaultHands.Choice(program, "Inventory");
            }
        });
    }

    [CorpusFact]
    public void A_paste_on_a_cell_that_does_not_take_it_goes_to_the_pages_first_room_and_says_so()
    {
        if (!Ready())
            return;

        program.On(() =>
        {
            var section = VaultHands.Section(program);
            try
            {
                section.SelectedPage = VaultHands.Choice(program, Stash);
                var ring = VaultHands.Tile(program, Ring, Stash);
                var other = section.SelectedPage!.Grid!.Items.First(tile => !ReferenceEquals(tile, ring));
                var taken = section.SelectedPage.Grid.Cells.First(cell => cell.Column == other.CellX && cell.Row == other.CellY);

                Page.ClickTileCommand.Execute(ring);
                Page.CutCommand.Execute(null);
                Page.PasteOn(section.SelectedPage, taken);

                Assert.StartsWith("No room there. ", Session.Status);
                Assert.True(IsIn(Ring, Stash));
                Assert.True(Clip.IsEmpty);
                Assert.False(Session.IsHolding);
            }
            finally
            {
                section.SelectedPage = VaultHands.Choice(program, "Inventory");
            }
        });
    }

    [CorpusFact]
    public void A_copy_crosses_between_the_character_and_the_vault_each_keeping_its_own()
    {
        if (!Ready())
            return;

        var vault = program.On(() => VaultHands.OnRight(program));
        Assert.True(program.Until(() => VaultHands.CardOrNone(vault, Tome) is not null), "the vault's cards were not listed");

        Pad.In(program, "Free", () => program.On(() =>
        {
            var section = VaultHands.Section(program);
            try
            {
                // The character's, pasted in the vault: a second there, the ring where it was.
                section.SelectedPage = VaultHands.Choice(program, Stash);
                int stored = VaultHands.Held(program);
                Page.ClickTileCommand.Execute(InGrid(Ring, Stash));
                Page.Copy();
                vault.PasteCommand.Execute(null);
                Assert.Equal(stored + 1, VaultHands.Held(program));
                Assert.NotNull(VaultHands.CardOrNone(vault, Ring));
                Assert.True(IsIn(Ring, Stash));

                // The vault's, pasted on the character: a second there, the vault holding what it held.
                int held = program.CountIn(Stash);
                vault.ClickTileCommand.Execute(VaultHands.Card(vault, Tome));
                vault.Copy();
                Page.PasteCommand.Execute(null);
                Assert.Equal(held + 1, program.CountIn(Stash));
                Assert.Equal(stored + 1, VaultHands.Held(program));
            }
            finally
            {
                section.SelectedPage = VaultHands.Choice(program, "Inventory");
            }
        }));
    }

    [CorpusFact]
    public void Closing_the_character_empties_the_clipboard_of_its_items_and_leaves_the_vaults()
    {
        if (!Ready())
            return;

        var vault = program.On(() => VaultHands.OnRight(program));
        Assert.True(program.Until(() => VaultHands.CardOrNone(vault, Tome) is not null), "the vault's cards were not listed");

        program.On(() =>
        {
            Page.ClickTileCommand.Execute(InGrid(Ring, Stash));
            Page.CutCommand.Execute(null);
            Assert.False(Clip.IsEmpty);
            Main.Home.CloseCommand.Execute(null);
        });
        Assert.True(program.Until(() => !Session.IsLoaded), "the character was not closed");
        program.On(() => Assert.True(Clip.IsEmpty));
    }

    /// <summary>
    /// A copy pasted on a cell lands on that cell, and the next one in the free room nearest it, not
    /// near the origin - by the menu of the cell, or by Ctrl + V with the pointer over it: the same call.
    /// </summary>
    [CorpusFact]
    public void A_copy_pasted_on_a_cell_lands_on_that_cell_and_the_next_beside_it()
    {
        if (!Ready())
            return;

        Pad.In(program, "Free", () => program.On(() =>
        {
            var section = VaultHands.Section(program);
            try
            {
                section.SelectedPage = VaultHands.Choice(program, Stash);
                var grid = section.SelectedPage!.Grid!;

                // The last bare cell of the page: as far from the first room as the page goes.
                var far = grid.Cells.Last(Page.IsFreeRoom);
                Page.ClickTileCommand.Execute(InGrid(Ring, Stash));
                Page.Copy();
                Page.PasteOn(section.SelectedPage, far);

                var copies = grid.Items.Where(tile => tile.Name == Ring).ToList();
                Assert.Equal(2, copies.Count);
                Assert.Contains(copies, tile => tile.CellX == far.Column && tile.CellY == far.Row);

                // Pasted there again: the cell is taken now, and the third lies next to it - not at the page's head.
                Page.PasteOn(section.SelectedPage, far);
                var third = grid.Items.Where(tile => tile.Name == Ring).ToList();
                Assert.Equal(3, third.Count);
                Assert.Contains(third, tile => (tile.CellX, tile.CellY) != (far.Column, far.Row)
                                               && Math.Abs(tile.CellX - far.Column) <= 1 && Math.Abs(tile.CellY - far.Row) <= 1);
                Assert.Null(program.Get<ItemLanding>().Wish);
            }
            finally
            {
                section.SelectedPage = VaultHands.Choice(program, "Inventory");
            }
        }));
    }

    /// <summary>
    /// The menu's Delete asks in the menu and then deletes - the second confirmation was
    /// on the tool row, not on the menu: the act says it asks, and run - the answer yes - the item
    /// is gone and the tools' bin was never left armed.
    /// </summary>
    [CorpusFact]
    public void An_items_menu_Delete_asks_in_the_menu_and_run_it_deletes_without_arming_the_tools_bin()
    {
        if (!Ready())
            return;

        program.On(() =>
        {
            var section = VaultHands.Section(program);
            try
            {
                section.SelectedPage = VaultHands.Choice(program, Stash);
                int held = program.CountIn(Stash);
                var menu = Page.Menu(InGrid(Ring, Stash));
                var delete = menu.Icons.First(icon => icon.Label == "Delete");

                Assert.True(delete.Asks);
                Assert.Equal("Delete", delete.Shown);
                delete.IsAsking = true;
                Assert.Equal("Confirm", delete.Shown);
                Assert.False(Page.DeleteArmed);
                Assert.Equal(held, program.CountIn(Stash));

                delete.Run();
                Assert.Equal(held - 1, program.CountIn(Stash));
                Assert.False(Page.DeleteArmed);
                Assert.False(IsIn(Ring, Stash));

                // A folder's and a character card's Delete have questions of their own: not asked twice.
                Assert.False(new MenuAct("Delete", MenuGlyph.Bin, () => { }) { IsBin = true }.Asks);
            }
            finally
            {
                section.SelectedPage = VaultHands.Choice(program, "Inventory");
            }
        });
    }
}
