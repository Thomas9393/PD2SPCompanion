using Avalonia.Input;

using D2SLib.Model.Save;

using PD2SPCompanion.Shell.CustomControls.Base;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Tests.Mercenary;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Keys;

/// <summary>
/// What the keys' commands do: Esc answers a
/// question no and Enter yes; Esc with nothing open lets the selection go; Ctrl+C and Ctrl+V make a second of what is selected
/// and Ctrl+Z takes the copy back; a number box steps by Up, Down, PageUp and PageDown within its range; a
/// number changed on the Edit page is written with no key pressed; and in the vault F2 names the folder shown,
/// Enter keeping the name, and Backspace is back.
/// <para>
/// Each is asked as the window's key handlers ask it - the command the key presses, the box handed the key.
/// That the key reaches the command - a question owning the keyboard, Esc closing the picker on top, the focus
/// a picker takes as it opens, Tab's ring, the pages and bookmarks by Ctrl, the arrows, Ctrl+F - is where the
/// window is, and the UI cases' to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class KeyEffectsTests(Program program)
{
    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Inv_L Page => Main.Inventory!;

    private EquipSlotViewModel Worn(ItemLocation at) => Session.Equipped.First(slot => slot.Slot.Location == at && !slot.IsSwap);

    private int Named(string name) =>
        Session.Equipped.Count(slot => slot.Item?.Name == name)
        + Page.Sections.First(section => section.Pages is not null).Pages!.Where(page => page.Grid is not null).Sum(page => page.Grid!.Items.Count(tile => tile.Name == name));

    [CorpusFact]
    public void A_question_answered_no_is_gone_with_nothing_done_and_answered_yes_does_it()
    {
        if (!program.Ready())
            return;

        try
        {
            program.On(() =>
            {
                var question = Main.Confirm;
                int deleted = 0, kept = 0;
                void Ask() => question.Ask("Delete character", "PhySpearZon is deleted.", "DELETE", () => deleted++, onCancel: () => kept++, deletes: true);

                // The bin is the asker's word, not the label's: a label in another language keeps it, and an
                // English DELETE that is not said to delete has the tick.
                question.Ask("x", "y", "LOESCHEN", () => { }, deletes: true);
                Assert.True(question.IsDelete);
                question.RejectCommand.Execute(null);
                question.Ask("x", "y", "DELETE", () => { });
                Assert.False(question.IsDelete);
                question.RejectCommand.Execute(null);

                // Esc: CANCEL.
                Ask();
                Assert.True(question.IsAsking);
                Assert.True(question.IsDelete);
                Assert.False(question.NeedsClick);
                question.RejectCommand.Execute(null);
                Assert.False(question.IsAsking);
                Assert.Equal((0, 1), (deleted, kept));

                // Enter: the yes.
                Ask();
                question.AcceptCommand.Execute(null);
                Assert.False(question.IsAsking);
                Assert.Equal((1, 1), (deleted, kept));

                // One up is left alone by another asked over it.
                Ask();
                question.Ask("Another", "", "CONTINUE", () => deleted += 10);
                Assert.Equal("Delete character", question.Title);
                question.RejectCommand.Execute(null);
                Assert.Equal((1, 2), (deleted, kept));
            });
        }
        finally
        {
            program.On(() =>
            {
                if (Main.Confirm.IsAsking)
                    Main.Confirm.RejectCommand.Execute(null);
            });
        }
    }

    [CorpusFact]
    public void Esc_with_nothing_open_lets_the_selection_go_one_item_or_several()
    {
        if (!program.Ready())
            return;

        try
        {
            program.On(() =>
            {
                var edit = program.Get<VP_ViewModel_Inv_R>();
                Page.ClickTileCommand.Execute(Worn(ItemLocation.Neck).Item);
                Assert.Equal("Beast Necklace", Session.SelectedItem?.Name);
                Assert.True(edit.HasItem);

                Page.ClearSelection();
                Assert.Null(Session.SelectedItem);
                Assert.False(Page.HasItemSelection);
                Assert.False(edit.HasItem);

                Page.ClickTileCommand.Execute(Worn(ItemLocation.Neck).Item);
                Assert.True(Page.BeginDragPick(keepSelected: true));
                Page.SetPicked(Worn(ItemLocation.LeftFinger).Item!, true);
                Page.EndPick();
                Assert.Equal(2, Page.PickedCount);

                Page.ClearSelection();
                Assert.False(Page.IsPicking);
                Assert.False(Page.HasItemSelection);
                Assert.Null(Session.SelectedItem);
            });
        }
        finally
        {
            program.On(Page.ClearSelection);
        }
    }

    /// <summary>
    /// Where the play mode makes no copies, Ctrl + C holds nothing to be pasted and Ctrl + V makes none - the Copy
    /// button is hidden there.
    /// </summary>
    [CorpusFact]
    public void Ctrl_V_copies_nothing_where_the_play_mode_makes_no_copies()
    {
        if (!program.Ready(changes: true))
            return;

        Pad.In(program, "Legit", () =>
        {
            program.On(() =>
            {
                Assert.False(Page.Rules.Duplicates);
                Page.ClickTileCommand.Execute(Worn(ItemLocation.Neck).Item);
                int before = Named("Beast Necklace");
                Page.Copy();
                Assert.True(PD2SPCompanion.Shell.Services.Generic.ItemClipboard.Shared.IsEmpty);
                Assert.False(Page.CanPaste);
                Page.PasteCommand.Execute(null);
                Assert.Equal("Nothing to paste.", Session.Status);
                Assert.Equal(before, Named("Beast Necklace"));
            });
        });
    }

    [CorpusFact]
    public void Ctrl_C_then_Ctrl_V_makes_a_second_of_what_is_selected_and_Ctrl_Z_takes_it_back()
    {
        if (!program.Ready(changes: true))
            return;

        int before = 0;
        Pad.In(program, "Free", () =>
        {
            program.On(() =>
            {
                before = Named("Beast Necklace");
                Page.ClickTileCommand.Execute(Worn(ItemLocation.Neck).Item);
                Page.Copy();
                Assert.True(Page.CanPaste);
                Assert.Equal("Paste (Ctrl + V)" + Environment.NewLine + "1 item copied", Page.PasteHint);

                Page.PasteCommand.Execute(null);
                Assert.Equal(before + 1, Named("Beast Necklace"));

                // Written - the pause after a change does it - the copy is a step, and the undo has it.
                Assert.True(program.Get<AutoSave>().Flush());
                Assert.True(Main.Home.UndoCommand.CanExecute(null));
                Main.Home.UndoCommand.Execute(null);
            });

            Assert.True(program.Until(() => Named("Beast Necklace") == before), "the undo did not take the copy back");
            program.On(() => Assert.Equal("Undone.", Session.Status));
        });
    }

    [CorpusFact]
    public void A_number_box_steps_by_one_with_Up_and_Down_and_by_ten_with_the_page_keys_within_its_range()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var box = new NumericTextBox { Minimum = 1, Maximum = 99, Text = "81" };
            string Press(Key key, KeyModifiers with = KeyModifiers.None)
            {
                box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = with });
                return box.Text ?? "";
            }

            Assert.Equal("82", Press(Key.Up));
            Assert.Equal("72", Press(Key.PageDown));
            Assert.Equal("71", Press(Key.Down));
            Assert.Equal("81", Press(Key.PageUp));

            // Held to its ends, either way.
            Press(Key.PageUp);
            Assert.Equal("99", Press(Key.PageUp));
            Assert.Equal("99", Press(Key.Up));
            box.Text = "4";
            Assert.Equal("1", Press(Key.PageDown));
            Assert.Equal("1", Press(Key.Down));

            // With a key held beside it, the arrow is some chord's and no step.
            Assert.Equal("1", Press(Key.Up, KeyModifiers.Control));
        });
    }

    [CorpusFact]
    public void A_number_changed_on_the_Edit_page_is_written_with_no_key_pressed()
    {
        if (!program.Ready(changes: true))
            return;

        Pad.In(program, "Free", () =>
        {
            var written = DateTime.MinValue;
            program.On(() =>
            {
                var edit = program.Get<VP_ViewModel_Inv_R>();
                Page.ClickTileCommand.Execute(Worn(ItemLocation.Neck).Item);
                Assert.Equal("81", edit.Basic.ItemLevel);

                // Nothing waits to be written before the step is made.
                program.Get<AutoSave>().Flush();
                written = File.GetLastWriteTimeUtc(program.SaveOf(Program.Open));
                edit.Basic.ItemLevel = "82";
            });

            // No KEEP, no key: the pause after the last change, and the file is written.
            Assert.True(program.Until(() => File.Exists(program.SaveOf(Program.Open)) && File.GetLastWriteTimeUtc(program.SaveOf(Program.Open)) != written, 8000),
                        "the save was not written by itself");
            program.On(() =>
            {
                var amulet = Pad.File(program, Program.Open).PlayerItemList.Items.First(item => item.Mode == ItemMode.Equipped && item.Location == ItemLocation.Neck);
                Assert.Equal(82, amulet.ItemLevel);
            });
        });
    }

    [CorpusFact]
    public void In_the_vault_F2_names_the_folder_selected_Enter_keeps_the_name_and_Backspace_is_back()
    {
        if (!program.Ready(changes: true))
            return;

        var vault = Main.Vault!;
        try
        {
            program.On(() =>
            {
                Main.ShowLeftCommand.Execute(MainViewModel.VaultMark);
                vault.Show();
            });
            Assert.True(program.Until(() => vault.Places.Any(place => place.Name == "Charms")), "the vault's folders were not listed");

            program.On(() =>
            {
                Assert.Equal("Vault", vault.Place?.Path);

                // F2 with nothing selected names nothing: Explorer's Rename is what is selected's, not the
                // folder shown, where the path stands.
                vault.RenameSelectedFolder();
                Assert.DoesNotContain(vault.SheetFolders, tile => tile.IsRenaming);
                Assert.False(vault.CanRenameFolder);

                // A folder's tile selected, and F2: its name box, on the tile - the page going nowhere.
                var charms = vault.SheetFolders.First(tile => tile.Name == "Charms");
                vault.ClickFolderTileCommand.Execute(charms);
                Assert.True(vault.CanRenameFolder);
                vault.RenameSelectedFolder();
                Assert.True(charms.IsRenaming);
                Assert.Equal("Vault", vault.Place?.Path);
                var rename = charms.Rename!;
                Assert.Equal("Charms", rename.Name);

                // Typed, and Enter.
                rename.Name = "Charmz";
                vault.CommitRename(rename);
            });
            Assert.True(program.Until(() => vault.Places.Any(place => place.Name == "Charmz")), "the folder was not renamed: " + program.On(() => vault.Notice));

            program.On(() =>
            {
                Assert.DoesNotContain(vault.SheetFolders, tile => tile.IsRenaming);
                Assert.Equal("Charmz", vault.SelectedFolder?.Name);

                // Backspace: back to where the page was, the folder above.
                vault.GoToPlace(vault.Places.First(place => place.Name == "Charmz"));
                Assert.Equal(@"Vault\Charmz", vault.Place?.Path);
                Assert.True(vault.CanGoBack);
                vault.GoBackCommand.Execute(null);
                Assert.Equal("Vault", vault.Place?.Path);
            });
        }
        finally
        {
            program.On(() => Main.ShowLeftCommand.Execute(MainViewModel.InventoryMark));
        }
    }
}
