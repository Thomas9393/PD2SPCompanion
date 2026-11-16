using D2SLib.Model.Save;

using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Season;

/// <summary>
/// The patch guard, of the utmost importance.
/// A new PD2 build whose saves all read back becomes the known
/// good one, and says so - the first run's, without a word. A new build with a save that does not: writes
/// held - the character still opens, an edit is not written - until Accept, which lifts it and lets the edit
/// be written. The build changing under the running program: writes held until a restart, with no Accept.
/// <para>
/// The program's own guard, asked what the start and the window's coming back ask of it (CheckAtStart,
/// CheckAgain). The known good build is the run's own, in a file beside its save folder, as every test's is;
/// the save that does not read back and the build that changed are named through the guard's own switches
/// for tests (PD2SPC_FAKE_UNREADABLE, PD2SPC_FAKE_BUILD). That the start and the window's coming back do
/// ask, that the strip draws what is said and that Settings draws Accept only where it may be pressed, are
/// the UI cases' to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class PatchGuardTests(Program program)
{
    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private PatchGuardService Guard => program.Get<PatchGuardService>();

    /// <summary>The build the tables in memory were read from.</summary>
    private string Build => Session.Season!.Build;

    /// <summary>Where a test's known good build is kept: beside its save folder (PatchGuardService).</summary>
    private string KnownFile => Path.Combine(program.Folder, "pd2spc-verified-build.txt");

    private string Known => File.Exists(KnownFile) ? File.ReadAllText(KnownFile).Trim() : "";

    /// <summary>The guard as the fixture has it - holding nothing - and no known build, no switch left behind.</summary>
    private void Leave()
    {
        Environment.SetEnvironmentVariable(PatchGuard.FakeUnreadable, null);
        Environment.SetEnvironmentVariable(PatchGuard.FakeBuild, null);
        program.On(() =>
        {
            Guard.Failed.Clear();
            Guard.Repairable.Clear();
            Guard.CanAccept = false;
            Guard.CanRepair = false;
            Guard.Reason = "";
            Guard.IsHeld = false;
        });
        File.Delete(KnownFile);
    }

    [CorpusFact]
    public void The_first_run_stores_the_build_and_says_nothing()
    {
        if (!program.Ready())
            return;

        try
        {
            File.Delete(KnownFile);
            program.On(() =>
            {
                var said = Session.Messages.Latest;
                Guard.CheckAtStart();

                Assert.Equal(Build, Known);
                Assert.False(Guard.IsHeld);
                Assert.Same(said, Session.Messages.Latest);
            });
        }
        finally
        {
            Leave();
        }
    }

    [CorpusFact]
    public void A_new_build_whose_saves_all_read_back_becomes_the_known_good_one_and_says_so()
    {
        if (!program.Ready())
            return;

        try
        {
            File.WriteAllText(KnownFile, "test-build-0");
            program.On(() =>
            {
                Guard.CheckAtStart();

                Assert.Equal("PD2 changed: 2 characters and the shared stash read back exactly.", Session.Status);
                Assert.Equal(Build, Known);
                Assert.False(Guard.IsHeld);

                // A test's known good build is kept beside its saves: the settings are never told of it.
                Assert.Equal("", program.Get<EditorSettings>().VerifiedBuild);
            });
        }
        finally
        {
            Leave();
        }
    }

    [CorpusFact]
    public void The_same_build_is_not_checked_again()
    {
        if (!program.Ready())
            return;

        try
        {
            Environment.SetEnvironmentVariable(PatchGuard.FakeUnreadable, Program.Open + ".d2s");
            program.On(() =>
            {
                File.WriteAllText(KnownFile, Build);
                var said = Session.Messages.Latest;
                Guard.CheckAtStart();

                Assert.False(Guard.IsHeld);
                Assert.Same(said, Session.Messages.Latest);
            });
        }
        finally
        {
            Leave();
        }
    }

    [CorpusFact]
    public void A_new_build_with_a_save_that_does_not_read_back_holds_writes_until_Accept()
    {
        if (!program.Ready())
            return;

        try
        {
            File.WriteAllText(KnownFile, "test-build-a");
            Environment.SetEnvironmentVariable(PatchGuard.FakeUnreadable, Program.Open + ".d2s");
            program.On(() =>
            {
                Guard.CheckAtStart();

                Assert.True(Guard.IsHeld);
                Assert.True(Guard.CanAccept);
                Assert.Equal("PD2 changed: 1 of 3 files do not read back.", Guard.Reason);
                Assert.Equal("PD2 changed: 1 of 3 files do not read back. Writes held.", Session.Status);
                Assert.Equal(Program.Open + ".d2s", Assert.Single(Guard.Failed).File);
                Assert.Equal("test-build-a", Known);
                Assert.True(Main.Sync!.IsWriteBlocked);

                // Accept, in Settings: it becomes the known good build.
                Assert.Same(Guard, program.Get<VP_ViewModel_Settings_L>().Guard);
                Guard.AcceptCommand.Execute(null);

                Assert.False(Guard.IsHeld);
                Assert.False(Guard.CanAccept);
                Assert.Empty(Guard.Failed);
                Assert.Equal("PD2 version accepted. Writes allowed.", Session.Status);
                Assert.Equal(Build, Known);
                Assert.False(Main.Sync.IsWriteBlocked);
            });
        }
        finally
        {
            Leave();
        }
    }

    [CorpusFact]
    public void While_held_the_character_still_opens_an_edit_is_not_written_and_Accept_writes_it()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            File.WriteAllText(KnownFile, "test-build-a");
            Environment.SetEnvironmentVariable(PatchGuard.FakeUnreadable, Program.Open + ".d2s");
            program.On(() =>
            {
                Guard.CheckAtStart();
                Assert.True(Guard.IsHeld);

                // Opened again while held: reading is not writing.
                Main.Home.LoadCommand.Execute(null);
            });
            Assert.True(program.Until(() => Session.IsLoaded && !Main.Home.IsLoading && Session.Status == "Opened PhySpearZon.", 15000), "the character did not open while held");

            byte[] before = File.ReadAllBytes(program.SaveOf(Program.Open));
            program.On(() =>
            {
                // An edit: the worn gloves taken off.
                Main.Inventory!.TakeOff(Session.Equipped.First(slot => slot.Slot.Location == ItemLocation.Gloves && !slot.IsSwap));
                Assert.True(program.Get<SaveSet>().HasChanges);

                Assert.False(program.Get<AutoSave>().Flush());
                Assert.Equal(before, File.ReadAllBytes(program.SaveOf(Program.Open)));

                // And every button that writes says why it is off.
                Assert.True(Main.Home.GameBlocked);
                Assert.Equal("PD2 changed: 1 of 3 files do not read back. Writes held.", Main.Home.RenameHint);

                // The backups list too, which restores and deletes the save's copies.
                Assert.Equal(Main.Home.RenameHint, Main.Home.BackupsHint);
                Assert.False(Main.Home.CanShowBackups);
                Main.Home.ToggleBackupsCommand.Execute(null);
                Assert.False(Main.Home.ShowBackups);

                Guard.AcceptCommand.Execute(null);
            });

            // The edit made while held is written by itself, a moment after.
            Assert.True(program.Until(() => !File.ReadAllBytes(program.SaveOf(Program.Open)).AsSpan().SequenceEqual(before), 5000), "the edit made while held was not written after Accept");
            program.On(() => Assert.False(program.Get<SaveSet>().HasChanges));
        }
        finally
        {
            Leave();
        }
    }

    /// <summary>
    /// The encoding repair (B and C): a patch within the season widened a stat, and the other
    /// character was last written by the build before it. The new build's check finds it the earlier build's, holds
    /// writes and offers REPAIR; REPAIR writes it again in this build's encoding, every value kept, and writes go on.
    /// </summary>
    [CorpusFact]
    public void A_file_an_earlier_build_wrote_is_found_held_and_repaired_with_every_value_kept()
    {
        if (!program.Ready(changes: true))
            return;

        string season = program.On(() => PD2SPCompanion.Core.SeasonSnapshot.NameFor(Session.Season!));
        string builds = Path.Combine(PD2SPCompanion.Core.SeasonSnapshot.FolderFor(season), PD2SPCompanion.Core.SeasonSnapshot.BuildsFolder);
        string save = program.SaveOf(Program.Other);
        try
        {
            string before = WrittenByAnEarlierBuild(builds, save);
            program.On(() =>
            {
                Guard.CheckAtStart();

                Assert.True(Guard.IsHeld);
                Assert.True(Guard.CanRepair);
                Assert.False(Guard.CanAccept);
                Assert.Equal("PD2 changed how items are stored: 1 file needs repair.", Guard.Reason);
                var found = Assert.Single(Guard.Repairable);
                Assert.Equal(Program.Other + ".d2s", found.Name);
                Assert.Equal($"written by {season} build test-build-old", found.Said);

                Guard.RepairCommand.Execute(null);

                Assert.False(Guard.IsHeld);
                Assert.Empty(Guard.Repairable);
                Assert.Equal("Repaired 1 file. Writes allowed.", Session.Status);
                Assert.Equal(Build, Known);
                Assert.Equal(before, Values(SaveVerifier.Load(save).Save));
                Session.UseMyLayout();
            });
        }
        finally
        {
            program.On(() => Guard.Repairable.Clear());
            program.On(() => Guard.CanRepair = false);
            if (Directory.Exists(builds))
                Directory.Delete(builds, recursive: true);
            Leave();
        }
    }

    /// <summary>
    /// The other character, last written by the build before a patch: this one's tables, its most held stat two bits
    /// wider, kept as a snapshot keeps it. That build the known good one. Answers its values, as the repair must keep them.
    /// </summary>
    private string WrittenByAnEarlierBuild(string builds, string save)
    {
        string kept = Path.Combine(builds, "test-build-old");
        string stat = program.On(() =>
        {
            var (other, _) = SaveVerifier.Load(save);
            return other.PlayerItemList.Items.Where(item => !item.IsCompact).SelectMany(ItemStats.Every)
                .GroupBy(each => each.Stat).OrderByDescending(group => group.Count()).First().Key!;
        });
        Widened(kept, stat);
        string before = program.On(() => Values(SaveVerifier.Load(save).Save));

        program.On(() =>
        {
            var (other, _) = SaveVerifier.Load(save);
            using (PD2SPCompanion.Core.Season.Open(Session.GameRoot, kept, announce: false))
                File.WriteAllBytes(save, SaveWriter.Prepared(other));
            Session.Season!.Reinstall();
            Session.UseMyLayout();
        });

        File.WriteAllText(KnownFile, "test-build-old");
        return before;
    }

    /// <summary>
    /// A file the repair tried and could not write - here read only - stays named, with why,
    /// and Accept takes the hold over it. Left repairable, every press failed the same way and no button lifted the hold.
    /// </summary>
    [CorpusFact]
    public void A_file_the_repair_could_not_write_is_named_with_why_and_Accept_lifts_the_hold()
    {
        if (!program.Ready(changes: true))
            return;

        string season = program.On(() => PD2SPCompanion.Core.SeasonSnapshot.NameFor(Session.Season!));
        string builds = Path.Combine(PD2SPCompanion.Core.SeasonSnapshot.FolderFor(season), PD2SPCompanion.Core.SeasonSnapshot.BuildsFolder);
        string save = program.SaveOf(Program.Other);
        try
        {
            WrittenByAnEarlierBuild(builds, save);
            File.SetAttributes(save, FileAttributes.ReadOnly);
            program.On(() =>
            {
                Guard.CheckAtStart();
                Assert.True(Guard.CanRepair);
                Assert.False(Guard.CanAccept);

                Guard.RepairCommand.Execute(null);

                Assert.True(Guard.IsHeld);
                Assert.False(Guard.CanRepair);
                Assert.Empty(Guard.Repairable);
                Assert.True(Guard.CanAccept);
                var failed = Assert.Single(Guard.Failed);
                Assert.Equal(Program.Other + ".d2s", failed.File);
                Assert.False(string.IsNullOrEmpty(failed.Problem));
                Assert.Contains($"{Program.Other}.d2s not repaired: ", Session.Status);

                Guard.AcceptCommand.Execute(null);

                Assert.False(Guard.IsHeld);
                Assert.Equal(Build, Known);
            });
        }
        finally
        {
            // The file, and the copy of it the refused write took, which came out read only as well: the fixture deletes them.
            foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(save)!, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            if (Directory.Exists(builds))
                Directory.Delete(builds, recursive: true);
            Leave();
        }
    }

    /// <summary>
    /// PD2 patched while a repair is offered. The tables in memory are stale, so nothing is
    /// offered and nothing is written - seen as the window comes back, or at the press itself - and the hold stays.
    /// </summary>
    [CorpusTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_patch_during_a_repair_hold_takes_the_repair_away_and_writes_nothing(bool windowCameBack)
    {
        if (!program.Ready(changes: true))
            return;

        string season = program.On(() => PD2SPCompanion.Core.SeasonSnapshot.NameFor(Session.Season!));
        string builds = Path.Combine(PD2SPCompanion.Core.SeasonSnapshot.FolderFor(season), PD2SPCompanion.Core.SeasonSnapshot.BuildsFolder);
        string save = program.SaveOf(Program.Other);
        try
        {
            WrittenByAnEarlierBuild(builds, save);
            byte[] before = File.ReadAllBytes(save);
            program.On(() =>
            {
                Guard.CheckAtStart();
                Assert.True(Guard.CanRepair);

                Environment.SetEnvironmentVariable(PatchGuard.FakeBuild, "test-build-d");
                if (windowCameBack)
                {
                    Main.Sync!.Check();
                    Assert.False(Guard.CanRepair);
                    Assert.Empty(Guard.Repairable);
                }

                Guard.RepairCommand.Execute(null);

                Assert.True(Guard.IsHeld);
                Assert.False(Guard.CanRepair);
                Assert.False(Guard.CanAccept);
                Assert.Equal("PD2 changed while open. Require restart.", Guard.Reason);
                Assert.Equal(before, File.ReadAllBytes(save));
                Assert.Equal("test-build-old", Known);

                // Looked at again, nothing more is said: the stale hold stands.
                var said = Session.Messages.Latest;
                Main.Sync!.Check();
                Assert.Same(said, Session.Messages.Latest);
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable(PatchGuard.FakeBuild, null);
            if (Directory.Exists(builds))
                Directory.Delete(builds, recursive: true);
            Leave();
        }
    }

    /// <summary>
    /// A first run's CONTINUE. The start's check found no folder to look at; settled, writes
    /// are held at once, and the check runs after all - here on a build whose save does not read back, so the hold stands.
    /// </summary>
    [CorpusFact]
    public void A_folder_settled_while_running_holds_writes_until_the_check_has_run()
    {
        if (!program.Ready())
            return;

        var setup = program.Get<GameFolderSetup>();
        var settings = program.Get<EditorSettings>();
        try
        {
            File.WriteAllText(KnownFile, "test-build-a");
            Environment.SetEnvironmentVariable(PatchGuard.FakeUnreadable, Program.Open + ".d2s");
            program.On(() =>
            {
                setup.Folder = Program.GameRoot;
                setup.AcceptCommand.Execute(null);

                // Before the check has had its turn: nothing writes.
                Assert.True(Guard.IsHeld);
                Assert.True(Main.Sync!.IsWriteBlocked);
                Assert.False(Guard.CanAccept);
            });

            Assert.True(program.Until(() => Guard.CanAccept), "the check did not run once the folder was settled");
            program.On(() =>
            {
                Assert.Equal(Program.Open + ".d2s", Assert.Single(Guard.Failed).File);
                Assert.Equal("PD2 changed: 1 of 3 files do not read back.", Guard.Reason);
            });

            // Nothing wrong with the saves: the hold goes by itself, and the build is the known good one.
            program.On(() => Guard.AcceptCommand.Execute(null));
            File.WriteAllText(KnownFile, "test-build-b");
            Environment.SetEnvironmentVariable(PatchGuard.FakeUnreadable, null);
            program.On(() =>
            {
                setup.AcceptCommand.Execute(null);
                Assert.True(Guard.IsHeld);
            });

            Assert.True(program.Until(() => !Guard.IsHeld), "the hold outlasted the check");
            string build = program.On(() => Build);
            Assert.True(program.Until(() => Known == build), "the check did not take the build as known good");
        }
        finally
        {
            Assert.True(program.Until(() => !Main.Confirm.IsAsking || Reject()));
            program.On(() => settings.GameFolder = "");
            Leave();
        }

        bool Reject()
        {
            Main.Confirm.RejectCommand.Execute(null);
            return true;
        }
    }

    /// <summary>
    /// A character's migration writes saves and the shared stash, and the hold covers it. Each
    /// chosen is refused with what the hold says, its file untouched, and Accept lifts it.
    /// </summary>
    [CorpusFact]
    public void While_held_a_characters_migration_is_refused_and_writes_nothing()
    {
        if (!program.Ready())
            return;

        var migration = program.Get<PD2SPCompanion.Shell.Services.Vault.SeasonMigrationService>();
        string save = program.SaveOf(Program.Other);
        string stash = Path.Combine(program.Folder, "saves", "pd2_shared.stash");
        try
        {
            File.WriteAllText(KnownFile, "test-build-a");
            Environment.SetEnvironmentVariable(PatchGuard.FakeUnreadable, Program.Open + ".d2s");
            byte[] character = File.ReadAllBytes(save), shared = File.ReadAllBytes(stash);
            program.On(() =>
            {
                Assert.Null(migration.CharactersHeld);
                Guard.CheckAtStart();
                Assert.True(Guard.IsHeld);
                Assert.Equal(Guard.HeldLine, migration.CharactersHeld);

                var result = Assert.Single(migration.MigrateCharacters([save]));
                Assert.Null(result.Report);
                Assert.Equal(Guard.HeldLine, result.Refused);
                Assert.Equal(Program.Other, result.Name);
                Assert.Equal(character, File.ReadAllBytes(save));
                Assert.Equal(shared, File.ReadAllBytes(stash));

                // And a vault: nothing is closed or migrated while the tables cannot be trusted.
                var vault = Assert.Single(migration.Migrate([("S13", false, "S13 Main")]));
                Assert.Null(vault.Report);
                Assert.Equal(Guard.HeldLine, vault.Refused);

                Guard.AcceptCommand.Execute(null);
                Assert.Null(migration.CharactersHeld);
            });
        }
        finally
        {
            Leave();
        }
    }

    /// <summary>A game folder whose archives will not open: as a patch half done, or a damaged disk, leaves one.</summary>
    private string DamagedFolder()
    {
        string folder = Path.Combine(program.Folder, "damaged-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "ProjectD2"));
        foreach (string archive in new[] { "d2data.mpq", Path.Combine("ProjectD2", "pd2data.mpq") })
            File.WriteAllBytes(Path.Combine(folder, archive), new byte[64]);
        return folder;
    }

    /// <summary>
    /// Tables that will not open. The start's check holds writes and says so; creating a
    /// character says so; neither lets it out to end the program. The next asking opens them anew.
    /// </summary>
    [CorpusFact]
    public void Tables_that_will_not_open_are_said_and_end_nothing()
    {
        if (!program.Ready())
            return;

        string folder = DamagedFolder();
        program.On(() =>
        {
            var settings = new EditorSettings { GameFolder = folder };
            var session = new CharacterSession(settings);
            try
            {
                Assert.True(session.IsConfigured);
                Assert.ThrowsAny<Exception>(() => session.EnsureGame());

                var guard = new PatchGuardService(session, settings);
                guard.CheckAtStart();
                Assert.True(guard.IsHeld);
                Assert.False(guard.CanAccept);
                Assert.False(guard.CanRepair);
                Assert.Equal("PD2 tables not read. Require restart.", guard.Reason);

                Assert.Null(session.TryGame());
                Assert.StartsWith("PD2 tables not read: ", session.Messages.Latest!.Text);

                session.Messages.Post("before");
                var creating = new CharacterCreationViewModel(session, null, program.Get<PlayRules>(), null) { NewName = "Nobody" };
                creating.CreateCommand.Execute(null);
                Assert.StartsWith("PD2 tables not read: ", session.Messages.Latest!.Text);
                Assert.False(File.Exists(Path.Combine(session.SaveFolder, "Nobody.d2s")));

                // The Extra page shown with no character open: empty, and said.
                session.Messages.Post("before");
                new VP_ViewModel_Extra_R(session, settings, program.Get<SharedStashSession>(), program.Get<PD2SPCompanion.Shell.Services.Generic.ISFXManager>(), program.Get<PlayRules>(),
                                         program.Get<PD2SPCompanion.Shell.Services.Vault.VaultService>()).Shown();
                Assert.StartsWith("PD2 tables not read: ", session.Messages.Latest!.Text);
            }
            finally
            {
                session.UseGameFolder("");
                var own = program.Get<EditorSettings>();
                own.Save();
                own.Apply();
            }
        });
    }

    /// <summary>Every item's every property: what the repair must keep.</summary>
    private static string Values(D2S save) =>
        string.Join("|", save.PlayerItemList.Items.SelectMany(item => item.SocketedItems.Prepend(item)).Where(item => !item.IsCompact)
            .Select(item => item.Code + ":" + string.Join(";", ItemStats.Every(item).Select(each => $"{each.Stat}={each.Value}/{each.Param}"))));

    /// <summary>
    /// A kept build in <paramref name="folder"/>: the tables the program runs on (the test's own, laid over the
    /// install) with <paramref name="stat"/> two bits wider, under its manifest.
    /// </summary>
    private void Widened(string folder, string stat)
    {
        string path = PD2SPCompanion.Core.GameFiles.GameData.ExcelPath("itemstatcost");
        if (Environment.GetEnvironmentVariable("PD2SPC_TABLES") is { Length: > 0 } tables)
        {
            foreach (string file in Directory.EnumerateFiles(tables, "*", SearchOption.AllDirectories))
            {
                string to = Path.Combine(folder, Path.GetRelativePath(tables, file));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(file, to, overwrite: true);
            }
        }

        var lines = program.On(() => System.Text.Encoding.Latin1.GetString(Session.Season!.Game.Read(path)!.Value.Data)).Split("\r\n");
        var head = lines[0].Split('\t').ToList();
        // Every season's width of it: the items read by the season before's columns as well as by this one's.
        int name = head.IndexOf("Stat");
        var widths = head.Select((column, at) => (column, at)).Where(each => each.column.StartsWith("Save Bits")).Select(each => each.at).ToList();
        for (int at = 1; at < lines.Length; at++)
        {
            var cells = lines[at].Split('\t');
            if (cells.Length > widths.Max() && cells[name] == stat)
            {
                foreach (int bits in widths.Where(bits => int.TryParse(cells[bits], out _)))
                    cells[bits] = (int.Parse(cells[bits]) + 2).ToString();
                lines[at] = string.Join('\t', cells);
            }
        }

        string target = Path.Combine(folder, path.Replace('\\', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(target, System.Text.Encoding.Latin1.GetBytes(string.Join("\r\n", lines)));
        File.Delete(Path.Combine(folder, PD2SPCompanion.Core.SeasonSnapshot.ManifestName));
        File.WriteAllText(Path.Combine(folder, PD2SPCompanion.Core.SeasonSnapshot.ManifestName), System.Text.Json.JsonSerializer.Serialize(
            new PD2SPCompanion.Core.SeasonSnapshot.Manifest("S13", "test-build-old", "", "S12", null, DateTimeOffset.UtcNow,
                [new PD2SPCompanion.Core.SeasonSnapshot.KeptFile(path, 1, "OLD")])));
    }

    [CorpusFact]
    public void The_build_changing_while_open_holds_writes_until_a_restart_with_no_Accept()
    {
        if (!program.Ready())
            return;

        try
        {
            program.On(() =>
            {
                // Started on a build already known: nothing held.
                File.WriteAllText(KnownFile, Build);
                Guard.CheckAtStart();
                Assert.False(Guard.IsHeld);

                // The window coming back looks again: the same build, nothing.
                Main.Sync!.Check();
                Assert.False(Guard.IsHeld);

                // PD2 patched under the running program.
                Environment.SetEnvironmentVariable(PatchGuard.FakeBuild, "test-build-c");
                Main.Sync.Check();

                Assert.True(Guard.IsHeld);
                Assert.False(Guard.CanAccept);
                Assert.Equal("PD2 changed while open. Require restart.", Guard.Reason);
                Assert.Equal("PD2 changed while open. Require restart. Writes held.", Session.Status);
                Assert.True(Main.Sync.IsWriteBlocked);

                // No Accept: the tables in memory are the old build's.
                Guard.AcceptCommand.Execute(null);
                Assert.True(Guard.IsHeld);
                Assert.Equal(Build, Known);
            });
        }
        finally
        {
            Leave();
        }
    }
}
