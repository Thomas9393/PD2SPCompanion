using PD2SPCompanion.Core.Testbed;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Experimental;

/// <summary>
/// APPLY on the Experimental page, the one write into the game's folder - here into a folder of the run's own. The
/// session is pointed at it (UseGameFolder) for the test, holding a copy of PD2's table archive and nothing else, and
/// every test first checks that this is where the page will write and afterwards that the game's real folder is as
/// it was. A session left pointing at the scratch folder is harmless, which is the safe way round.
/// <para>
/// Not asked: a table that does not round-trip, the "Refused" branch. Every table the page writes round-trips, and
/// making one that does not would mean building an MPQ archive by hand.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class ApplyTests(Program program)
{
    private VP_ViewModel_Experimental_L Page => program.Get<VP_ViewModel_Experimental_L>();
    private CharacterSession Session => program.Main.Session;
    private ConfirmService Confirm => program.Get<ConfirmService>();

    /// <summary>The page on a scratch game folder, nothing chosen; what it was and the real folder's state checked and put back.</summary>
    private sealed class Scratch : IDisposable
    {
        private readonly ApplyTests _test;
        private readonly string _realState;
        private readonly (int Density, int Packs, int Scope, bool Every, bool Cow) _choices;
        private readonly Dictionary<string, int> _boosts;

        public string Root { get; }
        public string Excel => DensityInstaller.FolderIn(Root);
        public string Marker => DensityInstaller.MarkerIn(Root);

        public Scratch(ApplyTests test)
        {
            _test = test;
            var program = test.Program;
            _realState = RealFolderState();

            Root = Path.Combine(program.Folder, "scratch-game-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(Root, "ProjectD2"));
            File.Copy(Path.Combine(Program.GameRoot, "ProjectD2", "pd2data.mpq"), Path.Combine(Root, "ProjectD2", "pd2data.mpq"));

            // What the page read from the real folder's marker, the drop panel's multiples with it.
            _boosts = program.On(() => new Dictionary<string, int>(test.Page.DropPanel.Boosts));
            _choices = program.On(() =>
            {
                var page = test.Page;
                var kept = (page.DensityIndex, page.PackIndex, page.ScopeIndex, page.EveryKill, page.CowPortal);
                page.DropPanel.Take(null);
                test.Session.UseGameFolder(Root);
                Assert.StartsWith(program.Folder, test.Session.GameRoot, StringComparison.OrdinalIgnoreCase);
                Choose(page, density: 0, packs: 0, everyKill: false, cow: false);
                page.ScopeIndex = 0;
                page.IsInstalled = DensityInstaller.Current(test.Session.GameRoot).Installed;
                Assert.False(page.IsInstalled);
                return kept;
            });
        }

        public void Dispose()
        {
            var program = _test.Program;
            program.On(() =>
            {
                var page = _test.Page;
                if (_test.Confirm.IsAsking)
                    _test.Confirm.RejectCommand.Execute(null);

                _test.Session.UseGameFolder(Program.GameRoot);
                page.DropPanel.Take(_boosts);
                Choose(page, _choices.Density, _choices.Packs, _choices.Every, _choices.Cow);
                page.ScopeIndex = _choices.Scope;
                page.IsInstalled = DensityInstaller.Current(Program.GameRoot).Installed;
            });

            Assert.Equal(_realState, RealFolderState());
        }
    }

    private Program Program => program;

    /// <summary>The real folder's marker and loose tables, by name, size and time: no test here may change a thing of it.</summary>
    private static string RealFolderState()
    {
        string marker = DensityInstaller.MarkerIn(Program.GameRoot), excel = DensityInstaller.FolderIn(Program.GameRoot);
        var files = (File.Exists(marker) ? [marker] : Array.Empty<string>())
            .Concat(Directory.Exists(excel) ? Directory.GetFiles(excel) : []);
        return string.Join("\n", files.Order(StringComparer.Ordinal).Select(file =>
        {
            var info = new FileInfo(file);
            return $"{file} {info.Length} {info.LastWriteTimeUtc:O}";
        }));
    }

    private static void Choose(VP_ViewModel_Experimental_L page, int density, int packs, bool everyKill, bool cow)
    {
        page.DensityIndex = density;
        page.PackIndex = packs;
        page.EveryKill = everyKill;
        page.CowPortal = cow;
    }

    /// <summary>APPLY pressed, the page first checked to be on the scratch folder.</summary>
    private void Apply(Scratch scratch)
    {
        Assert.Equal(scratch.Root, Session.GameRoot);
        Page.ApplyCommand.Execute(null);
    }

    private static string[] Tables(Scratch scratch) =>
        [.. Directory.GetFiles(scratch.Excel).Select(Path.GetFileName).Order(StringComparer.Ordinal)!];

    [CorpusFact]
    public void Apply_writes_each_table_chosen_records_it_and_says_so()
    {
        if (!program.Ready())
            return;

        using var scratch = new Scratch(this);
        program.On(() =>
        {
            Choose(Page, density: 1, packs: 1, everyKill: true, cow: true);
            Assert.True(Page.AnyChosen);

            Apply(scratch);

            Assert.Equal(["cubemain.txt", "levels.txt", "monstats.txt", "treasureclassex.txt"], Tables(scratch));
            Assert.True(Page.IsInstalled);
            Assert.StartsWith("Wrote ", Page.Status);
            Assert.Contains("levels.txt at 2x", Page.Status);
            Assert.Contains("monstats.txt with packs at 2x", Page.Status);
            Assert.Contains("cubemain.txt with the cow recipe", Page.Status);

            // What the marker records is what is there, and what was chosen reads back from it.
            var state = DensityInstaller.Current(scratch.Root);
            Assert.True(state.Installed);
            Assert.Equal((2d, 2, true, true), (state.Multiplier, state.Packs, state.CowPortal, state.Drops!.EveryKill));
            Assert.Equal(4, state.Files.Count);
            Assert.All(state.Files, file => Assert.True(File.Exists(file), file));
            Assert.False(Page.IsStale);
        });
    }

    /// <summary>
    /// The page's last look says nothing is installed, and the folder holds an install. APPLY
    /// goes by the folder: what the marker lists comes off, so no file stays in force with no marker naming it.
    /// </summary>
    [CorpusFact]
    public void Apply_on_a_stale_no_install_still_takes_off_what_the_folder_holds()
    {
        if (!program.Ready())
            return;

        using var scratch = new Scratch(this);
        program.On(() =>
        {
            Choose(Page, density: 1, packs: 1, everyKill: false, cow: false);
            Apply(scratch);
            Assert.Equal(["levels.txt", "monstats.txt"], Tables(scratch));

            Page.IsInstalled = false;
            Page.PackIndex = 0;
            Apply(scratch);

            Assert.Equal(["levels.txt"], Tables(scratch));
            var state = DensityInstaller.Current(scratch.Root);
            Assert.Single(state.Files);
        });
    }

    /// <summary>
    /// A page built with no game folder - a first run's - reads the folder when it is settled,
    /// with what it had dimmed for lack of one. A file of the save folder changing is no reason to read it again: a look
    /// puts the page's choices back to what is installed.
    /// </summary>
    [CorpusFact]
    public void A_page_built_with_no_folder_reads_the_install_when_one_is_settled()
    {
        if (!program.Ready())
            return;

        string folder = Path.Combine(program.Folder, "settled-game-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "ProjectD2"));
        foreach (string archive in new[] { "d2data.mpq", Path.Combine("ProjectD2", "pd2data.mpq") })
            File.WriteAllBytes(Path.Combine(folder, archive), new byte[64]);
        Directory.CreateDirectory(Path.GetDirectoryName(DensityInstaller.MarkerIn(folder))!);
        Directory.CreateDirectory(Path.GetDirectoryName(DensityInstaller.GameIn(folder))!);
        File.WriteAllText(DensityInstaller.MarkerIn(folder), "{ \"Multiplier\": 3, \"Files\": [] }");
        File.WriteAllBytes(DensityInstaller.GameIn(folder), [0]);

        program.On(() =>
        {
            var settings = new PD2SPCompanion.Shell.Services.Settings.EditorSettings();
            var session = new CharacterSession(settings);
            try
            {
                var page = new VP_ViewModel_Experimental_L(session, null!, program.Get<PD2SPCompanion.Shell.Services.Settings.PlayRules>(), new ConfirmService());
                Assert.False(page.IsInstalled);
                Assert.False(page.HasGame);

                var changes = new List<string?>();
                page.PropertyChanged += (_, changed) => changes.Add(changed.PropertyName);
                session.UseGameFolder(folder);

                Assert.True(page.IsInstalled);
                Assert.True(page.HasGame);
                Assert.True(page.ShowsOnlineNotice);
                Assert.Contains(nameof(page.ShortcutHint), changes);
                Assert.Contains(nameof(page.RemoveHint), changes);

                // A choice made since, and a save in the folder: the choice stands.
                page.DensityIndex = 1;
                session.UseGameFolder(folder);
                Assert.Equal(1, page.DensityIndex);
            }
            finally
            {
                session.UseGameFolder("");
                var own = program.Get<PD2SPCompanion.Shell.Services.Settings.EditorSettings>();
                own.Save();
                own.Apply();
            }
        });
    }

    /// <summary>
    /// The game, started with -direct -txt, compiles its tables into this folder. With it
    /// running APPLY and REMOVE are dimmed with why, and pressed they write and delete nothing.
    /// </summary>
    [CorpusFact]
    public void While_the_game_runs_Apply_and_Remove_are_refused_and_touch_nothing()
    {
        if (!program.Ready())
            return;

        using var scratch = new Scratch(this);
        program.On(() =>
        {
            Choose(Page, density: 1, packs: 0, everyKill: false, cow: false);
            Apply(scratch);
            Assert.True(Page.CanApply);
            Assert.True(Page.CanRemove);
        });

        string[] before = Tables(scratch);
        string marker = File.ReadAllText(scratch.Marker);
        try
        {
            File.WriteAllText(program.GameFlag, "running");
            Assert.True(program.Until(() => program.Main.Sync?.IsGameRunning == true, 10000), "the game was not seen");

            program.On(() =>
            {
                Assert.False(Page.CanApply);
                Assert.False(Page.CanRemove);
                Assert.Contains("Require game closed to apply.", Page.ApplyHint);
                Assert.Contains("Require game closed to remove.", Page.RemoveHint);

                Choose(Page, density: 2, packs: 1, everyKill: false, cow: false);
                Apply(scratch);
                Assert.Equal("Require game closed to apply.", Session.Status);

                Page.RemoveCommand.Execute(null);
                Assert.False(Confirm.IsAsking);
                Assert.Equal("Require game closed to remove.", Session.Status);
            });

            Assert.Equal(before, Tables(scratch));
            Assert.Equal(marker, File.ReadAllText(scratch.Marker));
        }
        finally
        {
            File.Delete(program.GameFlag);
            program.Until(() => program.Main.Sync?.IsGameRunning != true, 10000);
        }

        // Asked while the game was closed, started before the answer: still nothing.
        program.On(() =>
        {
            Assert.True(Page.CanRemove);
            Page.RemoveCommand.Execute(null);
            Assert.True(Confirm.IsAsking);
        });
        File.WriteAllText(program.GameFlag, "running");
        try
        {
            program.On(() => Confirm.AcceptCommand.Execute(null));
            Assert.Equal(before, Tables(scratch));
        }
        finally
        {
            File.Delete(program.GameFlag);
            program.Until(() => program.Main.Sync?.IsGameRunning != true, 10000);
        }
    }

    [CorpusFact]
    public void A_choice_turned_off_takes_its_file_with_it()
    {
        if (!program.Ready())
            return;

        using var scratch = new Scratch(this);
        program.On(() =>
        {
            Choose(Page, density: 1, packs: 0, everyKill: false, cow: true);
            Apply(scratch);
            Assert.Equal(["cubemain.txt", "levels.txt"], Tables(scratch));

            Page.CowPortal = false;
            Apply(scratch);
            Assert.Equal(["levels.txt"], Tables(scratch));
            Assert.False(DensityInstaller.Current(scratch.Root).CowPortal);
        });
    }

    [CorpusFact]
    public void A_table_not_made_here_refuses_and_leaves_the_install_that_works()
    {
        if (!program.Ready())
            return;

        using var scratch = new Scratch(this);
        program.On(() =>
        {
            Choose(Page, density: 1, packs: 0, everyKill: false, cow: false);
            Apply(scratch);
            string written = File.ReadAllText(Path.Combine(scratch.Excel, "levels.txt"));

            // Another's table at the place of one the next APPLY would write.
            string foreign = Path.Combine(scratch.Excel, "monstats.txt");
            File.WriteAllText(foreign, "not ours");

            Page.PackIndex = 1;
            Apply(scratch);

            Assert.Equal($"{foreign} exists, not made here.", Page.Status);
            Assert.Equal("not ours", File.ReadAllText(foreign));
            Assert.Equal(written, File.ReadAllText(Path.Combine(scratch.Excel, "levels.txt")));
            Assert.True(DensityInstaller.Current(scratch.Root).Installed);
            Assert.Equal(1, DensityInstaller.Current(scratch.Root).Packs);
        });
    }

    [CorpusFact]
    public void Nothing_chosen_with_something_installed_asks_and_yes_takes_it_out()
    {
        if (!program.Ready())
            return;

        using var scratch = new Scratch(this);
        program.On(() =>
        {
            Choose(Page, density: 1, packs: 0, everyKill: false, cow: false);
            Apply(scratch);
            Assert.True(Page.IsInstalled);

            Choose(Page, density: 0, packs: 0, everyKill: false, cow: false);
            Assert.False(Page.AnyChosen);
            Assert.True(Page.CanApply);
            Assert.StartsWith("Nothing chosen", Page.ApplyHint);

            Apply(scratch);
            Assert.True(Confirm.IsAsking);
            Assert.Equal("Remove mod files", Confirm.Title);
            Assert.True(File.Exists(scratch.Marker));

            Confirm.RejectCommand.Execute(null);
            Assert.True(File.Exists(scratch.Marker));
            Assert.True(Page.IsInstalled);

            Apply(scratch);
            Confirm.AcceptCommand.Execute(null);

            Assert.Equal("Mod files removed.", Page.Status);
            Assert.False(File.Exists(scratch.Marker));
            Assert.False(Directory.Exists(scratch.Excel), "the folder made for the tables stayed");
            Assert.False(Page.IsInstalled);

            // Nothing to write and nothing to take out.
            Assert.False(Page.CanApply);
            string said = Page.Status;
            Apply(scratch);
            Assert.False(Confirm.IsAsking);
            Assert.Equal(said, Page.Status);
        });
    }

    [CorpusFact]
    public void A_folder_with_no_archives_is_said_and_nothing_is_written()
    {
        if (!program.Ready())
            return;

        using var scratch = new Scratch(this);
        program.On(() =>
        {
            File.Delete(Path.Combine(scratch.Root, "ProjectD2", "pd2data.mpq"));
            Choose(Page, density: 1, packs: 0, everyKill: false, cow: false);

            Apply(scratch);

            Assert.Contains("No Diablo II archives", Page.Status);
            Assert.False(File.Exists(scratch.Marker));
            Assert.False(Page.IsInstalled);
        });
    }
}
