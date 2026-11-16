using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>One waypoint, as a row that can be switched on and off.</summary>
public sealed partial class WaypointRowViewModel(Waypoint point, Action afterToggle, Func<bool>? mayEdit = null)
    : ObservableObject
{
    public string Name => point.Name;
    public bool IsTown => point.IsTown;

    /// <summary>
    /// A main camp that is already held is fixed: the row shows it on and refuses to turn
    /// it off. One that is not held can still be granted, which is how a bulk enable
    /// reaches acts the character has never set foot in.
    /// </summary>
    public bool CanToggle => point.CanDisable || !point.IsFound;

    /// <summary>A main camp that is fixed, which the row says so the lock is not a mystery.</summary>
    public bool IsFixed => IsTown && !CanToggle;

    public string Hint => !IsTown
        ? Name
        : CanToggle
            ? L("Act town, not reached yet")
            : L("Act town, reached") + Environment.NewLine + L("Cannot be removed");

    public bool IsFound
    {
        get => point.IsFound;
        set
        {
            if (point.IsFound == value)
                return;

            // Read only where the play mode edits no waypoints (Legit shows them read-only): told again,
            // so the tick stands as the save has it.
            if (mayEdit?.Invoke() == false)
            {
                OnPropertyChanged();
                return;
            }

            if (!value && !point.CanDisable)
            {
                // Refused. Told anyway, so the control snaps back to what the save holds.
                OnPropertyChanged();
                return;
            }

            point.IsFound = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanToggle));
            OnPropertyChanged(nameof(IsFixed));
            OnPropertyChanged(nameof(Hint));
            afterToggle();
        }
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsFound));
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(IsFixed));
        OnPropertyChanged(nameof(Hint));
    }
}

/// <summary>A difficulty, as a tab.</summary>
public sealed partial class DifficultyTabViewModel(DifficultyWaypoints difficulty) : ObservableObject
{
    public DifficultyWaypoints Model { get; } = difficulty;
    public string Name => Model.Difficulty;
    public string Header => $"{Model.Difficulty}  ({Model.Tally})";

    /// <summary>The flat choice's label, in capitals as every tab's; the counts are in the card's summary.</summary>
    public string Label => Model.Difficulty.ToUpperInvariant();

    [ObservableProperty] private bool _isCurrent;

    public void Refresh() => OnPropertyChanged(nameof(Header));
}

/// <summary>An act within the chosen difficulty, as a tab.</summary>
public sealed partial class ActTabViewModel(ActWaypoints act) : ObservableObject
{
    public ActWaypoints Model { get; } = act;
    public string Name => Model.Act;
    public string Header => Model.Act;
    public string Tally => Model.Tally;

    /// <summary>The flat choice's label: "ACT I" - its count is beside Grant and Remove, so five fit narrow.</summary>
    public string Label => Model.Act.ToUpperInvariant();

    [ObservableProperty] private bool _isCurrent;

    public void Refresh()
    {
        OnPropertyChanged(nameof(Tally));
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Model));
    }
}

/// <summary>
/// A character's waypoints: a tab per difficulty, a tab per act, and every waypoint in the chosen
/// act as a switch - a folding card on the Character page, under the quests.
/// </summary>
public sealed partial class WaypointsViewModel : ObservableObject
{
    /// <summary>Everything in the act on screen.</summary>
    public const int ScopeAct = 0;

    /// <summary>Every act of the difficulty on screen.</summary>
    public const int ScopeDifficulty = 1;

    /// <summary>All three difficulties.</summary>
    public const int ScopeEverything = 2;

    public CharacterSession Session { get; }

    private readonly ISFXManager _sfxManager = null!;

    /// <summary>
    /// Whether the play mode edits waypoints (PlayRules.EditsWaypoints, handed over by the Character page). Where
    /// it does not - Legit - the card is read: its ticks and its Grant and Remove do nothing. It is still
    /// shown, as the quests are.
    /// </summary>
    public Func<bool> MayEdit { get; set; } = () => true;

    public ObservableCollection<DifficultyTabViewModel> Difficulties { get; } = [];
    public ObservableCollection<ActTabViewModel> Acts { get; } = [];
    public ObservableCollection<WaypointRowViewModel> Points { get; } = [];

    /// <summary>The act's waypoints in two columns, read down then across as the game lists them.</summary>
    public IReadOnlyList<WaypointRowViewModel> FirstColumn => Points.Take((Points.Count + 1) / 2).ToList();
    public IReadOnlyList<WaypointRowViewModel> SecondColumn => Points.Skip((Points.Count + 1) / 2).ToList();

    /// <summary>The act on screen and what of it is held: "Act I · 9/9".</summary>
    public string ActSummary => CurrentAct is { } act ? $"{act.Name} · {act.Tally}" : "";

    /// <summary>The session's changes, followed while the Character page is shown (VP_ViewModel_Char_L).</summary>
    public WhenShown Changes { get; }

    public WaypointsViewModel(CharacterSession session, ISFXManager sfxManager)
    {
        Session = session;
        _sfxManager = sfxManager;
        // Every edit and every move changed the session; behind the Items page the Character page's
        // cards were rebuilt for nobody - 7 ms of a move together (measured). Done while the
        // page is shown, once when it is shown next, or as it is laid out ahead (WhenShown).
        Changes = new WhenShown(Rebuild);
        Session.Changed += Changes.Ask;
        Rebuild();
    }

    public bool HasWaypoints => Difficulties.Count > 0;

    /// <summary>
    /// The button's line: how many are found on each difficulty - "Normal 39 / 39 · Nightmare 12 / 39 ·
    /// Hell 0 / 39".
    /// </summary>
    public string Summary => string.Join(" · ", Session.Waypoints.Select(d => $"{d.Difficulty} {d.Found} / {d.Total}"));

    /// <summary>Every waypoint the character has, across all three difficulties.</summary>
    public string Total => HasWaypoints
        ? $"{Session.Waypoints.Sum(d => d.Found)} / {Session.Waypoints.Sum(d => d.Total)}"
        : "";

    private DifficultyTabViewModel? CurrentDifficulty =>
        Difficulties.FirstOrDefault(tab => tab.IsCurrent) ?? Difficulties.FirstOrDefault();

    private ActTabViewModel? CurrentAct =>
        Acts.FirstOrDefault(tab => tab.IsCurrent) ?? Acts.FirstOrDefault();

    /// <summary>The two buttons' words: "Grant Act I on Normal", "Remove Act I on Normal".</summary>
    public string GrantLabel => L($"Grant {EnableScope}");

    public string RemoveLabel => L($"Remove {EnableScope}");

    /// <summary>What a plain click on the enable button would cover, for its label.</summary>
    public string EnableScope => CurrentAct is { } act && CurrentDifficulty is { } difficulty
        ? L($"{act.Name} on {difficulty.Name}")
        : L("this act");

    [RelayCommand]
    private void ShowDifficulty(DifficultyTabViewModel tab)
    {
        _sfxManager?.Method_ButtonSFX_Press();

        foreach (var difficulty in Difficulties)
            difficulty.IsCurrent = ReferenceEquals(difficulty, tab);

        // Which act is on screen carries across, so stepping through the difficulties
        // compares the same act rather than dropping back to Act I each time.
        string? act = CurrentAct?.Name;
        FillActs(tab.Model);
        ShowAct(Acts.FirstOrDefault(a => a.Name == act) ?? Acts.FirstOrDefault());
    }

    [RelayCommand]
    private void ShowActTab(ActTabViewModel tab)
    {
        _sfxManager?.Method_ButtonSFX_Press();
        ShowAct(tab);
    }

    /// <summary>
    /// The plain click. A command of its own rather than a parameter, because XAML would
    /// hand a literal parameter over as a string and the generated command takes an int.
    /// </summary>
    [RelayCommand]
    private void EnableAct() => EnableAll(ScopeAct);

    /// <summary>
    /// Grants waypoints, as far out as the modifier asked: this act, this difficulty, or
    /// the lot. It only ever adds - nothing here takes a waypoint away in bulk.
    /// </summary>
    public void EnableAll(int scope)
    {
        if (!MayEdit())
            return;

        _sfxManager?.Method_ButtonSFX_Press();

        int granted;
        string where;

        switch (scope)
        {
            case ScopeEverything:
                granted = WaypointProgress.EnableAll(Session.Waypoints);
                where = L("every difficulty");
                break;

            case ScopeDifficulty when CurrentDifficulty is { } difficulty:
                granted = difficulty.Model.EnableAll();
                where = difficulty.Name;
                break;

            default:
                if (CurrentAct is not { } act)
                    return;
                granted = act.Model.EnableAll();
                where = EnableScope;
                break;
        }

        Say(granted == 0
            ? L($"Every waypoint in {where} was already found.")
            : Plural(granted, $"Granted {granted} waypoint in {where}.", $"Granted {granted} waypoints in {where}."));

        Session.MarkEdited();
        RefreshCounts();
    }

    /// <summary>The plain click on the removing half. See <see cref="EnableAct"/>.</summary>
    [RelayCommand]
    private void RemoveAct() => RemoveAll(ScopeAct);

    /// <summary>
    /// Takes waypoints back, as far out as the modifier asked. A main camp of an act the
    /// character has actually reached is left alone - see <see cref="Waypoint.CanDisable"/>.
    /// </summary>
    public void RemoveAll(int scope)
    {
        if (!MayEdit())
            return;

        _sfxManager?.Method_ButtonSFX_Press();

        int removed;
        string where;

        switch (scope)
        {
            case ScopeEverything:
                removed = WaypointProgress.DisableAll(Session.Waypoints);
                where = L("every difficulty");
                break;

            case ScopeDifficulty when CurrentDifficulty is { } difficulty:
                removed = difficulty.Model.DisableAll();
                where = difficulty.Name;
                break;

            default:
                if (CurrentAct is not { } act)
                    return;
                removed = act.Model.DisableAll();
                where = EnableScope;
                break;
        }

        Say(removed == 0
            ? L($"Nothing in {where} could be taken back.")
            : Plural(removed, $"Took back {removed} waypoint in {where}.", $"Took back {removed} waypoints in {where}."));

        Session.MarkEdited();
        RefreshCounts();
    }

    /// <summary>
    /// Says what the last grant, removal or switch came to, on the left page's strip, where the
    /// waypoints' card is.
    /// </summary>
    private void Say(string text) => Session.Messages.Post(text);

    /// <summary>Called by a row once it has switched a waypoint.</summary>
    private void AfterToggle()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        Say(L("Waypoints changed."));
        Session.MarkEdited();
        RefreshCounts();
    }

    private void ShowAct(ActTabViewModel? tab)
    {
        foreach (var act in Acts)
            act.IsCurrent = ReferenceEquals(act, tab);

        Points.Clear();
        if (tab is not null)
        {
            foreach (var point in tab.Model.Points)
                Points.Add(new WaypointRowViewModel(point, AfterToggle, () => MayEdit()));
        }

        OnPropertyChanged(nameof(FirstColumn));
        OnPropertyChanged(nameof(SecondColumn));
        OnPropertyChanged(nameof(ActSummary));
        OnPropertyChanged(nameof(EnableScope));
        OnPropertyChanged(nameof(GrantLabel));
        OnPropertyChanged(nameof(RemoveLabel));
    }

    private void FillActs(DifficultyWaypoints difficulty)
    {
        Acts.Clear();
        foreach (var act in difficulty.Acts)
            Acts.Add(new ActTabViewModel(act));
    }

    /// <summary>The tallies on the tabs and in the header, after anything moved.</summary>
    private void RefreshCounts()
    {
        foreach (var difficulty in Difficulties)
            difficulty.Refresh();
        foreach (var act in Acts)
            act.Refresh();
        foreach (var point in Points)
            point.Refresh();

        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(ActSummary));
    }

    private void Rebuild()
    {
        Difficulties.Clear();
        Acts.Clear();
        Points.Clear();

        foreach (var difficulty in Session.Waypoints)
            Difficulties.Add(new DifficultyTabViewModel(difficulty));

        if (Difficulties.Count > 0)
        {
            Difficulties[0].IsCurrent = true;
            FillActs(Difficulties[0].Model);
            ShowAct(Acts.FirstOrDefault());
        }

        OnPropertyChanged(nameof(HasWaypoints));
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(EnableScope));
        OnPropertyChanged(nameof(GrantLabel));
        OnPropertyChanged(nameof(RemoveLabel));
    }
}
