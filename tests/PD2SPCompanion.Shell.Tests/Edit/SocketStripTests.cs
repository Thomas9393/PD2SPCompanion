using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Tests.Inventory;
using PD2SPCompanion.Shell.Tests.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Edit;

/// <summary>
/// The Edit page's Sockets section as a row of sockets, which had no drag and drop
/// or selection: one socket selected,
/// said in the line under the row, and what the tools then offer; a runeword less one rune no runeword, its other
/// runes left in the base; a socket dragged out of the row landing on the cell it is let go of on, and one called
/// off landing nowhere; a gem carried from a grid leaving the Edit page on the item it showed, and going into it
/// let go of on the section; a socket cut and pasted, copied and pasted; and a socket's menu.
/// <para>
/// What the section and the pages decide, asked as the view asks. That the mouse drags a socket out and a gem in,
/// and that a right click opens the menu, are the UI cases' to show (units/socket-strip.ps1).
/// </para>
/// On the pad's PhySpearZon: Stone Barb worn, holding two Shael Runes, a Perfect Skull and a jewel; Insight, a
/// runeword of four runes, in the personal stash.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class SocketStripTests(Program program)
{
    private CharacterSession Session => program.Main.Session;
    private VP_ViewModel_Inv_L Page => program.Main.Inventory!;
    private VP_ViewModel_Inv_R Edit => program.EditPage();
    private SocketsEditor Sockets => Edit.Sockets;
    private static ItemClipboard Clip => ItemClipboard.Shared;

    private List<string> Row => [.. Sockets.Rows.Select(socket => socket.Name)];

    [CorpusFact]
    public void A_socket_selected_is_said_under_the_row_and_the_tools_are_its()
    {
        if (!program.Ready())
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            program.Select("Stone Barb");
            Assert.Equal(["Shael Rune", "Shael Rune", "Perfect Skull", "Ruby Jewel of Envy"], Row);

            // None chosen: the first filled one is selected and said, as there is no unselected state - but
            // Del is not its: a gem is deleted by Del only out of a socket the user selected.
            Assert.Same(Sockets.Rows[0], Sockets.SelectedSocket);
            Assert.Equal("Shael Rune", Sockets.CaptionName);
            Assert.Null(SocketCarry.Shared.DeleteSelection);
            Assert.True(Sockets.CanUnsocketAll);

            Sockets.SelectCommand.Execute(Sockets.Rows[0]);
            Assert.True(Sockets.Rows[0].IsSelected);
            Assert.NotNull(SocketCarry.Shared.DeleteSelection);
            Assert.Equal("Shael Rune", Sockets.CaptionName);
            Assert.Equal("+20% Increased Attack Speed", Sockets.CaptionEffect);
            Assert.True(Sockets.CanCopySelected && Sockets.CanCutSelected && Sockets.CanUnsocketSelected);
            Assert.False(Sockets.CanEditSelected);
            Assert.Equal("Unsocket", Sockets.UnsocketHint);

            // Another: the one alone, and a jewel is opened.
            Sockets.SelectCommand.Execute(Sockets.Rows[3]);
            Assert.Single(Sockets.Rows, socket => socket.IsSelected);
            Assert.True(Sockets.CanEditSelected);
            Assert.Contains("Enhanced Damage", Sockets.CaptionEffect);
            Assert.Contains(Environment.NewLine, Sockets.CaptionEffect);

            // Another item: its first filled one, not chosen.
            program.Select("Insight", "Personal Stash");
            Assert.Same(Sockets.Rows.First(socket => socket.IsFilled), Sockets.SelectedSocket);
            Assert.Null(SocketCarry.Shared.DeleteSelection);
            program.Deselect();
        });
    }

    [CorpusFact]
    public void A_runeword_less_one_rune_is_its_base_with_the_other_runes_left_in_it()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            var insight = program.Select("Insight", "Personal Stash");
            Assert.True(insight.IsRuneword);
            Assert.Equal(4, insight.Filled());
            var rest = insight.SocketedItems.Skip(1).ToList();
            string was = program.TileOf(insight).Name;

            Sockets.UnsocketCommand.Execute(Sockets.Rows[0]);

            Assert.False(insight.IsRuneword);
            Assert.Equal(0u, insight.RunewordId);
            Assert.Single(insight.StatLists);
            Assert.Equal(rest, insight.SocketedItems);
            Assert.Equal(4, insight.TotalNumberOfSockets);
            Assert.NotEqual(was, program.TileOf(insight).Name);
            Assert.Equal("Sockets  (3 / 4)", Sockets.Header);
        });
    }

    [CorpusFact]
    public void A_socket_dragged_out_lands_on_the_cell_it_is_let_go_of_on_and_called_off_nowhere()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes("Inventory");
            var barb = program.Select("Stone Barb");
            var skull = Sockets.Rows[2];
            var gem = skull.Gem!;

            // Called off - Esc, the right button, let go of back on the section: nothing has left.
            Assert.True(Sockets.BeginCarry(skull));
            Assert.True(Sockets.IsCarrying && SocketCarry.Shared.IsCarrying);
            Assert.True(skull.IsSelected);
            Sockets.EndCarry(null, cancel: true);
            Assert.False(Sockets.IsCarrying || SocketCarry.Shared.IsCarrying);
            Assert.Equal(4, barb.Filled());

            // Let go of on a bare cell of the inventory: there, and the rest of the row closed up.
            var cell = VaultHands.FreeCell(program, "Inventory");
            Assert.True(Page.IsFreeRoom(cell));
            Assert.True(Sockets.BeginCarry(Sockets.Rows[2]));
            Sockets.EndCarry(cell, cancel: false);

            Assert.Equal(3, barb.Filled());
            Assert.DoesNotContain(gem, barb.SocketedItems);
            Assert.Equal(["Shael Rune", "Shael Rune", "Ruby Jewel of Envy", "Empty socket"], Row);
            var landed = program.TileOn("Inventory", cell);
            Assert.Equal("Perfect Skull", landed.Name);
            Assert.False(Page.IsFreeRoom(cell));
            Assert.Equal("Stone Barb", Session.SelectedItem?.Name);

            // Carried from the grid, the Edit page stays on the item with a socket bare - and let go of on its
            // Sockets section, it goes in.
            Assert.True(Page.BeginCarry(landed));
            Assert.Equal("Stone Barb", Session.SelectedItem?.Name);
            Assert.True(Page.WouldSocketEdited(Session.SelectedItem));
            Assert.True(Page.DropOnEdited(Session.SelectedItem));
            Assert.Equal(4, barb.Filled());
            Assert.Equal("Perfect Skull socketed into Stone Barb.", Session.Status);
            Assert.False(Session.IsHolding);
        });
    }

    [CorpusFact]
    public void A_socket_cut_is_turned_out_by_its_paste_and_a_copy_pasted_leaves_the_item_its_own()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            program.Panes("Inventory");
            var barb = program.Select("Stone Barb");
            var gem = Sockets.Rows[2].Gem!;

            Sockets.CutSocketedCommand.Execute(Sockets.Rows[2]);
            Assert.Equal(ClipKind.Cut, Clip.Kind);
            Assert.True(Sockets.Rows[2].IsCut);
            Assert.Equal(4, barb.Filled());
            Assert.Equal("Paste (Ctrl + V)" + Environment.NewLine + "1 item cut", Page.PasteHint);

            // Pasted by the menu of a bare cell: on that cell.
            var cell = VaultHands.FreeCell(program, "Inventory");
            Page.MenuFor(cell).Pick("Paste");
            Assert.Equal(3, barb.Filled());
            Assert.DoesNotContain(gem, barb.SocketedItems);
            Assert.Equal("Perfect Skull", program.TileOn("Inventory", cell).Name);
            Assert.True(Clip.IsEmpty);

            // A cut whose item is no longer the one on the Edit page is nothing to paste.
            program.Select("Stone Barb");
            Sockets.CutSocketedCommand.Execute(Sockets.Rows[0]);
            program.Select("Insight", "Personal Stash");
            Page.PasteCommand.Execute(null);
            Assert.Equal("Nothing to paste.", Session.Status);
            Assert.True(Clip.IsEmpty);
            Assert.Equal(3, barb.Filled());

            // A copy: a second of the gem on the character, the item keeping its own and staying the one shown.
            program.Select("Stone Barb");
            int held = program.CountIn("Inventory");
            Sockets.CopySocketedCommand.Execute(Sockets.Rows[0]);
            Assert.Equal(ClipKind.Copy, Clip.Kind);
            Page.PasteCommand.Execute(null);
            Assert.StartsWith("Copied ", Session.Status);
            Assert.NotEqual("Copied 1 item.", Session.Status);   // one copy is said by its name
            Assert.Equal(held + 1, program.CountIn("Inventory"));
            Assert.Equal(3, barb.Filled());
            Assert.Equal("Stone Barb", Session.SelectedItem?.Name);
        });
    }

    [CorpusFact]
    public void A_sockets_menu_has_Cut_and_Copy_at_its_head_and_Unsocket_first()
    {
        if (!program.Ready())
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            program.Select("Stone Barb");
            var rune = Sockets.MenuFor(Sockets.Rows[0]);
            Assert.Equal(["Cut", "Copy", "Paste", "Delete"], rune.Icons.Select(icon => icon.Label));
            Assert.Equal("Unsocket; Unsocket all", rune.Words());
            Assert.True(rune.Rows[0].IsMain);

            Assert.Equal("Unsocket; Edit jewel; Unsocket all", Sockets.MenuFor(Sockets.Rows[3]).Words());
            program.Deselect();
        });
    }

    /// <summary>
    /// A socketed item deleted outright:
    /// the bin asks, then it is gone - out of the item and in no grid - the rest closed up. Del with a socket selected
    /// is the same bin, never the item the socket is in.
    /// </summary>
    [CorpusFact]
    public void A_socketed_item_is_deleted_by_the_bin_and_by_Del_never_its_host()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            program.Panes("Inventory");
            var barb = program.Select("Stone Barb");
            int held = program.CountIn("Inventory");
            Pick(2);
            var skull = Sockets.Rows[2].Gem!;
            Assert.True(Sockets.CanDeleteSelected);

            // The first press asks; the second deletes.
            Sockets.DeleteSelectedCommand.Execute(null);
            Assert.True(Sockets.DeleteArmed);
            Assert.Contains(skull, barb.SocketedItems);
            Sockets.DeleteSelectedCommand.Execute(null);
        });
        program.Settled();
        program.On(() =>
        {
            var barb = Session.SelectedItem!.Item;
            Assert.Equal(["Shael Rune", "Shael Rune", "Ruby Jewel of Envy", "Empty socket"], Row);
            Assert.Equal(3, barb.Filled());
            Assert.Equal("Stone Barb", Session.SelectedItem?.Name);

            // Del with a socket selected: the socket's bin (armed), not the Items page's on Stone Barb.
            Pick(0);
            Assert.True(PD2SPCompanion.Shell.Services.Generic.SocketCarry.Shared.DeleteSelection?.Invoke());
            Assert.True(Sockets.DeleteArmed);
            Assert.False(Page.DeleteArmed);
            program.Deselect();
        });
    }

    [CorpusFact]
    public void A_socket_cut_and_pasted_in_a_vault_folder_is_turned_out_and_stored_there()
    {
        if (!program.Ready(changes: true))
            return;


        var vault = program.On(() => VaultHands.OnRight(program));
        Assert.True(program.Until(() => VaultHands.CardOrNone(vault, "Tome of Town Portal") is not null), "the vault's cards were not listed");

        int stored = 0;
        Item barb = null!;
        program.On(() =>
        {
            barb = program.Select("Stone Barb");
            stored = VaultHands.Held(program);
            Sockets.CutSocketedCommand.Execute(Sockets.Rows[2]);
            vault.PasteCommand.Execute(null);
            Assert.True(Clip.IsEmpty);
            Assert.Equal(3, barb.Filled());
        });

        Assert.True(program.Until(() => VaultHands.Held(program) == stored + 1 && VaultHands.CardOrNone(vault, "Perfect Skull") is not null),
                    "the skull was not stored in the folder pasted in");
    }

    /// <summary>
    /// The vault's Edit page, where nothing is unsocketed: the row of sockets is looked at, one
    /// selected and said, and nothing takes what is in it out - no tool, no menu row, no drag.
    /// </summary>
    [CorpusFact]
    public void On_the_vaults_Edit_page_a_socket_is_selected_and_said_and_nothing_takes_it_out()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.OnTheVaultsEditPage("crs", 4, "Crystal Sword");
        var page = program.VaultEditPage();

        program.On(() =>
        {
            page.Runeword.OpenPickerCommand.Execute(null);
            page.Runeword.PickCommand.Execute(page.Runeword.Choices.First(row => row.Name == "Spirit"));
            page.Runeword.ClosePickerCommand.Execute(null);
        });
        Assert.True(program.Until(() => program.Stored().Item.IsRuneword), "the vault's row is no runeword");
        program.Settled(page);

        program.On(() =>
        {
            var sockets = page.Sockets;
            Assert.Equal(4, sockets.Rows.Count(socket => socket.IsFilled));

            var first = sockets.Rows[0];
            sockets.SelectCommand.Execute(first);
            Assert.Same(first, sockets.SelectedSocket);
            Assert.Equal(first.Name, sockets.CaptionName);

            // Nothing for it to come out by.
            Assert.False(sockets.CanEmpty);
            Assert.False(sockets.CopiesSocketed);
            Assert.False(sockets.CanUnsocketSelected || sockets.CanCutSelected || sockets.CanCopySelected || sockets.CanUnsocketAll);
            Assert.True(sockets.MenuFor(first).IsEmpty);
            Assert.False(sockets.BeginCarry(first));
            Assert.False(sockets.IsCarrying);

            // And a cut asked for all the same holds nothing.
            sockets.CutSocketedCommand.Execute(first);
            Assert.True(Clip.IsEmpty);
            Assert.Equal(4, program.Stored().Item.Filled());
        });
    }

    /// <summary>The selection's own hand, as DragPick asks it: Ctrl on a socket, the rectangle.</summary>
    private void Pick(params int[] places)
    {
        var picker = (IDragPicker)Sockets;
        Assert.True(picker.BeginDragPick(keepSelected: false));
        picker.ClearPicks();
        foreach (int at in places)
            picker.SetPicked(Sockets.Rows[at], true);
        picker.EndPick();
    }

    [CorpusFact]
    public void The_one_selected_clicked_again_stays_and_a_socket_has_a_tooltip_while_not_said()
    {
        if (!program.Ready())
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            program.Select("Stone Barb");
            var rune = Sockets.Rows[0];

            // The one selected: the caption is its own, so it has no tooltip - and every other socket has its still.
            Assert.Equal("Shael Rune" + Environment.NewLine + "+20% Increased Attack Speed", rune.Tip);
            Sockets.SelectCommand.Execute(rune);
            Assert.Same(rune, Sockets.SelectedSocket);
            Assert.True(rune.IsCaptioned);
            Assert.Equal(Sockets.Rows.Count - 1, Sockets.Rows.Count(each => !each.IsCaptioned));

            // No tooltip on it - none, so one open when it was clicked closes - and the others' as they are.
            Assert.Null(rune.ShownTip);
            Assert.All(Sockets.Rows.Where(each => !each.IsCaptioned), each => Assert.Equal(each.Tip, each.ShownTip));

            // Several selected: the caption names none of them, and each says itself.
            if (Sockets.Rows.Count > 1)
            {
                Pick(0, 1);
                Assert.DoesNotContain(Sockets.Rows, each => each.IsCaptioned);
                Sockets.SelectOnly(rune);
                Assert.True(rune.IsCaptioned);
            }

            // Clicked again: it stays, and its caption with it.
            Sockets.SelectCommand.Execute(rune);
            Assert.Same(rune, Sockets.SelectedSocket);
            Assert.Equal("Shael Rune", Sockets.CaptionName);

            // The right button's press, and a double click, select and never let go.
            Sockets.SelectOnly(rune);
            Sockets.SelectOnly(rune);
            Assert.Same(rune, Sockets.SelectedSocket);

            // A click on the section's empty room lets go of what was chosen: the first filled one again, Del not its.
            Sockets.SelectOnly(Sockets.Rows[3]);
            ((IDragPicker)Sockets).ClickedEmpty();
            Assert.Same(rune, Sockets.SelectedSocket);
            Assert.Null(SocketCarry.Shared.DeleteSelection);
        });
    }

    [CorpusFact]
    public void Several_sockets_selected_are_said_as_a_count_and_cut_carried_and_unsocketed_together()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            program.Panes("Inventory");
            var barb = program.Select("Stone Barb");
            Pick(0, 2);

            Assert.Equal(2, Sockets.SelectedSockets.Count);
            Assert.Null(Sockets.SelectedSocket);
            Assert.Equal("2 sockets selected", Sockets.CaptionName);
            Assert.Equal("", Sockets.CaptionEffect);
            Assert.False(Sockets.CanEditSelected);
            Assert.True(Sockets.CanCutSelected && Sockets.CanCopySelected && Sockets.CanUnsocketSelected);

            // Their menu is the selection's: no Edit jewel, the count on Unsocket.
            var menu = Sockets.MenuFor(Sockets.Rows[0]);
            Assert.Equal(["Cut", "Copy", "Paste", "Delete"], menu.Icons.Select(icon => icon.Label));
            Assert.Equal("Unsocket (2); Unsocket all", menu.Words());

            // Cut: both held, both faint, nothing out.
            Sockets.CutSelectedCommand.Execute(null);
            Assert.Equal(2, Clip.SocketedGems.Count);
            Assert.True(Sockets.Rows[0].IsCut && Sockets.Rows[2].IsCut && !Sockets.Rows[1].IsCut);
            Assert.Equal(4, barb.Filled());
            Clip.Clear();

            // One of them dragged carries both; called off, nothing has left.
            Assert.True(Sockets.BeginCarry(Sockets.Rows[2]));
            Assert.Equal(2, SocketCarry.Shared.Parts.Count);
            // Fanned, as the inventory and the vault draw a group: the second down and right of the first.
            Assert.Equal((0d, 0d), (SocketCarry.Shared.Parts[0].Left, SocketCarry.Shared.Parts[0].Top));
            Assert.Equal((GhostPart.Fan, GhostPart.Fan), (SocketCarry.Shared.Parts[1].Left, SocketCarry.Shared.Parts[1].Top));
            Sockets.EndCarry(null, cancel: true);
            Assert.Equal(4, barb.Filled());
            Assert.Equal(2, Sockets.SelectedSockets.Count);

            // Unsocket: both out in one change, the other two closed up, the first filled one selected as with none chosen.
            int held = program.CountIn("Inventory");
            Sockets.UnsocketSelectedCommand.Execute(null);
            Assert.Equal(2, barb.Filled());
            Assert.Equal(["Shael Rune", "Ruby Jewel of Envy", "Empty socket", "Empty socket"], Row);
            Assert.Equal(held + 2, program.CountIn("Inventory"));
            Assert.Same(Sockets.Rows[0], Sockets.SelectedSocket);
        });
    }

    /// <summary>
    /// Several dragged out of the row onto a bare cell, which had no landing footprint and
    /// sometimes landed in a weird spread: lit before the drop where they would land, and landed
    /// there - the one dragged on the cell, the other in the free room nearest it, as a paste lands. The other went
    /// beside the item it came out of.
    /// </summary>
    [CorpusFact]
    public void Several_dragged_onto_a_cell_light_where_they_land_and_land_there_together()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            program.Panes("Inventory");
            var barb = program.Select("Stone Barb");
            Pick(0, 2);
            var (first, second) = (Sockets.Rows[2].Name, Sockets.Rows[0].Name);
            var grid = VaultHands.Choice(program, "Inventory").Grid!;
            var there = grid.Items.Select(tile => tile.Item).ToHashSet();
            // Two free side by side: one on the cell, the other about it.
            var at = VaultHands.FreeCell(program, "Inventory", wide: 2);

            Assert.True(Sockets.BeginCarry(Sockets.Rows[2]));
            Page.HoverOver(at);
            var lit = grid.Cells.Where(each => each.IsTarget).ToList();
            Assert.Equal(2, lit.Count);
            Assert.All(lit, each => Assert.True(each.IsAllowed));
            Assert.Contains(at, lit);

            Page.LandFromRow(at, () => Sockets.EndCarry(at, cancel: false));
            Page.HoverOver(null);

            Assert.Equal(2, barb.Filled());
            // New items, each out of its socket loose (EditPad.TileOn).
            var arrived = grid.Items.Where(tile => !there.Contains(tile.Item)).ToList();
            var one = arrived.Single(tile => tile.Name == first);
            var two = arrived.Single(tile => tile.Name == second);
            Assert.Equal((at.Column, at.Row), (one.CellX, one.CellY));
            Assert.Equal(1, Math.Max(Math.Abs(two.CellX - at.Column), Math.Abs(two.CellY - at.Row)));
            Assert.Equal(
                lit.Select(each => (each.Column, each.Row)).OrderBy(each => each).ToList(),
                new[] { (one.CellX, one.CellY), (two.CellX, two.CellY) }.OrderBy(each => each).ToList());
        });
    }

    [CorpusFact]
    public void Several_cut_together_are_turned_out_together_by_the_paste()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            program.Panes("Inventory");
            var barb = program.Select("Stone Barb");
            int held = program.CountIn("Inventory");
            Pick(0, 1);
            Sockets.CutSelectedCommand.Execute(null);
            Assert.Equal("2 items cut", Clip.Says);

            Page.PasteCommand.Execute(null);

            Assert.True(Clip.IsEmpty);
            Assert.Equal(2, barb.Filled());
            Assert.Equal(held + 2, program.CountIn("Inventory"));
        });
    }

    /// <summary>
    /// A gem, a rune or a jewel on the clipboard is pasted into the item's sockets:
    /// the tools' Paste and a socket's menu's, live while what is
    /// held goes into a socket and the item has one.
    /// </summary>
    [CorpusFact]
    public void A_rune_cut_in_a_grid_is_pasted_into_the_items_next_socket_and_leaves_the_grid()
    {
        if (!program.Ready(changes: true))
            return;

        using var legit = program.InMode("Legit");
        program.On(() =>
        {
            program.Panes("Personal Stash", "Inventory");
            var cap = program.GivePlain("cap", 2);

            // Nothing held: Paste is dimmed.
            program.Select("Cap", "Personal Stash");
            Assert.False(Sockets.CanPasteHere);

            Page.ClickTileCommand.Execute(program.Tile("Hel Rune", "Inventory"));
            Page.CutCommand.Execute(null);
            program.Select("Cap", "Personal Stash");
            Assert.True(Sockets.CanPasteHere);

            // An empty socket's menu: Paste, live, saying what is held.
            var menu = Sockets.MenuFor(Sockets.Rows[0]);
            Assert.Equal(["Paste"], menu.Icons.Select(icon => icon.Label));
            Assert.True(menu.Icons[0].IsEnabled);

            Sockets.PasteHereCommand.Execute(null);

            Assert.Equal("Hel Rune socketed into Cap.", Session.Status);
            Assert.Equal(1, cap.Filled());
            Assert.False(program.Holds("Hel Rune", "Inventory"));
            Assert.True(Clip.IsEmpty);
            Assert.False(Sockets.CanPasteHere);
        });
    }

    [CorpusFact]
    public void A_rune_copied_is_pasted_as_a_second_of_itself_into_each_socket_and_stays_where_it_lies()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            program.Panes("Personal Stash", "Inventory");
            var cap = program.GivePlain("cap", 2);
            Page.ClickTileCommand.Execute(program.Tile("Hel Rune", "Inventory"));
            Page.Copy();
            program.Select("Cap", "Personal Stash");
            Assert.True(Sockets.CanPasteHere);

            Sockets.PasteHereCommand.Execute(null);
            Assert.Equal(1, cap.Filled());
            Assert.True(program.Holds("Hel Rune", "Inventory"));

            // Held still: pasted again, into the next socket - and then the item has none left.
            program.Select("Cap", "Personal Stash");
            Assert.True(Sockets.CanPasteHere);
            Sockets.PasteHereCommand.Execute(null);
            Assert.Equal(2, cap.Filled());
            Assert.NotSame(cap.SocketedItems[0], cap.SocketedItems[1]);

            program.Select(program.TileOf(cap).Name, "Personal Stash");
            Assert.False(Sockets.CanPasteHere);
            Assert.False(Clip.IsEmpty);
        });
    }

    [CorpusFact]
    public void What_is_no_socketable_is_not_pasted_into_a_socket_nor_a_copy_where_none_are_made()
    {
        if (!program.Ready(changes: true))
            return;

        // A ring cut: nothing for a socket.
        using (program.InMode("Free"))
        {
            program.On(() =>
            {
                program.Panes("Personal Stash", "Inventory");
                var cap = program.GivePlain("cap", 2);
                Page.ClickTileCommand.Execute(program.Tile("Steel Ring of Skill", "Personal Stash"));
                Page.CutCommand.Execute(null);
                program.Select("Cap", "Personal Stash");
                Assert.False(Sockets.CanPasteHere);
                Assert.False(Sockets.MenuFor(Sockets.Rows[0]).Icons[0].IsEnabled);

                // Asked for all the same: nothing goes in, and the ring is cut still.
                Sockets.PasteHereCommand.Execute(null);
                Assert.Equal(0, cap.Filled());
                Assert.True(program.Holds("Steel Ring of Skill", "Personal Stash"));
                Clip.Clear();
            });
        }

        // A rune copied, in a play mode that makes no copies: held (the game's own text is still copied), not pasted.
        using (program.InMode("Legit"))
        {
            program.On(() =>
            {
                Clip.HoldItems(ClipKind.Copy, [program.Tile("Hel Rune", "Inventory").Item]);
                program.Select("Cap", "Personal Stash");
                Assert.False(Sockets.CanPasteHere);
                Clip.Clear();
            });
        }
    }

    /// <summary>
    /// Carried out of the row onto another item with an empty socket, they go into it: as many as
    /// it has room for, in the row's order, the rest staying in their sockets; the Edit page stays on the item they came
    /// from. Not into the item they came from, and not with the socket guard on.
    /// </summary>
    [CorpusFact]
    public void Sockets_carried_onto_another_item_go_in_as_many_as_it_has_room_for_and_the_rest_stay()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            program.Panes("Inventory", "Personal Stash");

            // Witchwild String's three sockets, one emptied: room for one.
            var wild = program.Select("Witchwild String", "Personal Stash");
            Sockets.UnsocketCommand.Execute(Sockets.Rows[0]);
            Assert.Equal(2, wild.Filled());

            var barb = program.Select("Stone Barb");
            var first = Sockets.Rows[0].Gem!;
            var host = program.Tile("Witchwild String", "Personal Stash");

            // The item they come from is no target; the guard stands in the way.
            Assert.False(Page.TakesFromRow(program.Tile("Stone Barb"), program.Tile("Stone Barb")));
            bool guard = Page.SocketGuard;
            Page.SocketGuard = true;
            Assert.False(Page.TakesFromRow(host, program.Tile("Stone Barb")));
            Page.SocketGuard = false;
            try
            {
                Assert.True(Page.TakesFromRow(host, program.Tile("Stone Barb")));

                // Three carried - the third one dragged - onto room for one: the leftmost goes in, two stay.
                Pick(0, 1, 2);
                Assert.True(Sockets.BeginCarry(Sockets.Rows[2]));
                Assert.True(Sockets.EndCarryInto(gems => Page.SocketFromRow(host, gems, Sockets.EjectAll)));

                Assert.Equal(3, wild.Filled());
                // The rune as it lay loose a moment (EditPad.TileOn), socketed again: of its code, not the same item.
                Assert.Equal(first.Code, wild.SocketedItems[^1].Code);
                Assert.Equal(3, barb.Filled());
                Assert.DoesNotContain(first, barb.SocketedItems);
                Assert.StartsWith("1 of 3 socketed into Witchwild String. 2 stayed.", Session.Status);
                Assert.Same(barb, Session.SelectedItem?.Item);
                Assert.False(SocketCarry.Shared.IsCarrying);
            }
            finally
            {
                Page.SocketGuard = guard;
                program.Deselect();
            }
        });
    }
}
