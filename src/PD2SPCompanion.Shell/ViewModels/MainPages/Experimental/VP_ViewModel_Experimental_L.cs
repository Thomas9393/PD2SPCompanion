using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Core.Testbed;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// The extras: things done to the game rather than to a character, which is why they are
/// not on a page that needs one open.
/// <para>
/// <b>This page is the one place this editor writes to the game install.</b> It writes loose
/// tables the game only reads when started with <c>-direct -txt</c>, so the stock launcher is
/// unaffected and nothing here changes how anyone else plays. The rule it bends and the terms
/// it keeps are set out on <see cref="DensityInstaller"/>.
/// </para>
/// </summary>
public partial class VP_ViewModel_Experimental_L : ViewModelBase
{
    public CharacterSession Session { get; }

    private readonly ISFXManager _sfxManager = null!;

    /// <summary>Used by the designer, which gets no services.</summary>
    public VP_ViewModel_Experimental_L() : this(new CharacterSession(), null!, new PlayRules(new EditorSettings()), new ConfirmService()) { }

    /// <summary>The question REMOVE asks first.</summary>
    private readonly ConfirmService _confirm;

    /// <summary>The game's lock: Apply and Remove write and delete in its folder.</summary>
    private readonly GameSync? _sync;

    /// <summary>The game folder <see cref="Look"/> last read: a folder settled after this page was built is read when it comes.</summary>
    private string _lookedAt = "";

    public VP_ViewModel_Experimental_L(CharacterSession session, ISFXManager sfxManager, PlayRules rules,
                                       ConfirmService confirm, GameSync? sync = null)
    {
        Session = session;
        _sfxManager = sfxManager;
        _confirm = confirm;
        _sync = sync;
        _settings = rules.Settings;
        DropPanel = new DropPanelViewModel(sfxManager, _settings, () => Session.GameRoot);
        DropPanel.Changed += Said;

        // The panel that could not open says why, where the page says everything else: it kept the reason to
        // itself, and its button did nothing that could be seen.
        DropPanel.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(DropPanelViewModel.Problem) && DropPanel.Problem is { Length: > 0 } why)
                Status = L($"Drop rates not read: {why}");
        };

        // The event says more than a folder settled - every file saved says it too - and a look puts the page's choices
        // back to what is installed, so only for a folder this page has not read.
        session.SaveFolderChanged += () =>
        {
            if (!string.Equals(_lookedAt, Session.GameRoot, StringComparison.OrdinalIgnoreCase))
                Look();
        };

        if (sync is not null)
        {
            sync.PropertyChanged += (_, changed) =>
            {
                if (changed.PropertyName is not (nameof(GameSync.IsGameRunning) or nameof(GameSync.IsWriteBlocked)))
                    return;

                OnPropertyChanged(nameof(CanApply));
                OnPropertyChanged(nameof(ApplyHint));
                OnPropertyChanged(nameof(CanRemove));
                OnPropertyChanged(nameof(RemoveHint));
            };
        }

        Look();
    }

    /// <summary>
    /// What the game multiplies monster counts by. One is the game as shipped, which is
    /// also what taking the override out leaves behind.
    /// </summary>
    [ObservableProperty] private double _multiplier = 5;

    /// <summary>
    /// The density multiples that can be chosen. One is the game as shipped. The last is MAX -
    /// every chosen area at the engine's ceiling (DensityOverride.MaxDensity).
    /// </summary>
    private static readonly double[] Multiples = [1, 2, 3, 4, 5, DensityOverride.MaxDensity];

    public IReadOnlyList<string> DensityOptions { get; } = ["1x", "2x", "3x", "4x", "5x", L("MAX")];

    /// <summary>The combo box's choice, as an index into <see cref="Multiples"/>.</summary>
    public int DensityIndex
    {
        get => Math.Max(0, Array.FindIndex(Multiples, each => Math.Abs(each - Multiplier) < 0.001));
        set
        {
            if (value < 0 || value >= Multiples.Length || Math.Abs(Multiples[value] - Multiplier) < 0.001)
                return;

            Multiplier = Multiples[value];
            Said();
        }
    }

    private string DensityLabel => DensityOptions[DensityIndex];

    /// <summary>
    /// How much bigger monster packs are: its own setting, since pack size
    /// belongs to the monster and cannot be kept to the chosen areas the way density is.
    /// </summary>
    [ObservableProperty] private int _packs = 1;

    public IReadOnlyList<string> PackOptions { get; } =
        [.. Enumerable.Range(1, DensityOverride.MostPacks).Select(n => $"{n}x")];

    public int PackIndex
    {
        get => Math.Clamp(Packs - 1, 0, DensityOverride.MostPacks - 1);
        set
        {
            if (value < 0 || value >= DensityOverride.MostPacks || value + 1 == Packs)
                return;

            Packs = value + 1;
            Said();
        }
    }

    /// <summary>
    /// Ordinary monsters always drop something (DropOverride): no-drop 0 on their main class only.
    /// </summary>
    [ObservableProperty] private bool _everyKill;

    partial void OnEveryKillChanged(bool value) => Said();

    /// <summary>
    /// The drop panel: a multiple for each of PD2's special items and each high rune, with
    /// their odds and presets. It replaced the one high rune box; an install made with that box reads back as each
    /// of Lem to Zod at its multiple.
    /// </summary>
    public DropPanelViewModel DropPanel { get; }

    private DropChoice Drops => new(EveryKill, DropPanel.Boosts);

    /// <summary>
    /// Bigger packs with only some areas chosen. Pack size is the monster's, so it changes every
    /// monster in every area whatever the areas are.
    /// Said under the setting, being a warning.
    /// </summary>
    public bool PacksReachFurther => Packs > 1 && Scope != DensityScope.All;

    /// <summary>
    /// Which areas the density reaches: everything, or the farming areas.
    /// </summary>
    [ObservableProperty] private DensityScope _scope = DensityScope.All;

    public IReadOnlyList<string> ScopeOptions { get; } = [L("All areas"), L("Level 85 + Cows"), L("Level 85 + Cows + Maps")];

    public int ScopeIndex
    {
        get => (int)Scope;
        set
        {
            if (value < 0 || value >= ScopeOptions.Count || value == (int)Scope)
                return;

            Scope = (DensityScope)value;
            Said();
        }
    }

    /// <summary>What a changed choice changes on the page.</summary>
    private void Said()
    {
        OnPropertyChanged(nameof(DensityIndex));
        OnPropertyChanged(nameof(PackIndex));
        OnPropertyChanged(nameof(PacksReachFurther));
        OnPropertyChanged(nameof(ScopeIndex));
        OnPropertyChanged(nameof(IsOriginal));
        OnPropertyChanged(nameof(AnyChosen));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(ApplyHint));
    }

    public bool IsOriginal => !DensityChanged && Packs == 1 && !Drops.Any;

    private bool DensityChanged => Math.Abs(Multiplier - 1) > 0.001;

    /// <summary>
    /// Whether the cow level should open from two tomes as well as from Wirt's Leg. See
    /// <see cref="CowPortalOverride"/> for what it appends, and why it appends a recipe
    /// rather than making the leg purchasable.
    /// </summary>
    [ObservableProperty] private bool _cowPortal;

    partial void OnCowPortalChanged(bool value)
    {
        OnPropertyChanged(nameof(AnyChosen));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(ApplyHint));
    }

    /// <summary>
    /// The recipe itself, so the toggle can show it rather than describe it, and the one
    /// thing the recipe cannot get round - the engine still wants the difficulty finished.
    /// </summary>
    public string CowPortalHint =>
        L($"{CowPortalOverride.Recipe}\nRequire current difficulty finished");

    /// <summary>
    /// Whether anything is asked for at all. Nothing chosen makes apply a teardown, which is
    /// the one sensible reading of applying the game as it ships.
    /// </summary>
    public bool AnyChosen => !IsOriginal || CowPortal;

    /// <summary>What apply would write, each named by the file it lands as.</summary>
    private List<string> Chosen()
    {
        var wanted = new List<string>();

        string where = Scope == DensityScope.All ? "" : $" ({ScopeOptions[ScopeIndex]})";

        if (DensityChanged)
            wanted.Add(L($"levels.txt at {DensityLabel}{where}"));

        if (Packs > 1)
            wanted.Add(L($"monstats.txt with packs at {Packs}x"));

        if (Drops.Any)
        {
            var parts = new List<string>();
            if (EveryKill)
                parts.Add(L("every kill dropping"));
            if (DropPanel.Boosts.Count > 0)
                parts.Add(L($"drop rates: {DropPanel.Summary}"));
            wanted.Add(L($"treasureclassex.txt with {string.Join(", ", parts)}"));
        }

        if (CowPortal)
            wanted.Add(L("cubemain.txt with the cow recipe"));

        return wanted;
    }

    /// <summary>
    /// What pressing apply will do, which depends on what is chosen: with nothing chosen
    /// there is nothing to generate, so it takes the overrides out instead.
    /// </summary>
    public string ApplyHint => (AnyChosen
            ? L($"Generate {Joined(Chosen())}")
            : IsInstalled ? L("Nothing chosen\nRemove mod files") : L("Nothing chosen"))
        + (_sync?.IsWriteBlocked == true ? Environment.NewLine + _sync.BlockedFor(L("apply")) : "");

    /// <summary>
    /// Something to write, or something to take out: with nothing chosen and nothing installed APPLY is dimmed,
    /// its tooltip saying so. Else it asks to remove mod files that are not there.
    /// </summary>
    public bool CanApply => (AnyChosen || IsInstalled) && _sync?.IsWriteBlocked != true;

    /// <summary>
    /// Set when the installed files were built from a different PD2 than the one installed
    /// now - which is the whole hazard of this page, because the game reads them in
    /// preference to the tables its own patch shipped.
    /// </summary>
    [ObservableProperty] private bool _isStale;

    [ObservableProperty] private string _staleNote = "";

    [ObservableProperty] private string _report = "";
    /// <summary>What the last install, removal or launch came to - said on the message strip.</summary>
    public string Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged();
            Session.Messages.Post(value);
        }
    }

    private string _status = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsOnlineNotice))]
    [NotifyPropertyChangedFor(nameof(RemoveHint))]
    [NotifyPropertyChangedFor(nameof(CanRemove))]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(ApplyHint))]
    private bool _isInstalled;

    /// <summary>Files to take out, and nothing against it now.</summary>
    public bool CanRemove => IsInstalled && _sync?.IsWriteBlocked != true;

    /// <summary>REMOVE's tooltip, with why where it is dimmed: nothing installed, or the game running.</summary>
    public string RemoveHint => !IsInstalled
        ? L("Remove mod files from game folder") + Environment.NewLine + L("No mod files installed.")
        : _sync?.IsWriteBlocked == true
            ? L("Remove mod files from game folder") + Environment.NewLine + _sync.BlockedFor(L("remove"))
            : L("Remove mod files from game folder");

    /// <summary>SHORTCUT's tooltip, with why where it is dimmed: no game where it is looked for.</summary>
    public string ShortcutHint => HasGame
        ? L("Create desktop shortcut (-3dfx -direct -txt)")
        : L("Create desktop shortcut (-3dfx -direct -txt)") + Environment.NewLine + L("Require game folder.");

    private readonly EditorSettings _settings = null!;

    /// <summary>
    /// The advice under the page's buttons while its files are installed. Said once, ever: the
    /// tables only apply when the game is started with LAUNCH or the shortcut, and online the
    /// server's tables decide - so it is a duty to say it, not a condition to keep repeating.
    /// The sidebar's red dot goes with it.
    /// </summary>
    public static string OnlineNotice => L("Mod files installed. Remove them before playing online.");

    /// <summary>Whether <see cref="OnlineNotice"/> shows: files installed, and never dismissed.</summary>
    public bool ShowsOnlineNotice => IsInstalled && !_settings.OnlineWarningDismissed;

    /// <summary>Dismisses the online notice for good, in the editor's settings.</summary>
    [RelayCommand]
    private void DismissOnlineNotice()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        _settings.OnlineWarningDismissed = true;
        OnPropertyChanged(nameof(ShowsOnlineNotice));
    }

    /// <summary>The game is in its folder, so a shortcut to it can be made.</summary>
    public bool HasGame => File.Exists(DensityInstaller.GameIn(Session.GameRoot));

    /// <summary>Writes the override, or takes it out when the choice is the game as shipped.</summary>
    [RelayCommand]
    private void Apply()
    {
        _sfxManager?.Method_ButtonSFX_Press();

        // The game started with -direct -txt compiles its tables into this very folder.
        if (_sync?.RefusedWhileRunning(L("apply")) == true)
            return;

        // What the folder holds now, not what this page last read: a stale no left the old files in force with no marker
        // that named them.
        bool installed = DensityInstaller.Current(Session.GameRoot).Installed;

        if (!AnyChosen)
        {
            if (installed)
                Remove();
            return;
        }

        try
        {
            using var game = GameData.Open(Session.GameRoot);

            var files = new List<ModdedFile>();
            string shortfall = "";

            if (Drops.Any)
            {
                if (!DensityOverride.RoundTrips(game, "treasureclassex"))
                {
                    Status = L("Refused. treasureclassex.txt does not round-trip.");
                    return;
                }

                files.Add(DropOverride.Build(game, EveryKill, DropPanel.Boosts).File);
            }

            if (Packs > 1)
            {
                if (!DensityOverride.RoundTrips(game, "monstats"))
                {
                    Status = L("Refused. monstats.txt does not round-trip.");
                    return;
                }

                // Every monster: the areas choice is density's alone (see PacksReachFurther).
                files.Add(DensityOverride.BuildPacks(game, Packs, DensityScope.All).File);
            }

            if (DensityChanged)
            {
                if (!DensityOverride.RoundTrips(game, "levels"))
                {
                    Status = L("Refused. levels.txt does not round-trip.");
                    return;
                }

                var (file, report) = DensityOverride.Build(game, Multiplier, Scope);
                files.Add(file);

                // The shortfall is said rather than swallowed: at the higher multiples a good
                // few levels are already dense enough that they cannot reach the full multiple.
                // MAX puts every value there on purpose, so it is not a shortfall to report.
                shortfall = report.AnyClamped && Multiplier < DensityOverride.MaxDensity
                    ? " " + L($"{report.Clamped} of {report.Scaled} hit the {DensityOverride.Ceiling} ceiling.")
                    : "";
            }

            if (CowPortal)
            {
                if (!DensityOverride.RoundTrips(game, "cubemain"))
                {
                    Status = L("Refused. cubemain.txt does not round-trip.");
                    return;
                }

                files.Add(CowPortalOverride.Build(game));
            }

            // Everything already installed comes off first, so a choice turned off takes its
            // file with it. Teardown removes exactly what the marker lists, and the marker is
            // then written afresh for what was chosen this time - without this, unticking
            // something would strand its file with nothing left to record it.
            // Asked before anything comes off: refused afterwards, the install that worked was already gone.
            if (DensityInstaller.Blocked(Session.GameRoot, files.Select(file => file.Table)) is { } blocked)
            {
                Status = blocked;
                return;
            }

            if (installed && DensityInstaller.Remove(Session.GameRoot) is { } stuck)
            {
                Status = stuck;

                // Some may have gone: the page says what is there.
                Look();
                return;
            }

            Status = DensityInstaller.Install(
                         Session.GameRoot, Multiplier, CowPortal,
                         ModVersion.Of(Session.GameRoot), files, Scope, Packs, Drops)
                     ?? L($"Wrote {Joined(Chosen())}.") + shortfall;
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            Status = problem.Message;
        }

        Look();
    }

    /// <summary>
    /// Takes the override out, removing only what the marker says was put in - asked first:
    /// it deletes files in the game folder, and no undo of the editor's brings them back.
    /// </summary>
    [RelayCommand]
    private void Remove()
    {
        _sfxManager?.Method_ButtonSFX_Press();

        if (_sync?.RefusedWhileRunning(L("remove")) == true)
            return;

        void Go()
        {
            // Looked at again: the game may have started while the question stood.
            if (_sync?.RefusedWhileRunning(L("remove")) == true)
                return;

            Status = DensityInstaller.Remove(Session.GameRoot) ?? L("Mod files removed.");
            Look();
        }

        _confirm.Ask(L("Remove mod files"), L("Mod files will be removed from the game folder."), L("REMOVE"), Go);
    }

    /// <summary>
    /// How the game has to be started for any of this to show - the shortcut's arguments:
    /// <c>-3dfx</c> because that is how PD2 is played, <c>-direct</c> to look in the loose tree
    /// first (against <c>ProjectD2</c>, the working directory), and <c>-txt</c> to read the table
    /// rather than the compiled <c>.bin</c> beside it, without which a <c>levels.txt</c> edit does
    /// nothing at all.
    /// </summary>
    public const string LaunchArguments = "-3dfx -direct -txt";

    /// <summary>
    /// Puts a shortcut on the desktop that starts the game the way this panel does.
    /// <para>
    /// Not named for what is applied: the overrides live in the install rather than in the
    /// shortcut, so a shortcut labelled 2x would run at whatever was applied last. It says
    /// only that this is the modded single player game, and the panel remains the place the
    /// choices are made.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void MakeShortcut()
    {
        _sfxManager?.Method_ButtonSFX_Press();

        string game = DensityInstaller.GameIn(Session.GameRoot);
        if (!File.Exists(game))
        {
            Status = L($"{game} not found.");
            return;
        }

        try
        {
            string where = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "PD2 - Modded SP.lnk");

            // Late-bound, so nothing has to reference the COM interop assembly for the one
            // thing in the app that makes a shortcut.
            // Windows only, as the whole editor is (the game is): said, so the analyser knows too.
            var shell = OperatingSystem.IsWindows() && Type.GetTypeFromProgID("WScript.Shell") is { } kind
                ? Activator.CreateInstance(kind)
                : null;

            if (shell is null)
            {
                Status = L("No shortcut made. Require Windows Script Host.");
                return;
            }

            dynamic maker = shell;
            dynamic link = maker.CreateShortcut(where);

            link.TargetPath = game;
            link.Arguments = LaunchArguments;
            link.WorkingDirectory = Path.GetDirectoryName(game)!;
            // PD2's own icon where there is one, and the game's where there is not.
            string launcher = DensityInstaller.LauncherIn(Session.GameRoot);
            link.IconLocation = File.Exists(launcher) ? launcher : game;
            link.Description = L("Project Diablo 2 single player, with the companion's modded tables");
            link.Save();

            Status = L("Shortcut created.");
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            Status = problem.Message;
        }
    }

    private void Look()
    {
        _lookedAt = Session.GameRoot;
        var state = DensityInstaller.Current(Session.GameRoot);
        IsInstalled = state.Installed;

        if (state.Installed)
        {
            Multiplier = state.Multiplier;
            CowPortal = state.CowPortal;
            Scope = state.Scope;
            Packs = state.Packs;
            EveryKill = state.Drops?.EveryKill ?? false;
            DropPanel.Take(state.Drops?.Boosts);
        }

        // Compared on the archive rather than on the version string: the string is written
        // by a launcher that may not be the one used, while the archive is the thing the
        // tables were read out of. A marker with neither is left alone - an override from
        // before this was recorded is not evidence of anything.
        var build = ModVersion.Of(Session.GameRoot);

        IsStale = state.Installed
                  && state.Archive.Length > 0
                  && !build.Matches(state.Archive);

        StaleNote = IsStale
            ? (state.Version.Length > 0 ? L($"Built for {state.Version}.") : L("Built for an earlier version."))
              + "\n" + L("Require remove and apply again.")
            : "";

        OnPropertyChanged(nameof(DensityIndex));
        OnPropertyChanged(nameof(PackIndex));
        OnPropertyChanged(nameof(PacksReachFurther));
        OnPropertyChanged(nameof(ScopeIndex));
        OnPropertyChanged(nameof(IsOriginal));
        OnPropertyChanged(nameof(AnyChosen));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(ApplyHint));
        OnPropertyChanged(nameof(HasGame));
        OnPropertyChanged(nameof(ShortcutHint));
    }
}
