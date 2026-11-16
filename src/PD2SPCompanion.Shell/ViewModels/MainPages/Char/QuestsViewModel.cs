using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;


using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>One quest on the Character page's list, in the difficulty shown.</summary>
public sealed partial class QuestRow : ObservableObject
{
    private readonly QuestsViewModel _owner;

    public QuestRow(QuestsViewModel owner, QuestProgress.Info info, int difficulty, QuestProgress.State state,
                    bool editable, bool revertible)
    {
        _owner = owner;
        Info = info;
        Difficulty = difficulty;
        State = state;
        IsEditable = editable;
        CanRevert = revertible;
    }

    public QuestProgress.Info Info { get; }

    public int Difficulty { get; }

    public string Name => Info.Name;

    /// <summary>
    /// On hover, only for the quests whose reward waits for its NPC - the imbue, the socket, the
    /// personalize; the rest show none.
    /// </summary>
    public string? Gives => Info.Key switch
    {
        "ActI.ToolsOfTheTrade" => L("Charsi imbue"),
        "ActV.SiegeOnHarrogath" => L("Larzuk socket"),
        "ActV.BetrayalOfHarrogath" => L("Anya personalization"),
        _ => null,
    };

    public QuestProgress.State State { get; }

    /// <summary>Whether the play mode lets the character be edited at all - not in Legit.</summary>
    public bool IsEditable { get; }

    /// <summary>Completed here this session: its revert is offered, and says why where it cannot.</summary>
    public bool CanRevert { get; }

    public bool IsDone => State == QuestProgress.State.Done;

    public bool IsWaiting => State == QuestProgress.State.RewardWaiting;

    public bool IsInProgress => State == QuestProgress.State.InProgress;

    /// <summary>
    /// One way, but for this session's revert: only what is not done yet - and not already waiting for
    /// what it leaves waiting - can be completed; never the Hellforge, which the game starts in Act IV
    /// (QuestProgress.CanComplete).
    /// </summary>
    public bool CanComplete => IsEditable && QuestProgress.CanComplete(Info, State);

    public string StateText => State switch
    {
        QuestProgress.State.Done => L("Done"),
        QuestProgress.State.RewardWaiting => L("Reward waiting"),
        QuestProgress.State.InProgress => L("In progress"),
        _ => L("Not started"),
    };

    /// <summary>What completing gives, and what it does not - the confirm's lines, now on the button.</summary>
    public string Hint => string.Join(Environment.NewLine, new[] { L("Complete"), Completing(Info) }
        .Concat(Notes(Info, Difficulty)).OfType<string>());

    /// <summary>
    /// What completing a quest does beyond marking it: the reward left waiting in game, or what the
    /// editor adds - null when neither.
    /// </summary>
    public static string? Completing(QuestProgress.Info quest)
    {
        if (quest.Waits)
            return L($"Reward waits in game: {quest.Reward}.");

        var parts = new System.Collections.Generic.List<string>();
        if (QuestProgress.Gifts.GetValueOrDefault(quest.Key) is { } gift)
        {
            if (gift.SkillPoints > 0) parts.Add(Plural(gift.SkillPoints, "1 skill point", $"{gift.SkillPoints} skill points"));
            if (gift.StatPoints > 0) parts.Add(L($"{gift.StatPoints} stat points"));
            if (gift.Life > 0) parts.Add(L($"{gift.Life} life"));
        }
        if (quest.Key == "ActI.DenOfEvil")
            parts.Add(L("respec on offer"));

        // An act's last quest opens the next act; the game's last, the next difficulty.
        if (quest.Key is "ActI.SistersToTheSlaughter" or "ActII.TheSevenTombs" or "ActIII.TheGuardian" or "ActIV.TerrorsEnd")
            parts.Add(L("opens the next act"));
        if (quest.Key == "ActV.EveOfDestruction")
            parts.Add(L("opens the next difficulty"));

        return parts.Count == 0 ? null : L($"Adds {string.Join(", ", parts)}.");
    }

    /// <summary>
    /// The item rewards the editor does not hand out, and where the cube is got: the player should be told -
    /// said on the button and again on the strip once done.
    /// </summary>
    public static IEnumerable<string> Notes(QuestProgress.Info quest, int difficulty)
    {
        var notGiven = QuestProgress.NotGiven(quest, difficulty);
        if (notGiven.Count > 0)
            yield return L($"Not given: {string.Join(", ", notGiven)}.");
        if (QuestProgress.HowToGet(quest) is { } how)
            yield return how;
    }

    [RelayCommand]
    private void Complete() => _owner.Complete(this);

    [RelayCommand]
    private void Revert() => _owner.Revert(this);
}

/// <summary>An act's quests, as a tab - the waypoints' layout: one act shown at a time.</summary>
public sealed partial class QuestAct(int number, string name, IReadOnlyList<QuestRow> rows) : ObservableObject
{
    public int Number { get; } = number;
    public string Name { get; } = name;
    public IReadOnlyList<QuestRow> Rows { get; } = rows;

    /// <summary>The flat choice's label, in capitals as every tab's.</summary>
    public string Label => Name.ToUpperInvariant();

    [ObservableProperty] private bool _isCurrent;
}

/// <summary>
/// The quests on the Character page:
/// each difficulty's, as they stand, and - where the play mode edits the character - a way to complete
/// them. One click completes a quest (no marks, no confirm); one completed this
/// session can be reverted while nothing since has touched what it wrote and its points are unspent
/// (QuestProgress.Revert). A quest with a reward is left <b>waiting</b> for it, so the game gives it when
/// its NPC is spoken to.
/// </summary>
public sealed partial class QuestsViewModel : ObservableObject
{
    private readonly CharacterSession _session;
    private readonly PlayRules _rules;
    private readonly ISFXManager? _sfxManager;

    /// <summary>This session's completions, by difficulty and quest - what each changed, to take it back.</summary>
    private readonly Dictionary<(int Difficulty, string Key), QuestProgress.Completion> _completed = [];

    /// <summary>The session's changes, followed while the Character page is shown (VP_ViewModel_Char_L).</summary>
    public WhenShown Changes { get; }

    public QuestsViewModel(CharacterSession session, PlayRules rules, ISFXManager? sfxManager = null)
    {
        _session = session;
        _rules = rules;
        _sfxManager = sfxManager;

        // Every edit and every move changed the session; behind the Items page the Character page's
        // cards were rebuilt for nobody - 7 ms of a move together (measured). Done while the
        // page is shown, once when it is shown next, or as it is laid out ahead (WhenShown).
        Changes = new WhenShown(Rebuild);
        session.Changed += Changes.Ask;
        session.PropertyChanged += (_, changed) =>
        {
            // Another character, or none: what was completed was the last one's.
            if (changed.PropertyName == nameof(CharacterSession.IsLoaded))
            {
                _completed.Clear();
                Rebuild();
            }
        };
        rules.PropertyChanged += (_, _) => Rebuild();
        Rebuild();
    }

    public static readonly string[] DifficultyNames = [N("Normal"), N("Nightmare"), N("Hell")];

    /// <summary>The difficulty shown: 0 Normal, 1 Nightmare, 2 Hell.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNormal))]
    [NotifyPropertyChangedFor(nameof(IsNightmare))]
    [NotifyPropertyChangedFor(nameof(IsHell))]
    private int _difficulty;

    partial void OnDifficultyChanged(int value) => Rebuild();

    public bool IsNormal => Difficulty == 0;
    public bool IsNightmare => Difficulty == 1;
    public bool IsHell => Difficulty == 2;

    [RelayCommand]
    private void ShowDifficulty(string which)
    {
        _sfxManager?.Method_ButtonSFX_Press();
        Difficulty = Array.IndexOf(DifficultyNames, which) is var index and >= 0 ? index : 0;
    }

    public ObservableCollection<QuestAct> Acts { get; } = [];

    /// <summary>The act shown, 1 to 5 - kept as the difficulty changes, as the waypoints keep theirs.</summary>
    private int _act = 1;

    /// <summary>The shown act's quests.</summary>
    public IReadOnlyList<QuestRow> Rows => Acts.FirstOrDefault(each => each.IsCurrent)?.Rows ?? [];

    [RelayCommand]
    private void ShowAct(QuestAct? tab)
    {
        if (tab is null)
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        _act = tab.Number;
        foreach (var each in Acts)
            each.IsCurrent = ReferenceEquals(each, tab);
        OnPropertyChanged(nameof(Rows));
    }

    /// <summary>Done per difficulty: "Normal 27/27 · Nightmare 12/27 · Hell 0/27".</summary>
    [ObservableProperty] private string _summary = "";

    public bool Edits => _rules.EditsCharacter;

    /// <summary>Completes a quest at once - one edit, which the autosave writes and the history can undo.</summary>
    internal void Complete(QuestRow row)
    {
        if (!row.CanComplete || _session.CompleteQuest(row.Difficulty, row.Info) is not { } done)
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        if (done.Undo is { } undo)
            _completed[(row.Difficulty, row.Info.Key)] = undo;

        _session.Status = string.Join(" ", new[] { L($"{row.Name} completed."), done.Opened }
            .Concat(QuestRow.Notes(row.Info, row.Difficulty)).OfType<string>());

        // Points and life went into the attributes: the pages showing them read them again.
        _session.RaiseAttributesChanged();
        Rebuild();
    }

    /// <summary>Takes back a quest completed this session, or says on the strip why it cannot.</summary>
    internal void Revert(QuestRow row)
    {
        if (!_completed.TryGetValue((row.Difficulty, row.Info.Key), out var undo))
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        if (_session.RevertQuest(undo) is { } why)
        {
            _session.Status = $"{row.Name}: {why}";
            // Changed since - the history undid it, or another edit wrote over it: nothing left to take back.
            if (why == QuestProgress.ChangedSince)
            {
                _completed.Remove((row.Difficulty, row.Info.Key));
                Rebuild();
            }
            return;
        }

        _completed.Remove((row.Difficulty, row.Info.Key));
        _session.Status = L($"{row.Name} reverted.");
        _session.RaiseAttributesChanged();
        Rebuild();
    }

    private void Rebuild()
    {
        Acts.Clear();
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(Edits));

        if (_session.Quests is not { } quests)
        {
            Summary = "";
            return;
        }

        // The classic game has no Act V.
        var shown = QuestProgress.All.Where(q => q.Act < 5 || _session.IsExpansion).ToList();

        Summary = string.Join(" · ", DifficultyNames.Select((name, d) =>
            $"{L(name)} {shown.Count(q => QuestProgress.StateOf(QuestProgress.QuestOf(quests, d, q)) == QuestProgress.State.Done)} / {shown.Count}"));

        string[] actNames = [L("Act I"), L("Act II"), L("Act III"), L("Act IV"), L("Act V")];
        // In the quest log's order, not the save's (QuestProgress.LogOrder).
        foreach (var act in shown.GroupBy(q => q.Act))
        {
            Acts.Add(new QuestAct(act.Key, actNames[act.Key - 1], [.. act.OrderBy(q => QuestProgress.LogOrder.ToList().IndexOf(q.Key)).Select(q => new QuestRow(this, q, Difficulty,
                QuestProgress.StateOf(QuestProgress.QuestOf(quests, Difficulty, q)), Edits,
                Edits && _completed.ContainsKey((Difficulty, q.Key))))]));
        }

        // The act shown stays through a rebuild - a completion, another difficulty - or Act I where it is gone.
        var current = Acts.FirstOrDefault(each => each.Number == _act) ?? Acts.FirstOrDefault();
        if (current is not null)
        {
            current.IsCurrent = true;
            _act = current.Number;
        }
        OnPropertyChanged(nameof(Rows));
    }
}
