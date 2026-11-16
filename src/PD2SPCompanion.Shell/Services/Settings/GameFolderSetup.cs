using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PD2SPCompanion.Shell.Services.Character;

namespace PD2SPCompanion.Shell.Services.Settings;

/// <summary>
/// The first run: asking where Diablo II is, before anything is read out of it.
/// <para>
/// Shown as an overlay over the whole window, the way <see cref="Generic.ConfirmService"/>
/// is, and for the same reason - a second window would need the same custom frame as the
/// main one, and nothing behind this is usable until it is answered.
/// </para>
/// <para>
/// It asks even when the folder was found, rather than settling it silently. What follows
/// from the answer is the editor writing to that folder's saves, so it is worth one glance
/// at the path before it starts. It is asked once; after that the setting stands.
/// </para>
/// </summary>
public sealed partial class GameFolderSetup : ObservableObject
{
    private readonly EditorSettings _settings;
    private readonly CharacterSession _session;

    public GameFolderSetup(EditorSettings settings, CharacterSession session)
    {
        _settings = settings;
        _session = session;

        // The mode as this overlay's buttons mark it, whichever road it was changed by: this object lasts as long as the
        // program, and the overlay can come back after Settings has changed it.
        settings.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(EditorSettings.PlayMode))
            {
                OnPropertyChanged(nameof(IsLegit));
                OnPropertyChanged(nameof(IsCasual));
                OnPropertyChanged(nameof(IsFree));
                OnPropertyChanged(nameof(ModeNote));
            }
        };

        // Asked when nothing is settled - a first run, or a folder that has since gone.
        if (!settings.HasGameFolder)
            Begin();
    }

    /// <summary>
    /// How long the overlay takes to go. Long enough to read as a fade rather than a cut,
    /// short enough that nobody waits for it.
    /// </summary>
    private static readonly TimeSpan Fading = TimeSpan.FromMilliseconds(260);

    /// <summary>
    /// Whether the question is live: opaque, and the only thing that answers the pointer.
    /// </summary>
    [ObservableProperty] private bool _isAsking;

    /// <summary>
    /// Whether the overlay is on screen at all, which outlasts the question by as long as
    /// it takes to fade. Bound to visibility, where <see cref="IsAsking"/> is bound to
    /// opacity - a control cannot fade out of a visibility that has already gone.
    /// </summary>
    [ObservableProperty] private bool _isShown;

    /// <summary>What the overlay fades between. A transition in the view does the rest.</summary>
    public double Fade => IsAsking ? 1 : 0;

    partial void OnIsAskingChanged(bool value) => OnPropertyChanged(nameof(Fade));

    /// <summary>What is in the box, which is what CONTINUE would settle on.</summary>
    [ObservableProperty] private string _folder = "";

    /// <summary>The folders found, offered as one button each. Usually one, often none.</summary>
    public ObservableCollection<string> Found { get; } = [];

    /// <summary>
    /// The list, only where there is a choice: one install found is already in the box, and a list of
    /// that one read as a second box beside it.
    /// </summary>
    public bool ShowsFound => Found.Count > 1;

    /// <summary>
    /// Why what is typed will not do, or empty where it will. Shown under the box rather
    /// than only on pressing CONTINUE, so it answers while the path is being corrected.
    /// </summary>
    public string Problem => GameLocation.Problem(Folder) ?? "";

    public bool HasProblem => Folder.Length > 0 && Problem.Length > 0;

    public bool CanContinue => Problem.Length == 0;

    /// <summary>
    /// The line above the box, which there is one of only in the second case: asked
    /// before, and the folder that was settled on has since gone. A first run needs no
    /// line - the title is the question.
    /// </summary>
    public string Reason =>
        _settings.GameFolder.Length > 0 ? L($"{_settings.GameFolder} not found.") : "";

    public bool HasReason => Reason.Length > 0;

    /// <summary>
    /// The folder settled on is PD2's version line, as the Settings page shows it: which install this is,
    /// at a glance, where there are several. Empty until the folder will do.
    /// </summary>
    public string FolderVersion =>
        CanContinue && ModVersion.Of(Folder).Name is { Length: > 0 } named ? L($"Project Diablo 2 {named}") : "";

    public bool HasFolderVersion => FolderVersion.Length > 0;

    partial void OnFolderChanged(string value)
    {
        // ProjectD2 picked or typed: the Diablo II folder around it, shown in the box. Posted: a box
        // still pushing what was typed ignores a change made back to it in the same turn.
        if (GameLocation.Resolve(value) is { } resolved && resolved != GameLocation.Tidy(value))
        {
            Dispatcher.UIThread.Post(() => Folder = resolved);
            return;
        }

        OnPropertyChanged(nameof(FolderVersion));
        OnPropertyChanged(nameof(HasFolderVersion));
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(HasProblem));
        OnPropertyChanged(nameof(CanContinue));
    }

    /// <summary>Looks for the game and asks, with the best guess already in the box.</summary>
    public void Begin()
    {
        Found.Clear();

        foreach (string found in GameLocation.Detect())
            Found.Add(found);

        Folder = Found.FirstOrDefault() ?? "";

        OnPropertyChanged(nameof(ShowsFound));
        OnPropertyChanged(nameof(Reason));
        OnPropertyChanged(nameof(HasReason));
        OnPropertyChanged(nameof(AsksMode));

        // Shown before it is asked, so the first frame is the overlay already in place
        // rather than the app behind it.
        IsShown = true;
        IsAsking = true;
    }

    /// <summary>
    /// A first run, where the play mode is asked beside the folder - not a folder that has since
    /// gone, whose owner has chosen a mode before.
    /// </summary>
    public bool AsksMode => _settings.GameFolder.Length == 0;

    public bool IsLegit => _settings.PlayMode == PlayPolicy.LegitName;
    public bool IsCasual => _settings.PlayMode == PlayPolicy.CasualName;
    public bool IsFree => _settings.PlayMode == PlayPolicy.FreeName;

    /// <summary>
    /// What the chosen mode is, in a line - a first-time user cannot know what the three names mean,
    /// worded short and plain.
    /// </summary>
    public string ModeNote => _settings.PlayMode switch
    {
        PlayPolicy.LegitName => L("Vault, compendium and trade. No editing."),
        PlayPolicy.FreeName => L("Unrestricted editing."),
        _ => L("Game-legal editing."),
    };

    /// <summary>Makes a preset the mode. Casual stands until another is chosen.</summary>
    [RelayCommand]
    private void ChooseMode(string name)
    {
        if (PlayPolicy.Preset(name) is not { } preset)
            return;

        _settings.ChoosePolicy(preset);
    }

    [RelayCommand]
    private void Choose(object? which)
    {
        if (which is string found)
            Folder = found;
    }

    [RelayCommand]
    private async Task Browse()
    {
        // A picker that will not open leaves the box as it was, which can still be typed
        // into.
        if (await FolderPicker.Pick(L("Select Project Diablo II game folder")) is { Length: > 0 } path)
            Folder = path;
    }

    /// <summary>
    /// Closes the editor.
    /// <para>
    /// This overlay covers the window, title bar and all, so without this a machine the
    /// game is not installed on would have no way out of the question but to be killed.
    /// Nothing has been opened yet, so there is nothing to ask about on the way.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void Quit()
    {
        if (Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    /// <summary>A folder was settled: what the start could not ask without one is asked now (MainViewModel).</summary>
    public event Action? Settled;

    /// <summary>Settles on what is in the box, and lets the rest of the app start.</summary>
    [RelayCommand]
    private void Accept()
    {
        if (!CanContinue)
            return;

        string chosen = GameLocation.Tidy(Folder);

        _settings.GameFolder = chosen;
        _session.UseGameFolder(chosen);

        // The question goes now and the overlay goes once it has faded, so what is behind
        // it arrives out of the fade rather than after it.
        IsAsking = false;

        DispatcherTimer.RunOnce(() => IsShown = false, Fading);

        Settled?.Invoke();
    }
}
