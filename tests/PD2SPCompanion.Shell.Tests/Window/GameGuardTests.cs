using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Tests.Inventory;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Window;

/// <summary>
/// The game guard's gaps, closed. An undo
/// writes nothing while the game runs, nor while the patch guard holds writes - it wrote through the save set past
/// both. The game's start writes the edit waiting only while the game has just started, before it can reach its
/// character screen; seen late, the edit is kept unsaved and the character open.
/// <para>
/// The game is pretended (the program's GameFlag), its start the flag file's time.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class GameGuardTests(Program program)
{
    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;

    private byte[] OnDisk => File.ReadAllBytes(program.SaveOf(Program.Open));

    /// <summary>An edit: the worn gloves taken off.</summary>
    private void Edit() =>
        Main.Inventory!.TakeOff(Session.Equipped.First(slot => slot.Slot.Location == ItemLocation.Gloves && !slot.IsSwap));

    /// <summary>The game pretended to have started <paramref name="ago"/>.</summary>
    private void GameStarted(TimeSpan ago)
    {
        File.WriteAllText(program.GameFlag, "running");
        File.SetLastWriteTimeUtc(program.GameFlag, DateTime.UtcNow - ago);
    }

    /// <summary>
    /// The autosave flushed on the program's thread. Asked from the test's own, it raced the program's autosave timer -
    /// two saves at once, the second finding the file changed under it - and this class's two tests failed now and then
    /// in a full run, never on their own.
    /// </summary>
    private bool SavedOnTheProgramsThread() => program.On(() => program.Saved());

    /// <summary>What held the autosave, should it be held: said in the failure.</summary>
    private string Why() => program.On(() =>
        $"the edit was not saved - game running {Main.Sync?.GameRunning}, write blocked {Main.Sync?.IsWriteBlocked}, "
        + $"guard held {program.Get<PatchGuardService>().IsHeld}, asking {Main.Confirm.IsAsking}, loaded {Session.IsLoaded}, "
        + $"changes {program.Get<SaveSet>().HasChanges}, strip '{Session.Status}'");

    private void GameEnds()
    {
        File.Delete(program.GameFlag);
        program.Until(() => Main.Sync?.IsGameRunning != true, 10000);
    }

    [CorpusFact]
    public void An_undo_as_the_game_starts_writes_nothing_and_the_character_is_closed_for_the_game()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            program.On(Edit);
            Assert.True(SavedOnTheProgramsThread(), Why());
            byte[] before = OnDisk;

            program.On(() =>
            {
                // Before the look every two seconds has seen it: the undo asks the process itself.
                GameStarted(TimeSpan.Zero);
                Assert.Equal("Require game closed to undo.", program.Get<SessionHistory>().Undo());
            });

            Assert.Equal(before, OnDisk);

            // And the game, first noticed by the refusal, closes the character as the look would have.
            Assert.True(program.Until(() => !Session.IsLoaded, 10000), "the character was left open beside the game");
        }
        finally
        {
            GameEnds();
        }
    }

    [CorpusFact]
    public void An_undo_while_the_patch_guard_holds_writes_nothing()
    {
        if (!program.Ready(changes: true))
            return;

        var guard = program.Get<PatchGuardService>();
        try
        {
            program.On(Edit);
            Assert.True(SavedOnTheProgramsThread(), Why());
            byte[] before = OnDisk;

            program.On(() =>
            {
                guard.Reason = "PD2 changed.";
                guard.IsHeld = true;
                Assert.Equal("PD2 changed. Writes held.", program.Get<SessionHistory>().Undo());
            });

            Assert.Equal(before, OnDisk);
        }
        finally
        {
            program.On(() =>
            {
                guard.IsHeld = false;
                guard.Reason = "";
            });
        }
    }

    [CorpusFact]
    public void The_edit_waiting_as_the_game_starts_is_written_while_the_game_is_new()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            byte[] before = OnDisk;

            // In one go on the window's thread, so no look comes between: the autosave then refuses, the game running.
            program.On(() =>
            {
                GameStarted(TimeSpan.Zero);
                Edit();
            });

            Assert.True(program.Until(() => !Session.IsLoaded, 10000), "the character was not closed for the game");
            Assert.NotEqual(before, OnDisk);
            program.On(() => Assert.Equal($"Game started. {Program.Open} closed; reload after game closes.", Session.Status));
        }
        finally
        {
            GameEnds();
        }
    }

    /// <summary>
    /// A backup on screen, edited, as the game starts: neither written, which would be a restore nobody asked for, nor
    /// closed, which would lose the edits. Said; and with nothing done to it, closed as ever.
    /// </summary>
    [CorpusFact]
    public void An_edited_backup_as_the_game_starts_stays_open_unwritten_and_an_untouched_one_closes()
    {
        if (!program.Ready(changes: true))
            return;

        var home = Main.Home;
        void OpenTheBackup()
        {
            program.On(() =>
            {
                Assert.True(Main.ShowPageByKey("Settings_L"));

                // Settings' button lists the open character's copies, whichever card another test left it on.
                if (home.ShowBackups)
                    home.ToggleBackupsCommand.Execute(null);
                home.ToggleBackupsCommand.Execute(null);
                Assert.NotEmpty(home.Backups);
                home.OpenBackupCommand.Execute(home.Backups[0]);
                Assert.True(Session.IsViewingBackup);
            });
        }

        try
        {
            // A backup to open: the file before an edit.
            program.On(() =>
            {
                Edit();
                Assert.True(program.Saved(), "the edit was not written");
            });
            byte[] before = OnDisk;

            OpenTheBackup();
            program.On(() =>
            {
                Edit();
                Assert.True(Session.HasUnsavedChanges);
                GameStarted(TimeSpan.Zero);
            });

            string kept = $"Game started. {Program.Open} backup not restored. Kept open; close game to restore.";
            Assert.True(program.Until(() => Session.Status == kept, 10000), "the backup's edits were not said");
            program.On(() =>
            {
                Assert.True(Session.IsLoaded);
                Assert.True(Session.IsViewingBackup);
                Assert.True(Session.HasUnsavedChanges);
            });
            Assert.Equal(before, OnDisk);
            GameEnds();

            // Nothing done to it: nothing to keep.
            program.On(() =>
            {
                program.Get<SaveSet>().Revert();
                Assert.False(Session.HasUnsavedChanges);
                GameStarted(TimeSpan.Zero);
            });
            Assert.True(program.Until(() => !Session.IsLoaded, 10000), "the untouched backup was not closed for the game");
            program.On(() => Assert.Equal($"Game started. {Program.Open} closed; reload after game closes.", Session.Status));
            Assert.Equal(before, OnDisk);
        }
        finally
        {
            GameEnds();
        }
    }

    [CorpusFact]
    public void The_edit_waiting_as_a_game_long_started_is_seen_is_kept_unsaved_and_the_character_open()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            byte[] before = OnDisk;
            program.On(() =>
            {
                GameStarted(TimeSpan.FromMinutes(1));
                Edit();
            });

            string late = $"Game started. {Program.Open} not saved: Game seen too late. Kept open; close game to save.";
            Assert.True(program.Until(() => Session.Status == late, 10000), "the late game was not said");
            program.On(() =>
            {
                Assert.True(Session.IsLoaded);
                Assert.True(program.Get<SaveSet>().HasChanges);
            });
            Assert.Equal(before, OnDisk);
        }
        finally
        {
            GameEnds();
        }
    }
}
