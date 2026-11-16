using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using PD2SPCompanion.Shell.Helper.ViewLocator;
using PD2SPCompanion.Shell.ViewModels;
using PD2SPCompanion.Shell.Views;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace PD2SPCompanion.Shell;

public partial class App : Application
{
    #region Fields and Members
    //Splash screen
    private SplashWindow?   _splashWindow;
    private MainWindow?     _mainWindow;
    private MainViewModel?  _mainViewModel;
    #endregion

    /// <summary>
    /// Every service, page and view, each built once (ServiceRegistration). A plain provider: the generic
    /// host it was built from also set up configuration files watched for changes, four loggers and the
    /// hosting machinery, none of which the editor uses.
    /// </summary>
    public ServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        // Where a tooltip opens, said once (the UI language's rule I): under its control, six units
        // off. It was written on some two hundred controls and left off eighty, whose tooltips then followed
        // the pointer. A control that wants another place still says so itself (an item's tile, the side bar).
        // On the kinds of control under Control, each in turn: the two properties are Control's own, and a
        // default cannot be said twice for one type ("Metadata is already set").
        void Under<T>() where T : Avalonia.Controls.Control
        {
            Avalonia.Controls.ToolTip.PlacementProperty.OverrideDefaultValue<T>(Avalonia.Controls.PlacementMode.Bottom);
            Avalonia.Controls.ToolTip.VerticalOffsetProperty.OverrideDefaultValue<T>(6);
        }
        Under<Avalonia.Controls.Primitives.TemplatedControl>();
        Under<Avalonia.Controls.Panel>();
        Under<Avalonia.Controls.Decorator>();
        Under<Avalonia.Controls.TextBlock>();
        Under<Avalonia.Controls.Shapes.Shape>();
        Under<Avalonia.Controls.Image>();
        Under<Avalonia.Controls.Presenters.ContentPresenter>();

        // Every popup - a tooltip, a drop-down, a flyout, a menu - is drawn at the scale of what it opens on:
        // the window's content is under one scale (Services.Settings.UiScale), and a popup is a window of its
        // own that would otherwise open at Windows' scale, larger or smaller than everything round it.
        // Turned on as a popup is given what it shows, which every one is before it opens - a tooltip's and a
        // flyout's are made in code, out of any style's reach, and the property's default cannot be overridden
        // on the type that owns it.
        Avalonia.Controls.Primitives.Popup.ChildProperty.Changed.AddClassHandler<Avalonia.Controls.Primitives.Popup>(
            (popup, _) => popup.InheritsTransform = true);

        CustomControls.Base.Input.WordedButtons.Listen();

        // Before the first XAML: every {L '...'} and L("...") reads the language in use as it is built.
        // Unless the program's first lines did, for the worker reading the game's tables (GameAhead).
        if (!PD2SPCompanion.Core.Localization.Language.InUse)
            PD2SPCompanion.Core.Localization.Language.Use(global::PD2SPCompanion.Shell.Services.Settings.EditorSettings.StoredLanguage());
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        SvcReg(services);
        // In a Debug build every registration is checked as the provider is built, so one that cannot be
        // made fails at the start rather than on the page that first asks for it.
        Services = services.BuildServiceProvider(new ServiceProviderOptions
        {
#if DEBUG
            ValidateOnBuild = true,
#endif
        });

        // The Avalonia 11 templates removed Avalonia's own data validator here so it did
        // not duplicate CommunityToolkit's. BindingPlugins is internal in Avalonia 12 and
        // the current templates no longer do this, so the line is gone.

        this.DataTemplates.Add(Services.GetRequiredService<MainViewPage_ViewLocator>());

        // The right page's own, for the views it keeps a second instance of - see the locator.
        this.Resources["RightPageLocator"] = new MainViewPage_ViewLocator(Services, MainViewPage_ViewLocator.RightSide);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The titles' font, before the first window is built: the player's own from the Fonts folder, if any.
            PD2SPCompanion.Shell.Services.Settings.TitleFont.Use(this);
            PD2SPCompanion.Shell.Services.Settings.TitleFont.LogBold(this);

            _splashWindow = new SplashWindow();
            desktop.MainWindow = _splashWindow;

            // The logo the pages wear, once the tables are open: behind the splash (WarmUp), or after a first
            // run's setup settles the folder.
            PD2SPCompanion.Core.GameFiles.InstalledTables.Changed += () => Dispatcher.UIThread.Post(() =>
                _ = PD2SPCompanion.Shell.Imaging.FrontEndArt.Provide(
                    Services.GetRequiredService<PD2SPCompanion.Shell.Services.Character.CharacterSession>().EnsureGame()));

            // On the way out every service that holds something is let go of: the vaults' databases
            // closed, the sound devices released - before, the process ending was all that did it.
            desktop.Exit += (_, _) =>
            {
                try
                {
                    Services.Dispose();
                }
                catch (Exception problem)
                {
                    PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
                }
            };

            Dispatcher.UIThread.Post(() => ApplicationStart_Complete(), DispatcherPriority.Background);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Start-up, step by step behind the splash, each said on it before it runs (SplashWindow.Say) and timed
    /// into the editor's log. A step that fails is logged and the rest go on: what failed fails again where
    /// it is used, and is said there.
    /// </summary>
    private async void ApplicationStart_Complete()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The tables a worker is filling (GameAhead) are not read half filled: the pages are built once it is done.
            var waited = System.Diagnostics.Stopwatch.StartNew();
            await PD2SPCompanion.Shell.Services.Character.GameAhead.Done;
            PD2SPCompanion.Core.Diagnostics.Log.Info("Start", $"Waiting for the game files: {waited.ElapsedMilliseconds} ms");

            await Step(L("Building the pages"), () =>
            {
                _mainWindow = Services.GetRequiredService<MainWindow>();
                _mainViewModel = Services.GetRequiredService<MainViewModel>();
                _mainWindow.DataContext = _mainViewModel;
                BuildThePages();
            });
            await WarmUp();

            // Without its window there is no editor: failed loudly, to the crash log, not left on the splash.
            if (_mainWindow is null)
                throw new InvalidOperationException("The main window could not be built; the editor log says why.");
            desktop.MainWindow = _mainWindow;

            // Shown, and out of sight until every page is laid out (Win11Frame.Cloak; MainView.PrepareAllPages):
            // the splash stays the while, and says so. Shown first - the pages are laid out in the room the
            // window really has, maximized where it comes back so.
            bool cloaked = PD2SPCompanion.Shell.Helper.Win11Frame.Cloak(_mainWindow, true);
            _mainWindow.Show();
            try
            {
                if (System.Linq.Enumerable.FirstOrDefault(System.Linq.Enumerable.OfType<PD2SPCompanion.Shell.Views.MainView>(
                        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(_mainWindow))) is { } view)
                    await Step(L("Laying out the pages"), view.PrepareAllPages);
            }
            finally
            {
                // Faded in, under the splash fading out, rather than there at once (Win11Frame.Reveal).
                if (cloaked && !PD2SPCompanion.Shell.Helper.Win11Frame.Reveal(_mainWindow))
                    PD2SPCompanion.Shell.Helper.Win11Frame.Cloak(_mainWindow, false);
            }

            // What the whole start allocated and how often the collector ran, beside the steps' times: some 600 MB
            // and a seventh of a start (measured).
            using (var self = System.Diagnostics.Process.GetCurrentProcess())
            {
                const long Megabyte = 1024 * 1024;
                long made = GC.GetTotalAllocatedBytes() / Megabyte, heap = GC.GetTotalMemory(false) / Megabyte;
                long set = self.WorkingSet64 / Megabyte, most = self.PeakWorkingSet64 / Megabyte;
                double stops = GC.GetTotalPauseDuration().TotalMilliseconds;
                PD2SPCompanion.Core.Diagnostics.Log.Info("Start", $"Allocated {made} MB; collections {GC.CollectionCount(0)} / {GC.CollectionCount(1)} / {GC.CollectionCount(2)}; collector's stops {stops:0} ms; heap {heap} MB; working set {set} MB, at most {most} MB");
            }

            // Finally, close the splash screen
            _splashWindow?.Close();
            if (cloaked)
                _mainWindow.Activate();

            // The saves checked against the installed PD2, and a new season's question, once the window is up.
            _mainViewModel?.PostStartChecks();

            // The play mode the open vault keeps, held against the editor's (VaultPlayMode): made here, so it
            // hears every vault opened and every mode chosen from now on, and asked of the vault open already.
            // After the new-season question, which it waits behind.
            Dispatcher.UIThread.Post(
                () => Services.GetRequiredService<PD2SPCompanion.Shell.Services.Vault.VaultPlayMode>().Check(),
                DispatcherPriority.Background);

            // Started at a newer version than last time: "What's new", once (WhatsNewService). After the
            // new-season question, which it waits behind.
            Dispatcher.UIThread.Post(
                () => Services.GetRequiredService<PD2SPCompanion.Shell.Services.Settings.WhatsNewService>().CheckAtStart(),
                DispatcherPriority.Background);

            // The other characters' search indexes, one at a time while the window is idle: the first search of
            // a session wrote them all at once (QuickSearchViewModel.PrepareOthers).
            Dispatcher.UIThread.Post(() => _ = _mainViewModel?.QuickSearch?.PrepareOthers(), DispatcherPriority.Background);

#if DEBUG
            WatchForPadReset();
#endif
        }
    }

#if DEBUG
    private DispatcherTimer? _padReset;

    /// <summary>
    /// For the UI cases, which run on copies (tests/ui/run.ps1), and in a Debug build only - a published
    /// program has none of this. <c>PD2SPC_TEST_RESET</c> names a file, and while that file is there the open
    /// character is closed, as its Close button closes it, and the vault let go of, so the runner can put the
    /// pad's saves and vault back as they were under a program that keeps running: a start of the program is
    /// some five seconds, and half the cases asked for one only to have the pad as it was handed in.
    /// "&lt;file&gt;.closed" tells the runner the files are free; when the first file is taken away the vault
    /// is opened again, the save folder watched again, and the second removed.
    /// <para>
    /// Nothing is discarded for it: where closing would have to ask - something not saved - it is not closed,
    /// "&lt;file&gt;.refused" says so, and the runner starts the program anew as it did before.
    /// </para>
    /// </summary>
    private void WatchForPadReset()
    {
        if (Environment.GetEnvironmentVariable("PD2SPC_TEST_RESET") is not { Length: > 0 } asked)
            return;

        string closed = asked + ".closed", refused = asked + ".refused";
        bool held = false;

        _padReset = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) =>
        {
            try
            {
                var session = Services.GetRequiredService<PD2SPCompanion.Shell.Services.Character.CharacterSession>();
                var vaults = Services.GetRequiredService<PD2SPCompanion.Shell.Services.Vault.VaultService>();

                if (!held && System.IO.File.Exists(asked) && !System.IO.File.Exists(refused))
                {
                    if (session.IsLoaded && _mainViewModel is { } main)
                    {
                        main.Home.CloseCommand.Execute(null);

                        // Asked about: the question is answered no, and the reset is not made.
                        if (session.IsLoaded)
                        {
                            if (main.Confirm is { IsAsking: true } question)
                                question.RejectCommand.Execute(null);

                            System.IO.File.WriteAllText(refused, "");
                            return;
                        }
                    }

                    vaults.Close();
                    held = true;
                    System.IO.File.WriteAllText(closed, "");
                }
                else if (held && !System.IO.File.Exists(asked))
                {
                    vaults.Reopened();
                    session.UseGameFolder(session.GameRoot);
                    held = false;
                    System.IO.File.Delete(closed);
                }
            }
            catch (Exception problem)
            {
                // Said in the log; the runner, left without an answer, starts the program anew.
                PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
            }
        });
        _padReset.Start();
    }
#endif

    /// <summary>
    /// The game's tables, and what the grail works out from them on its first visit, behind the
    /// splash too - the loading screen is there to make room for this.
    /// Opened after the window showed, the tables froze a window that looked ready; the grail's
    /// runeword list was 130 ms of its first open (measured with tests/perf). And the vault
    /// of the mode played last: the first character loaded opened it, 80 ms of the load, and a
    /// character of that mode now finds it open (VaultService.UseMode).
    /// </summary>
    private async Task WarmUp()
    {
        PD2SPCompanion.Core.GameFiles.GameData? game = null;
        await Step(L("Reading the game files"), () =>
            game = Services.GetRequiredService<PD2SPCompanion.Shell.Services.Character.CharacterSession>().EnsureGame());
        // No folder yet - a first run, before its setup: the rest waits for it (InstalledTables.Changed).
        if (game is null)
            return;

        await Step(L("Preparing the game's art"), () => PD2SPCompanion.Shell.Imaging.FrontEndArt.Provide(game));
        await Step(L("Preparing the compendium"), () =>
        {
            PD2SPCompanion.Core.Collection.GrailCatalog.Uniques();
            PD2SPCompanion.Core.Collection.GrailCatalog.Sets();
            PD2SPCompanion.Core.Collection.GrailCatalog.RunewordEntries();

            // Which properties are written together (AffixRanges.NeverApart, worked out from the tables once):
            // it was 90 ms of the first item selected in a session - that select was twice any later one
            // (121 ms against 40 - 60, measured), and it stood in a character opened from a find.
            PD2SPCompanion.Core.Items.AffixRanges.NeverApart();
        });
        await Step(L("Opening the vault"), () =>
        {
            // The click, out of the game's sound archive, so the first press does not read it.
            Services.GetRequiredService<PD2SPCompanion.Shell.Services.Generic.ISFXManager>().Prepare();
            Services.GetRequiredService<PD2SPCompanion.Shell.Services.Vault.VaultService>().Reopened();
        });
    }

    /// <summary>
    /// One step of start-up: said on the splash and drawn, then run. Timed from the saying: work posted
    /// meanwhile runs while its line is up - the art, built as the tables open (InstalledTables.Changed), is
    /// the art step's though the tables step set it off.
    /// </summary>
    private Task Step(string what, Action work) =>
        Step(what, () =>
        {
            work();
            return Task.CompletedTask;
        });

    /// <summary>A step whose work goes on off the UI thread (the art's cutting): awaited, the splash kept moving.</summary>
    private async Task Step(string what, Func<Task> work)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var paused = GC.GetTotalPauseDuration();
        long allocated = GC.GetTotalAllocatedBytes();
        if (_splashWindow is { } splash)
            await splash.Say(what);

        try
        {
            await work();
        }
        catch (Exception problem)
        {
            PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
        }
        // With what the step allocated - every thread's - and the collector's stops in it: a start-up allocates
        // what the tables and the pages are made of, and the collector's stops were an eighth of one.
        long made = (GC.GetTotalAllocatedBytes() - allocated) / (1024 * 1024);
        double stops = (GC.GetTotalPauseDuration() - paused).TotalMilliseconds;
        PD2SPCompanion.Core.Diagnostics.Log.Info("Start", $"{what}: {timer.ElapsedMilliseconds} ms (allocated {made} MB; collector {stops:0})");
    }

    /// <summary>
    /// Builds every page's view now rather than on the first press that shows it.
    /// <para>
    /// The views are singletons the view locator resolves out of the container, so each one
    /// is built the first time it is navigated to - inside the cross-fade that is meant to
    /// be showing it. The inventory pages are 3400 lines of XAML between them, several
    /// times any other page, and an Avalonia animation runs on wall-clock time: whatever
    /// the build costs comes straight out of the 300ms fade, which is why that page arrives
    /// without one.
    /// </para>
    /// <para>
    /// Here the same work happens behind the splash screen, where a stall is what a splash
    /// screen is for.
    /// </para>
    /// </summary>
    private void BuildThePages()
    {
        var pages = new[]
        {
            typeof(VP_View_Default_L), typeof(VP_View_Default_R),
            typeof(VP_View_Char_L), typeof(VP_View_Char_R),
            typeof(VP_View_Inv_L),
            typeof(VP_View_Vault_L), typeof(VP_View_Vault_R),
            typeof(VP_View_Extra_L), typeof(VP_View_Extra_R), typeof(VP_View_Trade),
            typeof(VP_View_Experimental_L), typeof(VP_View_Experimental_R),
            typeof(VP_View_Settings_L), typeof(VP_View_Settings_R),
        };

        foreach (var page in pages)
        {
            try
            {
                Services.GetRequiredService(page);

                // The right page's own instance, where it has one.
                if (Services is IKeyedServiceProvider keyed)
                    keyed.GetKeyedService(page, MainViewPage_ViewLocator.RightSide);
            }
            catch (Exception)
            {
                // A page that will not build here would not have built on navigation
                // either, and the rest of the app is still worth showing.
            }
        }
    }
}
