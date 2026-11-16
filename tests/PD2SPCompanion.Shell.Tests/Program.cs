using System.Collections.Concurrent;

using Avalonia;
using Avalonia.Headless;

using Microsoft.Extensions.DependencyInjection;

using D2SLib.Model.Save;

using PD2SPCompanion.Core.Characters;
using PD2SPCompanion.Core.Collection;
using PD2SPCompanion.Core.GameFiles;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests;

/// <summary>How the program is built for these tests: the program's own <see cref="App"/>, on no screen.</summary>
public static class Headless
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont();
}

/// <summary>
/// The program without its window, started once for every test here: its own services, built as it builds
/// them (App.SvcReg), the game's tables open, the vault open, and the pad's character loaded - on copies of
/// the UI pad's saves and vault in a folder of the run's own, with settings of the run's own. A test asks
/// things of the view models on the program's thread (<see cref="On"/>) and looks at what they say.
/// <para>
/// Two characters: PhySpearZon, the pad's, open; and Finder, the same save under another name, so that a
/// search has finds on a character that is not open. The game is pretended (<see cref="GameFlag"/>): the
/// real one, running or not, is nothing to these tests.
/// </para>
/// <para>
/// Needs what the Core's tests need: the installed game and the corpus (<see cref="TestPaths"/>). Without them
/// <see cref="Available"/> is false and a test is skipped (<see cref="CorpusFactAttribute"/>); <see cref="TestKitTests"/> is the one that fails and says so (PD2SPC_NO_GAME=1 where that is meant).
/// </para>
/// <para>
/// One call into the headless session holds the program for the whole run: the session makes an application of
/// its own for every call made of it, so the program lives inside one, which takes what the tests ask from a
/// queue and, between askings, leaves the thread to the program's own timers and what it posts to itself.
/// </para>
/// </summary>
public sealed class Program : IDisposable
{
    public static readonly string GameRoot = TestPaths.Game;

    public static readonly string Corpus = TestPaths.Corpus;

    private readonly HeadlessUnitTestSession? _session;
    private readonly ConcurrentQueue<Action> _asked = new();

    /// <summary>Something was asked: the program's loop takes it at once, not at its next look.</summary>
    private readonly SemaphoreSlim _wake = new(0);
    private readonly TaskCompletionSource _built = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task? _life;
    private volatile bool _over;

    /// <summary>The folder of this run: the pad's saves and vault copied, the program's data begun empty.</summary>
    /// <summary>
    /// The run's own folder - by its process and a fresh suffix: by the process alone, a run whose process number Windows
    /// gave again found an earlier run's folder left behind, and failed to start ("Finder already exists").
    /// </summary>
    public string Folder { get; } = Path.Combine(Path.GetTempPath(), $"pd2spc-shell-tests-{Environment.ProcessId}-{Guid.NewGuid():N}"[..40]);

    public bool Available { get; }

    public MainViewModel Main { get; private set; } = null!;

    /// <summary>The pad's character, open when a test begins.</summary>
    public const string Open = "PhySpearZon";

    /// <summary>The character that is not open: PhySpearZon's save, renamed.</summary>
    public const string Other = "Finder";

    public string SaveOf(string name) => Path.Combine(Folder, "saves", name + ".d2s");

    /// <summary>While this file is there the game counts as running (PD2SPC_FAKE_GAME); looked for every two seconds.</summary>
    public string GameFlag => Path.Combine(Folder, "game-running.flag");

    public Program()
    {
        string pad = Path.Combine(Corpus, "ui-pad", "pad");
        if (!GameLocation.Holds(GameRoot) || !Directory.Exists(pad))
            return;

        Copy(Path.Combine(pad, "saves"), Path.Combine(Folder, "saves"));
        Copy(Path.Combine(pad, "vtsmart"), Path.Combine(Folder, "vtsmart"));
        Directory.CreateDirectory(Path.Combine(Folder, "data"));

        // Where the program looks, before anything of it is made: never the player's own.
        Environment.SetEnvironmentVariable("PD2SPC_DATA", Path.Combine(Folder, "data"));
        Environment.SetEnvironmentVariable("PD2SPC_SAVES", Path.Combine(Folder, "saves"));
        Environment.SetEnvironmentVariable("PD2SPC_VAULTS", Path.Combine(Folder, "vtsmart"));
        Environment.SetEnvironmentVariable("PD2SPC_LOG", Path.Combine(Folder, "editor.log"));
        Environment.SetEnvironmentVariable("PD2SPC_FOLDS", "off");
        Environment.SetEnvironmentVariable("PD2SPC_VAULT_MODE", "off");
        Environment.SetEnvironmentVariable("PD2SPC_FAKE_GAME", GameFlag);

        _session = HeadlessUnitTestSession.StartNew(typeof(Headless));
        _life = _session.Dispatch(Live, CancellationToken.None);

        // Built, or failed to be: a failure is the first test's to show.
        Task.WaitAny(_built.Task, _life);
        if (_life.IsFaulted)
            _life.GetAwaiter().GetResult();
        _built.Task.GetAwaiter().GetResult();

        Available = true;
    }

    /// <summary>The program's life: built, then asked and left to itself in turn until the run is over.</summary>
    private async Task<int> Live()
    {
        try
        {
            var services = ((App)Application.Current!).Services;
            var character = services.GetRequiredService<CharacterSession>();

            // The run's settings are empty: the game's folder is said here, as a first run's setup says it.
            character.UseGameFolder(GameRoot);
            character.EnsureGame();
            services.GetRequiredService<VaultService>().Reopened();

            // The second character, made aside - a rename works on a folder of its own - before the home page
            // reads what characters there are.
            string aside = Path.Combine(Folder, "made");
            Directory.CreateDirectory(aside);
            File.Copy(SaveOf(Open), Path.Combine(aside, Open + ".d2s"));
            File.Copy(CharacterRename.Rename(Path.Combine(aside, Open + ".d2s"), Other), SaveOf(Other));

            // The pad as every test finds it, kept aside: put back after a test that changed what is saved.
            Copy(Path.Combine(Folder, "saves"), Path.Combine(Folder, "fresh", "saves"));
            Copy(Path.Combine(Folder, "vtsmart"), Path.Combine(Folder, "fresh", "vtsmart"));

            Main = services.GetRequiredService<MainViewModel>();
            character.Load(SaveOf(Open));
            _built.SetResult();
        }
        catch (Exception problem)
        {
            _built.SetException(problem);
            return 1;
        }

        while (!_over)
        {
            while (_asked.TryDequeue(out var work))
                work();

            // Until something is asked, or a few milliseconds: the thread is the program's own meanwhile.
            await _wake.WaitAsync(5);
        }

        try
        {
            ((App)Application.Current!).Services.Dispose();
        }
        catch (Exception)
        {
            // The run is over either way.
        }

        return 0;
    }

    /// <summary>
    /// The program as every test finds it: the game not running, the pad's character open on the Items page
    /// with the inventory and Edit, no search standing, nothing typed in the search box. False where the
    /// program could not be built (<see cref="Available"/>): the test passes, having looked at nothing.
    /// </summary>
    /// <param name="changes">
    /// The test changes what is saved - a character, the shared stash, the vault: the saves and the vault are
    /// put back as they were before the next test begins.
    /// </param>
    public bool Ready(bool changes = false)
    {
        if (!Available)
            return false;

        if (File.Exists(GameFlag) || On(() => Main.Sync?.IsGameRunning == true))
        {
            File.Delete(GameFlag);
            Until(() => Main.Sync?.IsGameRunning != true, 10000);
        }

        if (_changed)
            PutBack();
        _changed = changes;

        if (On(() => !Main.Session.IsLoaded || Main.Session.CharacterName != Open))
        {
            On(() => Main.Home.SwitchTo(SaveOf(Open)));
            if (!Until(() => Main.Session.IsLoaded && Main.Session.CharacterName == Open, 15000))
                throw new InvalidOperationException($"{Open} could not be opened again.");

            // What the load posted done first: a character the game closed is put back on its bookmarks a moment
            // after it opens (MainViewModel.Resume), which would undo the bookmark the next test turns to.
            bool posted = false;
            On(() => Avalonia.Threading.Dispatcher.UIThread.Post(() => posted = true, Avalonia.Threading.DispatcherPriority.Background));
            if (!Until(() => posted))
                throw new InvalidOperationException("What the load posted never ran.");
        }

        On(() =>
        {
            PD2SPCompanion.Shell.Services.Generic.ItemClipboard.Shared.Clear();
            Main.SearchWindow!.End();
            Main.QuickSearch!.Text = "";
            Main.QuickSearch.Targets = null;
            Main.ShowPageByKey("Inventory_L");
            Main.ShowPageByKey("Inventory_R");
            Main.Inventory!.InventoryLock = false;
        });
        return true;
    }

    private bool _changed;

    /// <summary>The saves and the vault as they were when the program was built; the character is opened again by <see cref="Ready"/>.</summary>
    private void PutBack()
    {
        On(() =>
        {
            Get<AutoSave>().Flush();
            if (Main.Session.IsLoaded)
                Main.Session.Close();
            Get<VaultService>().Close();

            foreach (string part in new[] { "saves", "vtsmart" })
            {
                Directory.Delete(Path.Combine(Folder, part), recursive: true);
                Copy(Path.Combine(Folder, "fresh", part), Path.Combine(Folder, part));
            }

            Get<VaultService>().Reopened();

            // The save folder watched again: its watcher did not outlive the folder it watched.
            Get<CharacterSession>().UseGameFolder(GameRoot);
        });
    }

    /// <summary>One of the program's own services or pages. Asked on the program's thread.</summary>
    public T Get<T>() where T : notnull => ((App)Application.Current!).Services.GetRequiredService<T>();

    private uint _made = 0x7E570000;

    /// <summary>
    /// An item made as the Forge makes it - the find of that name for what is typed, stamped in the
    /// character's own era - and handed to the character where a forged item lands: the page a pane shows,
    /// then the first room. Asked on the program's thread. Null where it found no room.
    /// </summary>
    public Item? Give(string typed, string name)
    {
        var search = new ForgeSearch();
        search.Load();
        search.Pick(search.Find(typed, 40, out _).First(find => find.Name == name));

        Main.Session.UseMyLayout();
        var item = ForgeSearch.Make(search.What!, Main.Session.ItemEra!);
        item.Id = ++_made;
        return Get<ItemLanding>().Receive(item, "no room", out _) is null ? item : null;
    }

    /// <summary>Something done on the program's own thread, as a click or a key is.</summary>
    public void On(Action work) => On(() => { work(); return true; });

    /// <summary>Something asked on the program's own thread.</summary>
    public T On<T>(Func<T> ask)
    {
        var answer = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _asked.Enqueue(() =>
        {
            try
            {
                answer.SetResult(ask());
            }
            catch (Exception problem)
            {
                answer.SetException(problem);
            }
        });

        _wake.Release();

        if (!answer.Task.Wait(TimeSpan.FromSeconds(60)))
            throw new TimeoutException("The program did not answer in a minute.");

        return answer.Task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Waited for: asked every few milliseconds until it is so, the program's thread free in between for its
    /// timers and what it posted to itself. False where it never was.
    /// </summary>
    public bool Until(Func<bool> so, int milliseconds = 5000)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < milliseconds)
        {
            if (On(so))
                return true;

            Thread.Sleep(15);
        }

        return On(so);
    }

    public void Dispose()
    {
        _over = true;
        try
        {
            _life?.Wait(TimeSpan.FromSeconds(10));
            _session?.Dispose();
        }
        catch (Exception)
        {
            // The run is over either way.
        }

        try
        {
            if (Directory.Exists(Folder))
                Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
            // A file still held: the temp folder is the system's to clear.
        }
    }

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string folder in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, folder)));
        foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: true);
    }
}

/// <summary>The tests that share the one program: they run one after another, each leaving it as it found it.</summary>
[CollectionDefinition(Name)]
public sealed class WithTheProgram : ICollectionFixture<Program>
{
    public const string Name = "the program";
}
