using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// One of the four allocated attributes, with the two moves the game allows.
/// </summary>
public sealed partial class AttributeRowViewModel : ObservableObject
{
    private readonly VP_ViewModel_Char_L _page;

    public string Stat { get; }

    /// <summary>"strength" as the save spells it, "Strength" as a person reads it.</summary>
    public string Label { get; }

    public AttributeRowViewModel(VP_ViewModel_Char_L page, string stat)
    {
        _page = page;
        Stat = stat;
        Label = L(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(stat));
    }

    /// <summary>The four as the template's keys (tests/words.py reads N(...)); Label translates them.</summary>
    private static readonly string[] Shown = [N("Strength"), N("Dexterity"), N("Vitality"), N("Energy")];

    private AttributeEditor? Editor => _page.Session.Attributes;

    public int Value => Editor?.Value(Stat) ?? 0;

    public int Minimum => Editor?.Minimum(Stat) ?? 0;

    /// <summary>What the class started with, which is as low as this can go.</summary>
    public string Floor => L($"Min {Minimum}");

    /// <summary>
    /// What the gear in use adds, written +xx in the blue font, strength reading 30 + 20: worn items and charms whose requirements are met, their
    /// sockets and set bonuses with them - as the requirements count gear.
    /// </summary>
    public int Bonus => _page.GearBonus(Stat);

    public bool HasBonus => Bonus != 0;

    /// <summary>"+20" after the attribute's own points; nothing where the gear adds none.</summary>
    public string BonusText => BonusSign + BonusNumber;

    /// <summary>
    /// The sign and the number apart: the view draws the sign in the body's font - the titles' font draws a
    /// plus as an ornate cross that read as an asterisk.
    /// </summary>
    public string BonusSign => Bonus > 0 ? "+" : Bonus < 0 ? "-" : "";

    public string BonusNumber => Bonus != 0 ? $"{Math.Abs(Bonus)}" : "";

    /// <summary>
    /// The points of every row of this row's kind - the rows the gear adds to, or the rows it does not - and
    /// the gear's part on every row that has one: the figure is as wide as the longest of its kind
    /// (AttributeFigure).
    /// </summary>
    public IReadOnlyList<string> PointsOfAll => _page.PointsOfAll(HasBonus);

    public IReadOnlyList<string> GearOfAll => _page.GearOfAll;

    /// <summary>The figure's tooltip, only where the gear adds something: the sum, and its two parts.</summary>
    public string? ValueTip => HasBonus
        ? Bonus > 0 ? L($"{Label} {Value + Bonus}: {Value} base + {Bonus} from items") : L($"{Label} {Value + Bonus}: {Value} base - {-Bonus} from items")
        : null;

    public bool CanRaise => Editor?.CanRaise(Stat) ?? false;
    public bool CanLower => Editor?.CanLower(Stat) ?? false;

    /// <summary>A points stepper's tooltip: what a click does, and its modifier lines - the last one "all" unless said.</summary>
    public static string Stepper(string does, string? every = null) => string.Join(Environment.NewLine,
        does, L("Ctrl + LMB: 5 points"), L("Shift + LMB: 20 points"), every ?? L("Ctrl + Shift + LMB: all"));

    /// <summary>The plus's tooltip, and dimmed, why: no table, or nothing to spend.</summary>
    public string RaiseTip => CanRaise ? Stepper(L("Add point"))
        : ShortcutText.Dimmed(L("Add point"), _page.CanRespec ? L("No attribute points to spend.") : L("charstats.txt not readable."));

    /// <summary>The minus's tooltip, and dimmed, why: no table, or at what the class started with.</summary>
    public string LowerTip => CanLower ? Stepper(L("Remove point"))
        : ShortcutText.Dimmed(L("Remove point"), _page.CanRespec ? L($"{Label} at class minimum {Minimum}.") : L("charstats.txt not readable."));

    /// <summary>
    /// How many points one click moves. A modifier is the only way to say "more than one"
    /// on a control this small, and the tooltips spell it out.
    /// </summary>
    public const int ManyPoints = 5;

    /// <summary>A bigger step, for moving a stat a meaningful distance in one go.</summary>
    public const int ManyMorePoints = 20;

    /// <summary>
    /// Ctrl and Shift together, PD2's own key for it: as many as there are - every point left into a stat or a
    /// skill, every point back out. More than any character has; each spender stops where it must.
    /// </summary>
    public const int EveryPoint = 100_000;

    [RelayCommand]
    private void Raise() => Invest(1);

    [RelayCommand]
    private void Lower() => Refund(1);

    /// <summary>Spends up to <paramref name="points"/>, stopping when there are none left.</summary>
    public void Invest(int points)
    {
        int moved = 0;
        while (moved < points && (Editor?.CanRaise(Stat) ?? false))
        {
            Editor.Raise(Stat);
            moved++;
        }

        if (moved > 0)
            _page.AfterSpend(L($"{Label} {Value}."));
    }

    /// <summary>Takes back up to <paramref name="points"/>, stopping at the class minimum.</summary>
    public void Refund(int points)
    {
        int moved = 0;
        while (moved < points && (Editor?.CanLower(Stat) ?? false))
        {
            Editor.Lower(Stat);
            moved++;
        }

        if (moved > 0)
            _page.AfterSpend(L($"{Label} {Value}."));
    }

    /// <summary>Re-reads everything from the editor after a point moves.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(Minimum));
        OnPropertyChanged(nameof(Floor));
        OnPropertyChanged(nameof(Bonus));
        OnPropertyChanged(nameof(HasBonus));
        OnPropertyChanged(nameof(BonusText));
        OnPropertyChanged(nameof(BonusSign));
        OnPropertyChanged(nameof(BonusNumber));
        OnPropertyChanged(nameof(PointsOfAll));
        OnPropertyChanged(nameof(GearOfAll));
        OnPropertyChanged(nameof(ValueTip));
        OnPropertyChanged(nameof(CanRaise));
        OnPropertyChanged(nameof(CanLower));
        OnPropertyChanged(nameof(RaiseTip));
        OnPropertyChanged(nameof(LowerTip));
        RaiseCommand.NotifyCanExecuteChanged();
        LowerCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>
/// The character page: who is open, and their attributes.
/// </summary>
public partial class VP_ViewModel_Char_L : ViewModelBase
{
    public CharacterSession Session { get; }

    private readonly ISFXManager _sfxManager = null!;

    public ObservableCollection<AttributeRowViewModel> Attributes { get; } = [];

    /// <summary>Used by the designer, which gets no services.</summary>
    public VP_ViewModel_Char_L() : this(new CharacterSession(), null!, new PlayRules(new EditorSettings())) { }

    public VP_ViewModel_Char_L(CharacterSession session, ISFXManager sfxManager, PlayRules rules,
                               WaypointsViewModel? waypoints = null, QuestsViewModel? quests = null,
                               MapSeedViewModel? mapSeed = null)
    {
        MapSeed = mapSeed ?? new MapSeedViewModel(session, new EditorSettings(), rules);
        Waypoints = waypoints ?? new WaypointsViewModel(session, sfxManager);
        Waypoints.MayEdit = () => rules.EditsWaypoints;
        Quests = quests ?? new QuestsViewModel(session, rules);
        Session = session;
        _sfxManager = sfxManager;
        Rules = rules;

        foreach (var stat in AttributeEditor.Editable)
            Attributes.Add(new AttributeRowViewModel(this, stat));

        // Every edit and every move changed the session; behind the Items page the Character page's
        // cards were rebuilt for nobody - 7 ms of a move together (measured). Done while the
        // page is shown, once when it is shown next, or as it is laid out ahead (WhenShown).
        _changes = new WhenShown(Refresh);
        Session.Changed += _changes.Ask;

        // A respec or a spent point moves what a level can give back; the skills page spends too.
        Session.AttributesChanged += RefreshLevel;
        Rules.PropertyChanged += (_, _) =>
        {
            // The steppers, the slider and their tooltips answer from the mode too: all of it told again.
            RefreshLevel();
            OnPropertyChanged(nameof(ShowsRespec));
        };
        Refresh();
    }

    private readonly WhenShown _changes;

    /// <summary>The page's cards, each following the session's changes while the page is shown.</summary>
    private WhenShown[] Gates => [_changes, Quests.Changes, Waypoints.Changes, MapSeed.Changes];

    /// <summary>In front, or put away (the view, through PageDeck).</summary>
    public void Shown() { foreach (var gate in Gates) gate.Shown(); }

    public void Hidden() { foreach (var gate in Gates) gate.Hidden(); }

    /// <summary>Laid out ahead of a visit (MainView.PrepareCharacterPages): what is owed, done first.</summary>
    public void Settle() { foreach (var gate in Gates) gate.Settle(); }

    /// <summary>The character's quests, the first card under the character card.</summary>
    public QuestsViewModel Quests { get; }

    /// <summary>The character's waypoints, the card under the quests.</summary>
    public WaypointsViewModel Waypoints { get; }

    /// <summary>The character's map seed, the last card.</summary>
    public MapSeedViewModel MapSeed { get; }

    // The three cards under the character card fold: all open to begin with,
    // folded as the player likes - and kept so (FoldMemory).
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _questsOpen = PD2SPCompanion.Shell.Services.Settings.FoldMemory.IsOpen(nameof(QuestsOpen));

    partial void OnQuestsOpenChanged(bool value) => PD2SPCompanion.Shell.Services.Settings.FoldMemory.Keep(nameof(QuestsOpen), value);
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _waypointsOpen = PD2SPCompanion.Shell.Services.Settings.FoldMemory.IsOpen(nameof(WaypointsOpen));

    partial void OnWaypointsOpenChanged(bool value) => PD2SPCompanion.Shell.Services.Settings.FoldMemory.Keep(nameof(WaypointsOpen), value);
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _mapSeedOpen = PD2SPCompanion.Shell.Services.Settings.FoldMemory.IsOpen(nameof(MapSeedOpen));

    partial void OnMapSeedOpenChanged(bool value) => PD2SPCompanion.Shell.Services.Settings.FoldMemory.Keep(nameof(MapSeedOpen), value);

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ToggleQuests() => QuestsOpen = !QuestsOpen;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ToggleWaypoints() => WaypointsOpen = !WaypointsOpen;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ToggleMapSeed() => MapSeedOpen = !MapSeedOpen;

    /// <summary>
    /// Whether the attributes are unfolded - they are shown on the right page now, above the skills,
    /// each folding as the inventory's sections do.
    /// </summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(ShowsRespec))]
    private bool _attributesOpen = PD2SPCompanion.Shell.Services.Settings.FoldMemory.IsOpen(nameof(AttributesOpen));

    partial void OnAttributesOpenChanged(bool value) => PD2SPCompanion.Shell.Services.Settings.FoldMemory.Keep(nameof(AttributesOpen), value);

    /// <summary>Respec and Refit, under the attributes: where the play mode edits the character, while they are unfolded.</summary>
    public bool ShowsRespec => Rules.EditsCharacter && AttributesOpen;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ToggleAttributes() => AttributesOpen = !AttributesOpen;

    /// <summary>What the play mode shows: in Legit, the attributes are read, not changed.</summary>
    public PlayRules Rules { get; }

    /// <summary>Points the character has left to spend.</summary>
    public int Unspent => Session.Attributes?.Unspent ?? 0;

    /// <summary>
    /// False when the class table could not be read. Rather than offer a respec with no
    /// floor to enforce, the page says so and shows the values as they stand.
    /// </summary>
    public bool CanRespec => Session.Attributes?.IsAvailable ?? false;

    /// <summary>Points a respec would return.</summary>
    public int Refundable => Session.Attributes?.Refundable ?? 0;

    public bool CanRespec_Execute => Session.Attributes?.CanRespec ?? false;

    /// <summary>Respec's tooltip, and dimmed, why: no table, or no point put in.</summary>
    public string RespecTip => ShortcutText.Dimmed(L("Refund attribute points"),
        CanRespec_Execute ? null : CanRespec ? L("No attribute points spent.") : L("charstats.txt not readable."));

    /// <summary>
    /// Hands back every attribute point the character put in - the attributes only; the skills have
    /// their own respec in their card. The autosave writes it, and the
    /// session's history takes it back.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRespec_Execute))]
    private void Respec()
    {
        if (!CanRespec_Execute)
            return;

        _sfxManager?.Method_ButtonSFX_Press();

        int attributes = Session.Attributes?.RespecAttributes() ?? 0;

        Session.Status = L($"Respec: {attributes} attribute points refunded.");

        Refresh();
        Session.RaiseAttributesChanged();
    }

    /// <summary>
    /// Refit: points into Strength and Dexterity
    /// until the worn gear of both weapon sets meets them - unspent first, then Energy, then
    /// Vitality, the gear's bonuses helping.
    /// </summary>
    [RelayCommand]
    private void RefitGear() => Refit();

    public void Refit()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        Session.Status = Session.Refit();
        Refresh();
    }

    // ---- level and experience, and what the save stores of life, mana and stamina ------------

    private LevelEditor? Leveling => Session.Leveling is { IsAvailable: true } editor ? editor : null;

    /// <summary>
    /// Free mode's, as adding points is: a level brings points with it. Casual reads it.
    /// </summary>
    public bool EditsLevel => Rules.AddsCharacterPoints && Leveling is not null;

    public int Level => Session.Leveling?.Level ?? Session.OpenCard?.Save.Level ?? 0;

    /// <summary>"Level 50 Assassin": the card's own line, following the level as it moves.</summary>
    public string CardDescription =>
        Session.OpenCard is { } card ? L($"Level {Level} {card.Save.ClassName}") : "";

    /// <summary>The level and the experience as typed, applied on Enter or on leaving the box.</summary>
    [ObservableProperty] private string _levelText = "";

    [ObservableProperty] private string _experienceText = "";

    public string ExperienceShown => (Session.Leveling?.Experience ?? 0).ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>
    /// How far into this level the experience is, 0 to 99 - the slider under the level,
    /// a gauge where the level is only read. Set, it moves the experience and keeps the level.
    /// </summary>
    public double LevelProgress
    {
        get => Session.Leveling is { IsAvailable: true } editor ? Math.Min(editor.Progress, 99) : 0;
        set
        {
            // The same percent handed back by the slider is no change: putting the experience at that
            // percent's exact start would take away what was earned past it.
            int percent = (int)Math.Round(value);
            if (Leveling is { } editor && CanSlideProgress && percent != editor.Progress && editor.SetProgress(percent))
            {
                // One line for a whole drag: every step of the slider is a change.
                if (Session.Status != L("Experience changed."))
                    Session.Status = L("Experience changed.");
                Refresh();
                Session.RaiseAttributesChanged();
            }
        }
    }

    /// <summary>Wherever the level moves; not at the last level, where there is no next to go towards.</summary>
    public bool CanSlideProgress => EditsLevel && Leveling is { AtMaxLevel: false };

    public string ProgressText => Session.Leveling is { IsAvailable: true } editor ? $"{editor.Progress}%" : "";

    public bool AtMaxLevel => Session.Leveling is { IsAvailable: true, AtMaxLevel: true };

    /// <summary>The bar's name: the way to the next level, or none left.</summary>
    public string ProgressLabel => AtMaxLevel ? L("Maxed") : L("Next level");

    /// <summary>The bar's ends: the totals this level and the next begin at.</summary>
    public string LevelStartText => Session.Leveling is { IsAvailable: true } editor
        ? editor.LevelStart.ToString("N0", CultureInfo.InvariantCulture) : "";
    public string NextLevelStartText => Session.Leveling is { IsAvailable: true } editor
        ? editor.NextLevelStart.ToString("N0", CultureInfo.InvariantCulture) : "";

    public bool CanRaiseLevel => EditsLevel && Level < Leveling!.HighestLevel;

    public bool CanLowerLevel => EditsLevel && Level > Leveling!.LowestLevel;

    /// <summary>The level's steppers' tooltips, and dimmed, why (LevelEditor.CannotRaise, CannotLower).</summary>
    public string RaiseLevelTip => CanRaiseLevel
        ? string.Join(Environment.NewLine, L("Raise level"), L("Ctrl + LMB: 5 levels"), L("Shift + LMB: 20 levels"), L("Ctrl + Shift + LMB: the most"))
        : ShortcutText.Dimmed(L("Raise level"), Leveling?.CannotRaise);

    public string LowerLevelTip => CanLowerLevel
        ? string.Join(Environment.NewLine, L("Lower level"), L("Ctrl + LMB: 5 levels"), L("Shift + LMB: 20 levels"), L("Ctrl + Shift + LMB: level 1"))
        : ShortcutText.Dimmed(L("Lower level"), Leveling?.CannotLower);

    /// <summary>What the save stores, before gear: shown on the card as it is, since gear is not added here.</summary>
    public string BaseLife => $"{Session.Leveling?.Base.Life ?? 0}";
    public string BaseMana => $"{Session.Leveling?.Base.Mana ?? 0}";
    public string BaseStamina => $"{Session.Leveling?.Base.Stamina ?? 0}";

    [RelayCommand]
    private void RaiseLevel() => MoveLevel(Level + 1);

    /// <summary>Several levels at once - Ctrl and Shift on the steppers, 5 and 20, as the attributes.</summary>
    public void StepLevel(int levels) => MoveLevel(Level + levels);

    [RelayCommand]
    private void LowerLevel() => MoveLevel(Level - 1);

    /// <summary>The level box, applied: held to what the points allow, as the steppers are.</summary>
    public void CommitLevel()
    {
        if (int.TryParse(LevelText.Trim(), out int level))
            MoveLevel(level);
        else
            RefreshLevel();
    }

    /// <summary>The experience box, applied: the level follows it.</summary>
    public void CommitExperience()
    {
        if (Leveling is { } editor && EditsLevel
            && long.TryParse(ExperienceText.Trim().Replace(",", ""), NumberStyles.None, CultureInfo.InvariantCulture, out long experience)
            && editor.SetExperience(experience))
        {
            AfterLevel(L($"Experience {ExperienceShown}."));
        }
        else
        {
            RefreshLevel();
        }
    }

    private void MoveLevel(int level)
    {
        if (Leveling is { } editor && EditsLevel && editor.SetLevel(level))
            AfterLevel(L($"Level {Level}."));
        else
            RefreshLevel();
    }

    /// <summary>
    /// A level moved: points, life, mana and stamina moved with it, so the attributes and the
    /// skills page read again, and the character is marked changed. Said on the strip, as every edit is: the
    /// autosave names the undo step and the backup by what the strip last said, and an edit that said nothing
    /// would be recorded under the act before it.
    /// </summary>
    private void AfterLevel(string said)
    {
        _sfxManager?.Method_ButtonSFX_Press();
        Session.Status = said;
        Refresh();
        Session.RaiseAttributesChanged();
    }

    private void RefreshLevel()
    {
        LevelText = Level.ToString(CultureInfo.InvariantCulture);
        ExperienceText = ExperienceShown;
        OnPropertyChanged(nameof(Level));
        OnPropertyChanged(nameof(EditsLevel));
        OnPropertyChanged(nameof(CardDescription));
        OnPropertyChanged(nameof(ExperienceShown));
        OnPropertyChanged(nameof(LevelProgress));
        OnPropertyChanged(nameof(CanSlideProgress));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(AtMaxLevel));
        OnPropertyChanged(nameof(ProgressLabel));
        OnPropertyChanged(nameof(LevelStartText));
        OnPropertyChanged(nameof(NextLevelStartText));
        OnPropertyChanged(nameof(CanRaiseLevel));
        OnPropertyChanged(nameof(CanLowerLevel));
        OnPropertyChanged(nameof(RaiseLevelTip));
        OnPropertyChanged(nameof(LowerLevelTip));
        OnPropertyChanged(nameof(BaseLife));
        OnPropertyChanged(nameof(BaseMana));
        OnPropertyChanged(nameof(BaseStamina));
    }

    // ---- Free mode: unspent points out of nowhere ------------------------------------------

    public bool CanRemoveUnspent => Unspent > 0;

    /// <summary>Remove points' tooltip, and dimmed, why: none left.</summary>
    public string RemoveUnspentTip => CanRemoveUnspent ? AttributeRowViewModel.Stepper(L("Remove points"))
        : ShortcutText.Dimmed(L("Remove points"), L("No attribute points to spend."));

    /// <summary>
    /// The points to spend as the box shows them (a stepper and its box, as the level's).
    /// Typed in Free mode, applied on Enter or on leaving the box; elsewhere the box is read only.
    /// </summary>
    [ObservableProperty] private string _unspentText = "0";

    public void CommitUnspent()
    {
        if (Rules.AddsCharacterPoints && int.TryParse(UnspentText.Trim(), out int wanted) && wanted >= 0 && wanted != Unspent)
            ChangeUnspent(wanted - Unspent);
        UnspentText = $"{Unspent}";
    }

    [RelayCommand]
    private void AddUnspent() => ChangeUnspent(1);

    [RelayCommand]
    private void RemoveUnspent() => ChangeUnspent(-1);

    /// <summary>Adds points to spend, or takes them away - Ctrl and Shift for 5 and 20, as the steppers.</summary>
    public void ChangeUnspent(int points)
    {
        if ((Session.Attributes?.AddUnspent(points) ?? 0) != 0)
            AfterSpend(L($"Attribute points to spend {Unspent}."));
    }

    /// <summary>Called by a row once it has moved a point: said, as the level is (<see cref="AfterLevel"/>).</summary>
    public void AfterSpend(string said)
    {
        _sfxManager?.Method_ButtonSFX_Press();
        Session.Status = said;

        // Every row is affected: spending the last point stops them all from raising.
        Refresh();
        Session.RaiseAttributesChanged();
    }

    private void Refresh()
    {
        UnspentText = $"{Unspent}";
        OnPropertyChanged(nameof(Unspent));
        OnPropertyChanged(nameof(CanRemoveUnspent));
        OnPropertyChanged(nameof(RemoveUnspentTip));
        OnPropertyChanged(nameof(CanRespec));
        OnPropertyChanged(nameof(Refundable));
        OnPropertyChanged(nameof(CanRespec_Execute));
        OnPropertyChanged(nameof(RespecTip));
        RespecCommand.NotifyCanExecuteChanged();

        // What the gear gives, asked once for the four rows: a point moved can bring an item into use.
        _gear = Session.GearAttributes();

        // What the rows of each kind show, for a figure to be as wide as the longest of its kind: lists made
        // anew, so that a figure hears of a neighbor's digit more.
        _pointsPlain = [.. Attributes.Where(row => !row.HasBonus).Select(row => $"{row.Value}")];
        _pointsGeared = [.. Attributes.Where(row => row.HasBonus).Select(row => $"{row.Value}")];
        GearOfAll = [.. Attributes.Where(row => row.HasBonus).Select(row => row.BonusNumber)];
        foreach (var row in Attributes)
            row.Refresh();

        RefreshLevel();
    }


    private IReadOnlyDictionary<string, int> _gear = new Dictionary<string, int>();

    /// <summary>What the gear in use adds to an attribute (CharacterSession.GearAttributes), as of the last refresh.</summary>
    public int GearBonus(string stat) => _gear.GetValueOrDefault(stat);

    private IReadOnlyList<string> _pointsPlain = [], _pointsGeared = [];

    /// <summary>The points of the rows the gear adds to, or of the rows it does not, as of the last refresh.</summary>
    public IReadOnlyList<string> PointsOfAll(bool geared) => geared ? _pointsGeared : _pointsPlain;

    /// <summary>The gear's part on every row that has one, as of the last refresh.</summary>
    public IReadOnlyList<string> GearOfAll { get; private set; } = [];
}
