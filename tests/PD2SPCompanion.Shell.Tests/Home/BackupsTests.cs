using D2SLib.Model.Save;

using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Home;

/// <summary>
/// The backups out of the main flow:
/// everything saves by itself and the undo is the way back,
/// so Restore (the top bar's) and Close backup (the home page's card's) are offered only while a backup is on screen; the open
/// character's backups are counted and listed from Settings; one opened there is put on screen on the home
/// page; Restore asks first, then writes it.
/// <para>
/// What the home page and Settings decide, asked of them as their views ask and read back from the session and
/// the file. That the card draws no Backups and no save button, that the list opens over the Settings page and
/// that a click reaches a row are the UI cases' to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class BackupsTests(Program program)
{
    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Default_L HomePage => Main.Home;
    private VP_ViewModel_Settings_L Settings => program.Get<VP_ViewModel_Settings_L>();

    private ItemTileViewModel Amulet => Session.Equipped.First(slot => slot.Slot.Location == ItemLocation.Neck && !slot.IsSwap).Item!;

    /// <summary>
    /// The worn amulet's item level typed as 82, as the Edit page's box takes it, and written by the autosave -
    /// which takes a backup of the character as it was.
    /// </summary>
    private void StepTheAmulet()
    {
        program.On(() =>
        {
            Assert.Equal("Beast Necklace", Amulet.Name);
            Assert.Equal(81, Amulet.Item.ItemLevel);

            Session.SelectedItem = Amulet;
            program.Get<VP_ViewModel_Inv_R>().Basic.ItemLevel = "82";
            Assert.True(program.Get<AutoSave>().Flush(), "the level was not written");
            Assert.Equal(82, Amulet.Item.ItemLevel);
        });
    }

    /// <summary>Settings shown, as its side bar button shows it: the open character's backups are looked at again.</summary>
    private void ShowSettings() => program.On(() => Assert.True(Main.ShowPageByKey("Settings_L")));

    private void Leave()
    {
        program.On(() =>
        {
            if (Main.Confirm.IsAsking)
                Main.Confirm.RejectCommand.Execute(null);
            HomePage.ShowBackups = false;
            if (Session.IsViewingBackup)
                HomePage.CloseBackupCommand.Execute(null);
        });
    }

    [CorpusFact]
    public void With_no_backup_on_screen_neither_Restore_nor_Close_backup_is_offered()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            Assert.True(HomePage.IsEditing);
            Assert.False(HomePage.ShowsRestore);
            Assert.False(HomePage.HasOpenedBackup);
            Assert.Null(HomePage.OpenedBackup);
        });
    }

    [CorpusFact]
    public void Settings_names_the_open_characters_backups_and_the_newest_says_what_it_was_taken_before()
    {
        if (!program.Ready(changes: true))
            return;

        using var mode = Made.Mode(program, "Free");
        try
        {
            StepTheAmulet();
            ShowSettings();

            program.On(() =>
            {
                Assert.NotEmpty(HomePage.Backups);
                Assert.Equal(Line(HomePage.Backups.Count), HomePage.BackupsLine);
                Assert.StartsWith("Before: Edited Beast Necklace.", HomePage.Backups[0].Caption);

                // Newest first.
                Assert.Equal(HomePage.Backups.OrderByDescending(each => each.Taken).Select(each => each.Path), HomePage.Backups.Select(each => each.Path));
            });
        }
        finally
        {
            Leave();
        }

        static string Line(int count) => count == 1 ? "PhySpearZon: 1 backup" : $"PhySpearZon: {count} backups";
    }

    [CorpusFact]
    public void With_no_character_open_Settings_says_so_in_place_of_a_count()
    {
        if (!program.Ready())
            return;

        program.On(() => HomePage.CloseCommand.Execute(null));
        Assert.True(program.Until(() => !Session.IsLoaded));

        ShowSettings();
        program.On(() => Assert.Equal("No character open", HomePage.BackupsLine));
    }

    [CorpusFact]
    public void A_backup_opened_from_Settings_is_put_on_screen_on_the_home_page_with_Restore_and_Close_backup()
    {
        if (!program.Ready(changes: true))
            return;

        using var mode = Made.Mode(program, "Free");
        try
        {
            StepTheAmulet();
            ShowSettings();

            program.On(() =>
            {
                Assert.IsType<VP_ViewModel_Settings_L>(Main.CurrentPage_L);
                HomePage.ToggleBackupsCommand.Execute(null);
                Assert.True(HomePage.ShowBackups);

                HomePage.OpenBackupCommand.Execute(HomePage.Backups[0]);

                Assert.True(Session.IsViewingBackup);
                Assert.Same(HomePage, Main.CurrentPage_L);
                Assert.False(HomePage.ShowBackups);
                Assert.True(HomePage.ShowsRestore);
                Assert.True(HomePage.HasOpenedBackup);
                Assert.StartsWith("Before: Edited Beast Necklace.", HomePage.OpenedBackup!.Caption);
                Assert.Equal("Restore this backup", HomePage.SaveHint.Split('\n')[0].Trim());

                // The backup is the character as it was before the edit.
                Assert.Equal(81, Amulet.Item.ItemLevel);
            });

            // Close backup: the character itself again, Restore gone.
            program.On(() => HomePage.CloseBackupCommand.Execute(null));
            program.On(() =>
            {
                Assert.False(Session.IsViewingBackup);
                Assert.False(HomePage.ShowsRestore);
                Assert.Equal(82, Amulet.Item.ItemLevel);
            });
        }
        finally
        {
            Leave();
        }
    }

    [CorpusFact]
    public void Restore_asks_first_then_writes_the_backup_and_is_offered_no_more()
    {
        if (!program.Ready(changes: true))
            return;

        using var mode = Made.Mode(program, "Free");
        try
        {
            StepTheAmulet();
            ShowSettings();

            program.On(() =>
            {
                HomePage.OpenBackupCommand.Execute(HomePage.Backups[0]);
                HomePage.SaveCommand.Execute(null);

                Assert.True(Main.Confirm.IsAsking);
                Assert.StartsWith("Current character is backed up first.", Main.Confirm.Message);
                Assert.Equal("RESTORE", Main.Confirm.ConfirmLabel);

                // Nothing written until the answer.
                Assert.True(Session.IsViewingBackup);

                Main.Confirm.AcceptCommand.Execute(null);

                Assert.False(Session.IsViewingBackup);
                Assert.False(HomePage.ShowsRestore);
                Assert.False(HomePage.HasOpenedBackup);
                Assert.Equal(81, Amulet.Item.ItemLevel);
                Assert.StartsWith("Restored. Backup: ", Session.Status);

                // The file is the backup's: what the session holds is what was written.
                Assert.Equal(Session.SavedBytes, File.ReadAllBytes(program.SaveOf(Program.Open)));
            });

            // And read off the disk again, it is the level of before the edit.
            program.On(() => HomePage.LoadCommand.Execute(null));
            Assert.True(program.Until(() => Session.IsLoaded && !HomePage.IsLoading && Amulet.Item.ItemLevel == 81), "the file does not hold the backup");
        }
        finally
        {
            Leave();
        }
    }

    /// <summary>
    /// Never loss; a duplicate is allowed: a restore only adds. What the file being replaced
    /// holds and the backup does not - here a helm come by since the backup, which is in no other place -
    /// goes into the vault's Recovered folder, said before the restore and after; the helm is gone from the
    /// character, as the backup has it, and nowhere lost.
    /// </summary>
    [CorpusFact]
    public void What_the_replaced_file_holds_and_the_backup_does_not_goes_to_the_vaults_Recovered_folder_and_is_said()
    {
        if (!program.Ready(changes: true))
            return;

        using var mode = Made.Mode(program, "Free");
        var vaults = program.Get<PD2SPCompanion.Shell.Services.Vault.VaultService>();
        try
        {
            // A backup of the character as it was; then, since it, a helm that is nowhere else.
            StepTheAmulet();
            uint id = 0;
            program.On(() =>
            {
                var crest = program.Give("harlequin", "Harlequin Crest")!;
                id = crest.Id;
                Assert.True(program.Get<AutoSave>().Flush(), "the helm was not written");
                Assert.Equal(0, vaults.Store!.CountHeld(id, null));
                Assert.DoesNotContain(vaults.Store.Folders(), folder => folder.Name == "Recovered");
            });
            ShowSettings();

            program.On(() =>
            {
                // The newest backup is the file as it was before the helm was written into it.
                HomePage.OpenBackupCommand.Execute(HomePage.Backups[0]);
                Assert.DoesNotContain(Session.HeldItems, item => item.Id == id);

                HomePage.SaveCommand.Execute(null);
                Assert.True(Main.Confirm.IsAsking);
                Assert.Contains("Not in backup, goes to vault folder Recovered: Harlequin Crest.", Main.Confirm.Message);

                // Nothing is in the vault until the answer.
                Assert.Equal(0, vaults.Store!.CountHeld(id, null));
                Main.Confirm.AcceptCommand.Execute(null);

                Assert.False(Session.IsViewingBackup);
                Assert.Contains("Not in backup, put in vault folder Recovered: Harlequin Crest.", Session.Status);

                // In the vault, in its folder; not on the character, which is the backup's.
                Assert.Equal(1, vaults.Store.CountHeld(id, null));
                var folder = Assert.Single(vaults.Store.Folders(), each => each.Name == "Recovered");
                Assert.Null(folder.ParentId);
                Assert.Equal(1, vaults.Store.Count(folder.Id));
                Assert.DoesNotContain(Session.HeldItems, item => item.Id == id);
            });
        }
        finally
        {
            Leave();
        }
    }

    /// <summary>
    /// A stack of runes has no id to know it by: what the file holds over the backup goes to the vault by
    /// count - three more of a kind now than then, a stack of three into the vault. It may make a copy; it
    /// cannot lose one.
    /// </summary>
    [CorpusFact]
    public void Runes_come_by_since_the_backup_go_to_the_vault_by_count()
    {
        if (!program.Ready(changes: true))
            return;

        using var mode = Made.Mode(program, "Free");
        var vaults = program.Get<PD2SPCompanion.Shell.Services.Vault.VaultService>();
        try
        {
            StepTheAmulet();
            // The stack of Ist runes, as the materials tab hands one out: three, on the character.
            const string code = "r24s";
            string named = PD2SPCompanion.Core.Items.ItemPresenter.BaseName(code);
            program.On(() =>
            {
                Assert.DoesNotContain(Session.HeldItems, item => item.Code.Trim() == code);
                var stack = PD2SPCompanion.Core.Items.ItemForge.Material(code, 3, Session.ItemEra!);
                Assert.Null(Session.Receive(stack, "no room"));
                Assert.True(program.Get<AutoSave>().Flush(), "the runes were not written");
            });
            ShowSettings();

            program.On(() =>
            {
                int held = vaults.Store!.Count();
                HomePage.OpenBackupCommand.Execute(HomePage.Backups[0]);
                HomePage.SaveCommand.Execute(null);
                Assert.Contains($"Not in backup, goes to vault folder Recovered: 3 {named}.", Main.Confirm.Message);
                Main.Confirm.AcceptCommand.Execute(null);

                // One stack more in the vault, in its folder; the character holds what the backup did.
                Assert.Equal(held + 1, vaults.Store.Count());
                var folder = Assert.Single(vaults.Store.Folders(), each => each.Name == "Recovered");
                Assert.Equal(1, vaults.Store.Count(folder.Id));
                Assert.DoesNotContain(Session.HeldItems, item => item.Code.Trim() == code);
            });
        }
        finally
        {
            Leave();
        }
    }

    /// <summary>
    /// Season 14's plain stack, which the tab hands out and which grows under the id it had: in file and backup alike by
    /// id, so only its count tells what it came by since.
    /// </summary>
    [CorpusFact]
    public void A_plain_stack_grown_since_the_backup_sends_what_it_grew_by_to_the_vault()
    {
        if (!program.Ready(changes: true))
            return;

        using var mode = Made.Mode(program, "Free");
        var vaults = program.Get<PD2SPCompanion.Shell.Services.Vault.VaultService>();
        try
        {
            const string code = "r24";
            string named = PD2SPCompanion.Core.Items.ItemPresenter.BaseName(code);
            Item stack = null!;
            program.On(() =>
            {
                Assert.DoesNotContain(Session.HeldItems, item => item.Code.Trim() == code);
                stack = PD2SPCompanion.Core.Items.ItemForge.Material(code, 10, Session.ItemEra!);
                Assert.False(stack.IsCompact, $"{code} does not stack itself in this season");
                stack.Id = 0x7E5A0001;
                Assert.Null(Session.Receive(stack, "no room"));
                Assert.True(program.Get<AutoSave>().Flush(), "the stack was not written");
            });

            // The next save's backup holds ten of it. The stack grows, the id the same; a second save takes that backup.
            program.On(() =>
            {
                var held = Session.HeldItems.Single(item => item.Code.Trim() == code);
                held.Quantity = 15;
                Session.MarkEdited();
                Assert.True(program.Get<AutoSave>().Flush(), "the growth was not written");
            });
            ShowSettings();

            program.On(() =>
            {
                // The backup that holds it: the two saves may share a second, and so their place in the list.
                var withStack = HomePage.Backups.First(each => SaveVerifier.Load(each.Path).Save.PlayerItemList.Items.Any(item => item.Code.Trim() == code));
                Session.UseMyLayout();
                HomePage.OpenBackupCommand.Execute(withStack);
                Assert.Equal(10, Session.HeldItems.Single(item => item.Code.Trim() == code).Quantity);
                HomePage.SaveCommand.Execute(null);
                Assert.Contains($"Not in backup, goes to vault folder Recovered: 5 {named}.", Main.Confirm.Message);
                Main.Confirm.AcceptCommand.Execute(null);

                var folder = Assert.Single(vaults.Store!.Folders(), each => each.Name == "Recovered");
                Assert.Equal(1, vaults.Store.Count(folder.Id));
            });
        }
        finally
        {
            Leave();
        }
    }

    /// <summary>
    /// Every compact item the backup has fewer of comes back by count, not only what a socket takes -
    /// here a quest item. What a belt takes does not: a potion stays in the replaced file's copy.
    /// </summary>
    [CorpusFact]
    public void A_compact_item_come_by_since_the_backup_goes_to_the_vault_and_a_potion_does_not()
    {
        if (!program.Ready(changes: true))
            return;

        using var mode = Made.Mode(program, "Free");
        var vaults = program.Get<PD2SPCompanion.Shell.Services.Vault.VaultService>();
        try
        {
            StepTheAmulet();
            const string quest = "j34", potion = "hp1";
            string named = PD2SPCompanion.Core.Items.ItemPresenter.BaseName(quest);
            program.On(() =>
            {
                foreach (string code in new[] { quest, potion })
                {
                    Assert.DoesNotContain(Session.HeldItems, item => item.Code.Trim() == code);
                    var made = PD2SPCompanion.Core.Items.ItemForge.Material(code, 1, Session.ItemEra!);
                    Assert.True(made.IsCompact, $"{code} is no compact item");
                    Assert.Null(Session.Receive(made, "no room"));
                }

                Assert.True(program.Get<AutoSave>().Flush(), "the items were not written");
            });
            ShowSettings();

            program.On(() =>
            {
                int held = vaults.Store!.Count();
                HomePage.OpenBackupCommand.Execute(HomePage.Backups[0]);
                HomePage.SaveCommand.Execute(null);
                Assert.Contains($"Not in backup, goes to vault folder Recovered: {named}.", Main.Confirm.Message);
                Main.Confirm.AcceptCommand.Execute(null);

                Assert.Equal(held + 1, vaults.Store.Count());
                var folder = Assert.Single(vaults.Store.Folders(), each => each.Name == "Recovered");
                Assert.Equal(1, vaults.Store.Count(folder.Id));
                Assert.DoesNotContain(Session.HeldItems, item => item.Code.Trim() == quest);
            });
        }
        finally
        {
            Leave();
        }
    }

    /// <summary>
    /// A backup on screen, edited - which the autosave does not write. Another row clicked
    /// asks first, as closing does; CLOSE ANYWAY opens it, and until the answer the edit stands.
    /// </summary>
    [CorpusFact]
    public void Opening_a_backup_over_unsaved_edits_asks_first()
    {
        if (!program.Ready(changes: true))
            return;

        using var mode = Made.Mode(program, "Free");
        try
        {
            StepTheAmulet();
            program.On(() =>
            {
                Session.SelectedItem = Amulet;
                program.Get<VP_ViewModel_Inv_R>().Basic.ItemLevel = "83";
                Assert.True(program.Get<AutoSave>().Flush(), "the second level was not written");
            });
            ShowSettings();

            program.On(() =>
            {
                Assert.True(HomePage.Backups.Count >= 2, "two backups are wanted");
                var first = HomePage.Backups[0];
                var other = HomePage.Backups[1];
                HomePage.OpenBackupCommand.Execute(first);
                Assert.True(Session.IsViewingBackup);
                Assert.False(Main.Confirm.IsAsking);

                int level = Amulet.Item.ItemLevel;
                Session.SelectedItem = Amulet;
                program.Get<VP_ViewModel_Inv_R>().Basic.ItemLevel = "70";
                Assert.True(Session.HasUnsavedChanges);

                HomePage.OpenBackupCommand.Execute(other);

                Assert.True(Main.Confirm.IsAsking);
                Assert.Equal("CLOSE ANYWAY", Main.Confirm.ConfirmLabel);
                Assert.Contains("has unsaved changes", Main.Confirm.Message);
                Assert.Equal(70, Amulet.Item.ItemLevel);

                Main.Confirm.RejectCommand.Execute(null);
                Assert.Equal(70, Amulet.Item.ItemLevel);

                HomePage.OpenBackupCommand.Execute(other);
                Main.Confirm.AcceptCommand.Execute(null);

                Assert.False(Session.HasUnsavedChanges);
                Assert.True(Session.IsViewingBackup);
                Assert.NotEqual(70, Amulet.Item.ItemLevel);
            });
        }
        finally
        {
            Leave();
        }
    }

    /// <summary>
    /// A backup is the file from BEFORE a save, so none holds what the last save brought. The rolling copy
    /// does: once the file is no longer what the companion wrote - here written over from outside, as the
    /// game or a failed disk would - it leads the list, and restoring it brings the last save's work back.
    /// While the file is what was written, it is not offered: there is nothing in it the file does not hold.
    /// </summary>
    [CorpusFact]
    public void The_file_as_last_saved_here_leads_the_list_once_the_file_is_no_longer_it_and_brings_the_last_save_back()
    {
        if (!program.Ready(changes: true))
            return;

        using var mode = Made.Mode(program, "Free");
        try
        {
            StepTheAmulet();
            ShowSettings();

            program.On(() =>
            {
                string file = program.SaveOf(Program.Open);
                string last = SaveWriter.AsWrittenPath(file);

                // Written with the save, and the same as the file: not in the list.
                Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(last));
                Assert.DoesNotContain(HomePage.Backups, each => each.Path.EndsWith(SaveWriter.AsWrittenSuffix));

                // The file written over from outside, with the character as it was before the edit.
                File.Copy(HomePage.Backups[0].Path, file, overwrite: true);
                HomePage.LookAtBackups();

                Assert.Equal(last, HomePage.Backups[0].Path, ignoreCase: true);
                Assert.Equal("As last saved here.", HomePage.Backups[0].Caption);
                Assert.StartsWith("Before: Edited Beast Necklace.", HomePage.Backups[1].Caption);

                // Opened, it is the character with the edit; restored, the file holds it again.
                HomePage.OpenBackupCommand.Execute(HomePage.Backups[0]);
                Assert.True(Session.IsViewingBackup);
                Assert.Equal(82, Amulet.Item.ItemLevel);

                HomePage.SaveCommand.Execute(null);
                Assert.True(Main.Confirm.IsAsking);
                Main.Confirm.AcceptCommand.Execute(null);

                Assert.False(Session.IsViewingBackup);
                Assert.Equal(82, Amulet.Item.ItemLevel);
                Assert.Equal(Session.SavedBytes, File.ReadAllBytes(file));

                // The file is what was written once more: the copy steps out of the list.
                HomePage.LookAtBackups();
                Assert.DoesNotContain(HomePage.Backups, each => each.Path.EndsWith(SaveWriter.AsWrittenSuffix));
            });
        }
        finally
        {
            Leave();
        }
    }

    /// <summary>The shared stash is every character's: its copy as last written is one, at the backups' root.</summary>
    [CorpusFact]
    public void The_shared_stash_as_last_written_is_kept_once_for_every_character()
    {
        if (!program.Ready(changes: true))
            return;

        const string Piece = "Death Cleaver", Shared = "Shared Page 2";
        var stash = program.Get<SharedStashSession>();
        string file = Path.Combine(Session.SaveFolder, stash.FileName);
        string last = Path.Combine(SaveWriter.BackupRoot(Session.SaveFolder), stash.FileName + SaveWriter.AsWrittenSuffix);

        program.On(() =>
        {
            var page = Main.Inventory!;
            Vault.VaultHands.Section(program).SelectedPage = Vault.VaultHands.Choice(program, Shared);
            page.ClickTileCommand.Execute(Vault.VaultHands.Tile(program, Piece, Shared));
            page.StoreToVaultCommand.Execute(null);
            Assert.Equal($"{Piece} stored in vault.", Session.Status);
            program.Get<AutoSave>().Flush();

            Assert.True(File.Exists(last), "the stash as last written was not kept");
            Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(last));
            Assert.Equal(stash.SavedBytes, File.ReadAllBytes(last));
        });
    }

    [CorpusFact]
    public void A_restore_that_would_lose_nothing_puts_nothing_in_the_vault()
    {
        if (!program.Ready(changes: true))
            return;

        using var mode = Made.Mode(program, "Free");
        var vaults = program.Get<PD2SPCompanion.Shell.Services.Vault.VaultService>();
        try
        {
            StepTheAmulet();
            ShowSettings();
            program.On(() =>
            {
                int held = vaults.Store!.Count();
                HomePage.OpenBackupCommand.Execute(HomePage.Backups[0]);
                HomePage.SaveCommand.Execute(null);

                // The amulet is in the backup too, at its level of then: nothing would be lost, nothing is said of the vault.
                Assert.DoesNotContain("Recovered", Main.Confirm.Message);
                Main.Confirm.AcceptCommand.Execute(null);

                Assert.Equal(held, vaults.Store.Count());
                Assert.DoesNotContain(vaults.Store.Folders(), folder => folder.Name == "Recovered");
            });
        }
        finally
        {
            Leave();
        }
    }

    /// <summary>
    /// Backups in a character card's menu: the list is that card's save's with no
    /// character open, and a backup picked from it is loaded as that character - where the list was the open
    /// character's alone, and a closed one had to be opened for its backups to be reached.
    /// </summary>
    [CorpusFact]
    public void A_backup_picked_from_a_closed_characters_card_is_opened_as_that_character()
    {
        if (!program.Ready(changes: true))
            return;

        using var mode = Made.Mode(program, "Free");
        try
        {
            StepTheAmulet();
            program.On(() => HomePage.CloseCommand.Execute(null));
            Assert.True(program.Until(() => !Session.IsLoaded && HomePage.IsBrowsable && HomePage.Characters.Any(card => card.Name == "PhySpearZon")),
                        "the list of characters is not shown");

            program.On(() =>
            {
                HomePage.SelectedIndex = HomePage.Characters.ToList().FindIndex(card => card.Name == "PhySpearZon");

                // The card's own button, beside Rename and Clone: the menu's row is never the only way.
                HomePage.ShowSelectedBackupsCommand.Execute(null);
                Assert.True(HomePage.ShowBackups);
                Assert.NotEmpty(HomePage.Backups);
                HomePage.CloseBackupPickerCommand.Execute(null);
                Assert.False(HomePage.ShowBackups);

                var menu = HomePage.MenuForSelected();
                menu.Rows.First(row => row.Label == "Backups").Run();

                // Listed with nothing open: the card's save's own.
                Assert.True(HomePage.ShowBackups);
                Assert.False(Session.IsLoaded);
                Assert.NotEmpty(HomePage.Backups);
                Assert.StartsWith("Before: Edited Beast Necklace.", HomePage.Backups[0].Caption);

                HomePage.OpenBackupCommand.Execute(HomePage.Backups[0]);
            });

            Assert.True(program.Until(() => Session.IsLoaded && Session.IsViewingBackup), "the backup was not opened");
            program.On(() =>
            {
                Assert.Equal("PhySpearZon", Session.CharacterName);
                Assert.False(HomePage.ShowBackups);
                Assert.True(HomePage.HasOpenedBackup);

                // The character as it was before the edit.
                Assert.Equal(81, Amulet.Item.ItemLevel);
            });
        }
        finally
        {
            Leave();
        }
    }

    /// <summary>What lies on a corpse goes with the file as anything else does: not in the backup, it goes to Recovered.</summary>
    [CorpusFact]
    public void What_lies_on_a_corpse_and_not_in_the_backup_goes_to_the_vaults_Recovered_folder()
    {
        const string Dead = "LightFury";
        string specimen = Path.Combine(Program.Corpus, "specimens", "corpse-twelve-worn-items-s13.d2s");
        if (!program.Ready(changes: true) || !File.Exists(specimen))
            return;

        using var mode = Made.Mode(program, "Free");
        string named = "";
        try
        {
            program.On(() => File.Copy(specimen, program.SaveOf(Dead), overwrite: true));
            Made.Open(program, Dead);

            // Taken on and written: a backup with the corpse, and a file without it.
            program.On(() =>
            {
                Assert.True(Session.HasCorpse);
                var gone = Main.Inventory!.CorpseSlots.Select(slot => slot.Item!.Item).First(item => !item.IsCompact && item.Id != 0);
                named = PD2SPCompanion.Core.Items.ItemPresenter.Describe(gone).Name;
                Main.Inventory!.TakeFromCorpseCommand.Execute(null);
                Assert.True(program.Get<AutoSave>().Flush(), "the taking on was not written");

                // One of them gone from the file: it is on the corpse of the backup and nowhere else.
                Assert.NotNull(Session.Release(gone));
                Assert.True(program.Get<AutoSave>().Flush(), "its going was not written");
            });
            ShowSettings();

            // That backup restored: the corpse is in the file again, and a backup without it is kept. Found by what it
            // holds: the second write kept a backup too, the newer, and the two may fall in one second or in two.
            program.On(() =>
            {
                bool found = false;
                foreach (var backup in HomePage.Backups.ToList())
                {
                    HomePage.OpenBackupCommand.Execute(backup);
                    if (found = Session.HasCorpse)
                        break;
                }
                Assert.True(found, "no backup with the corpse");
                HomePage.SaveCommand.Execute(null);
                if (Main.Confirm.IsAsking)
                    Main.Confirm.AcceptCommand.Execute(null);
                Assert.False(Session.IsViewingBackup);
            });
            ShowSettings();

            program.On(() =>
            {
                // The kept copy of the file as it was with the items taken on: no corpse in it.
                bool opened = false;
                foreach (var backup in HomePage.Backups.ToList())
                {
                    HomePage.OpenBackupCommand.Execute(backup);
                    if (opened = !Session.HasCorpse)
                        break;
                }
                Assert.True(opened, "no backup without the corpse");
                HomePage.SaveCommand.Execute(null);
                Assert.True(Main.Confirm.IsAsking);
                Assert.Contains($"goes to vault folder Recovered: {named}", Main.Confirm.Message);
            });
        }
        finally
        {
            Leave();
            Made.Gone(program, Dead);
        }
    }
}
