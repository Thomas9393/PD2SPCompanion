using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;
using Avalonia.Media.Imaging;
using PD2SPCompanion.Shell.Imaging;
using PD2SPCompanion.Core.Graphics;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// One cell of a tree: a skill, how many points are in it, and why no point can go in, if none can
/// (<see cref="SkillEditor.CannotAdd"/>).
/// </summary>
public sealed class SkillCellViewModel(SkillEntry skill, int points, int characterLevel, byte classId, string? cannotAdd,
                                       IReadOnlyList<string>? synergies = null, int bonus = 0,
                                       IReadOnlyList<string>? description = null, int waiting = 0)
{
    public SkillEntry Skill { get; } = skill;

    /// <summary>The game's own icon for this skill, or null when the sheet has none.</summary>
    public Bitmap? Icon { get; } =
        SpriteBitmap.From(SkillIcons.Current?.Load(classId, skill.IconCel));

    public bool HasIcon => Icon is not null;

    public string Name => Skill.Name;
    public int Points { get; } = points;
    public bool HasPoints => Points > 0;

    /// <summary>The minus's tooltip, and dimmed, why: no point in the skill (ShortcutText.Dimmed).</summary>
    public string RemoveTip => HasPoints
        ? AttributeRowViewModel.Stepper(L("Remove point")) + System.Environment.NewLine + L("Skills needing it lose theirs at 0")
        : ShortcutText.Dimmed(L("Remove point"), L($"No points in {Name}."));

    /// <summary>
    /// Whether a point can go in: none with no points to spend, nor past the
    /// skill's maximum, nor where the play mode asks for its level or the skills before it.
    /// </summary>
    public bool CanAdd => cannotAdd is null;

    /// <summary>The plus button's tip: what it does, and dimmed, why it cannot.</summary>
    public string AddTip => cannotAdd is null ? AttributeRowViewModel.Stepper(L("Add point")) : ShortcutText.Dimmed(L("Add point"), cannotAdd);

    /// <summary>Row and column are one-based in the table and zero-based in a Grid.</summary>
    public int GridRow => Skill.Row - 1;
    public int GridColumn => Skill.Column - 1;

    /// <summary>Dimmed when the character is not yet high enough to have taken it.</summary>
    public bool IsReachable => characterLevel >= Skill.RequiredLevel;

    public string Detail => Skill.RequiredLevel > 1 ? L($"Level {Skill.RequiredLevel}") : "";

    /// <summary>The levels the character's gear adds (SkillTips.BonusFrom).</summary>
    public int Bonus { get; } = bonus;

    public bool HasBonus => Bonus > 0;

    /// <summary>
    /// The badge on the icon, the base points and then +xx in the same blue font: the
    /// skill's own points, and after them what items add. Shown for a skill only items give, too - "0 +3".
    /// Without a hard point all, class and tree skills wait and the cell stays grey; "+N to the skill" and an
    /// oskill grant it, light it, and the rest then counts too - "0 +27". With no such grant, a bonus such as +all
    /// skills still leaves it greyed out, while a +single skill with no hard point lights it as 0 +xx.
    /// The cell is lit where the badge shows (the view's <c>taken</c>).
    /// </summary>
    public bool ShowsBadge => HasPoints || HasBonus;

    public string PointsText => $"{Points}";

    /// <summary>" +3" after the points, the gap its own; nothing where items add none.</summary>
    public string BonusText => BonusSign + BonusNumber;

    /// <summary>The sign and the number apart: the sign is drawn in the body's font (AttributeRowViewModel.BonusSign).</summary>
    public string BonusSign => HasBonus ? " +" : "";

    public string BonusNumber => HasBonus ? $"{Bonus}" : "";

    /// <summary>
    /// What the tooltip says: the skill and what it does, its hard points and
    /// the soft points items add, what it needs, and the synergies it receives as the game words them (SkillTips).
    /// No figures: gear moves the game's own lines (King Leoric's skeletons), and a plausible
    /// wrong number in an editor is worse than none.
    /// </summary>
    public string Tooltip
    {
        get
        {
            var lines = new List<string> { Name };

            if (description is { Count: > 0 })
                lines.AddRange(description);
            else if (Skill.Description is { Length: > 0 } said)
                lines.Add(said);

            lines.Add("");
            string hard = Plural(Points, "1 hard point", $"{Points} hard points");
            lines.Add(Points == 0
                ? Bonus > 0 ? L($"Level {Bonus}: {Bonus} from items") : L("No points")
                : Bonus > 0 ? L($"Level {Points + Bonus}: {hard} + {Bonus} from items") : hard);

            // Without a hard point or a grant (an oskill, "+N to the skill") what the rest of the gear would add
            // waits for the first point: no hard point invested, yet boosted by gear.
            if (waiting > 0)
                lines.Add(Bonus > 0
                    ? L($"+{waiting} more from items with a hard point")
                    : L($"+{waiting} from items with a hard point"));

            if (Skill.RequiredLevel > 1)
            {
                lines.Add(IsReachable
                    ? L($"Require level {Skill.RequiredLevel}")
                    : L($"Require level {Skill.RequiredLevel}") + System.Environment.NewLine + L($"Current level {characterLevel}"));
            }

            if (Skill.RequiresNames.Count > 0)
                lines.Add(L($"Require {string.Join(", ", Skill.RequiresNames)}"));

            if (synergies is { Count: > 0 })
            {
                lines.Add("");
                lines.AddRange(synergies);
            }

            return string.Join("\n", lines);
        }
    }
}

/// <summary>A tree, as a tab.</summary>
public sealed partial class SkillPageViewModel(SkillPage page, IReadOnlyList<SkillCellViewModel> cells)
    : ObservableObject
{
    public int Number { get; } = page.Number;
    public string Name { get; } = page.Name;
    public IReadOnlyList<SkillCellViewModel> Cells { get; } = cells;
    public int Spent => Cells.Sum(cell => cell.Points);
    /// <summary>The tab's label, in capitals as every tab's.</summary>
    public string Header => Spent > 0 ? $"{Name.ToUpperInvariant()}  ({Spent})" : Name.ToUpperInvariant();

    /// <summary>Whether this is the tree on screen; the tab button keeps its selected look.</summary>
    [ObservableProperty] private bool _isCurrent;
}

/// <summary>
/// The character page's right half: the three skill trees, edited a point at a time where the
/// play mode edits the character - by the tables' rules, see
/// <see cref="SkillEditor"/>: Casual as the game's own tree allows, Free without the level and
/// prerequisites, and a skill emptied taking what needs it with it.
/// </summary>
public partial class VP_ViewModel_Char_R : ViewModelBase
{
    public CharacterSession Session { get; }

    public ObservableCollection<SkillPageViewModel> Pages { get; } = [];

    /// <summary>The tree on screen, by its page number.</summary>
    [ObservableProperty] private int _currentPage = 1;

    public IReadOnlyList<SkillCellViewModel> CurrentCells =>
        Pages.FirstOrDefault(page => page.Number == CurrentPage)?.Cells ?? [];

    [RelayCommand]
    private void ShowPage(int number)
    {
        CurrentPage = number;
        MarkCurrent();
        OnPropertyChanged(nameof(CurrentCells));
    }

    private void MarkCurrent()
    {
        foreach (var page in Pages)
            page.IsCurrent = page.Number == CurrentPage;
    }

    private readonly WhenShown _changes;

    /// <summary>In front, or put away (the view, through PageDeck).</summary>
    public void Shown() => _changes.Shown();

    public void Hidden() => _changes.Hidden();

    /// <summary>Laid out ahead of a visit (MainView.PrepareCharacterPages): what is owed, done first.</summary>
    public void Settle() => _changes.Settle();

    /// <summary>Used by the designer, which gets no services.</summary>
    public VP_ViewModel_Char_R() : this(new CharacterSession(), new PlayRules(new EditorSettings())) { }

    public VP_ViewModel_Char_R(CharacterSession session, PlayRules rules, VP_ViewModel_Char_L? stats = null)
    {
        Stats = stats ?? new VP_ViewModel_Char_L();
        Rules = rules;
        Session = session;

        // Every edit and every move changed the session; behind the Items page the Character page's
        // cards were rebuilt for nobody - 7 ms of a move together (measured). Done while the
        // page is shown, once when it is shown next, or as it is laid out ahead (WhenShown).
        _changes = new WhenShown(Refresh);
        Session.Changed += _changes.Ask;
        Session.AttributesChanged += _changes.Ask;

        // A mode changed: the cells fix what they may do as they are built, so they are built again. Not for a look
        // the rules also carry (the socket toggle, the socket guard), which raise their own name.
        rules.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName is not (nameof(PlayRules.SocketsFaded) or nameof(PlayRules.SocketOpacity) or nameof(PlayRules.SocketGuard)))
            {
                OnPropertyChanged(nameof(EditsSkills));
                OnPropertyChanged(nameof(ShowsRespec));
                _changes.Ask();
            }
        };
        Refresh();
    }

    /// <summary>The attributes, above the skills: the left page's own view model, whose they are.</summary>
    public VP_ViewModel_Char_L Stats { get; }

    /// <summary>Whether the skills are unfolded.</summary>
    [ObservableProperty] private bool _skillsOpen = PD2SPCompanion.Shell.Services.Settings.FoldMemory.IsOpen(nameof(SkillsOpen));

    [RelayCommand]
    private void ToggleSkills() => SkillsOpen = !SkillsOpen;

    public bool HasTree => Pages.Count > 0;

    /// <summary>Skill points the character has not assigned.</summary>
    public int Unspent => Session.Skills?.Unspent ?? 0;

    /// <summary>What the play mode shows: Free adds skill points out of nowhere.</summary>
    public PlayRules Rules { get; }

    public bool CanRemoveUnspent => Unspent > 0;

    /// <summary>Remove points' tooltip, and dimmed, why: none left.</summary>
    public string RemoveUnspentTip => CanRemoveUnspent ? AttributeRowViewModel.Stepper(L("Remove points"))
        : ShortcutText.Dimmed(L("Remove points"), L("No skill points to spend."));

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

    /// <summary>
    /// Adds skill points to spend, or takes them away - Ctrl and Shift for 5 and 20.
    /// </summary>
    public void ChangeUnspent(int points)
    {
        if ((Session.Skills?.AddUnspent(points) ?? 0) != 0)
        {
            Session.Status = L($"Skill points to spend {Unspent}.");
            Session.RaiseAttributesChanged();
        }
    }

    public int Spent => Session.Skills?.Spent ?? 0;

    /// <summary>Whether points can be moved here: any play mode that edits the character.</summary>
    public bool EditsSkills => Rules.EditsCharacter;

    /// <summary>The skills' respec, in their card while it is open.</summary>
    public bool ShowsRespec => EditsSkills && SkillsOpen;

    partial void OnSkillsOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowsRespec));
        PD2SPCompanion.Shell.Services.Settings.FoldMemory.Keep(nameof(SkillsOpen), value);
    }

    private bool CanRespecSkills() => Session.Skills?.CanRespec ?? false;

    /// <summary>Respec's tooltip, and dimmed, why: no point put in.</summary>
    public string RespecSkillsTip => ShortcutText.Dimmed(L("Refund skill points"), CanRespecSkills() ? null : L("No skill points spent."));

    /// <summary>
    /// Hands back every skill point the character put in - the attributes have their own respec. The
    /// autosave writes it, and the session's history takes it back.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRespecSkills))]
    private void RespecSkills()
    {
        if (Session.Skills is not { } skills || !skills.CanRespec || !EditsSkills)
            return;

        // An Iron Golem cannot stay without its skill: its item needs somewhere to go first - the
        // inventory, the cube or the stash (full, the points stay in). Not asked
        // where an item worn gives the skill: the golem stays then.
        if (skills.RespecEmptiesGolemSkill && Session.GolemHasNoRoom() is { } refused)
        {
            Session.Status = refused;
            return;
        }

        int points = skills.RespecSkills();
        string golem = Session.DismissUnskilledGolem();
        Session.Status = (L($"Respec: {points} skill points refunded.") + " " + golem).Trim();
        Session.RaiseAttributesChanged();
    }

    /// <summary>Points into a skill - one, or 5 and 20 with Ctrl and Shift.</summary>
    [RelayCommand]
    private void AddPoint(SkillCellViewModel? cell) => AddPoints(cell, 1);

    /// <summary>Points out of a skill - one, or 5 and 20 with Ctrl and Shift.</summary>
    [RelayCommand]
    private void RemovePoint(SkillCellViewModel? cell) => RemovePoints(cell, 1);

    public void AddPoints(SkillCellViewModel? cell, int count)
    {
        if (cell is null || !EditsSkills || Session.Skills is not { } skills)
            return;

        int level = Session.Leveling?.Level ?? Session.OpenCard?.Save.Level ?? 0;
        var (added, stopped) = skills.Add(cell.Skill, count, level, freely: Rules.AddsCharacterPoints);

        if (added > 0)
        {
            Session.Status = $"{cell.Name} +{added}." + (count > 1 && stopped is not null ? $" {stopped}" : "");
            Session.RaiseAttributesChanged();
        }
        else if (stopped is not null)
        {
            Session.Status = stopped;
        }
    }

    public void RemovePoints(SkillCellViewModel? cell, int count)
    {
        if (cell is null || !EditsSkills || Session.Skills is not { } skills)
            return;

        // The last point out of the Iron Golem's skill, or of one it stands on, dismisses the golem.
        if (skills.RemovalEmptiesGolemSkill(cell.Skill, count) && Session.GolemHasNoRoom() is { } noRoom)
        {
            Session.Status = noRoom;
            return;
        }

        var refunded = skills.Remove(cell.Skill, count);
        if (refunded.Count == 0)
            return;

        int total = refunded.Sum(each => each.Points);
        string skillsRefunded = string.Join(", ", refunded.Select(each => $"{each.Skill.Name} {each.Points}"));
        string golem = Session.DismissUnskilledGolem();
        Session.Status = ((refunded.Count == 1
            ? $"{cell.Name} -{total}."
            : L($"Refunded {total} points: {skillsRefunded}.")) + " " + golem).Trim();
        Session.RaiseAttributesChanged();
    }

    private void Refresh()
    {
        // The tree being worked on stays on screen: every point moved rebuilds the pages.
        int showing = CurrentPage;
        Pages.Clear();

        if (Session.Skills is { IsAvailable: true } skills)
        {
            // As it stands: the character page moves it, and a skill needs the level it has now.
            int level = Session.Leveling?.Level ?? Session.OpenCard?.Save.Level ?? 0;
            byte classId = Session.OpenCard?.Save.ClassId ?? 0;

            // What the gear in hand gives - worn, but the other weapon set, and the charms - counted as the
            // requirements count it: the tooltips' soft points.
            var gear = Session.GearInHand();

            foreach (var page in skills.Tree.Pages)
            {
                var cells = page.Skills
                    .Select(entry => new SkillCellViewModel(
                        entry, skills.PointsIn(entry.Slot), level, classId,
                        skills.CannotAdd(entry, level, freely: Rules.AddsCharacterPoints),
                        SkillTips.Synergies(entry),
                        SkillTips.BonusFrom(gear, entry, classId, skills.PointsIn(entry.Slot)),
                        SkillTips.Description(entry),
                        skills.PointsIn(entry.Slot) == 0 ? SkillTips.Waiting(gear, entry, classId) : 0))
                    .ToList();

                Pages.Add(new SkillPageViewModel(page, cells));
            }
        }

        CurrentPage = Pages.Any(page => page.Number == showing) ? showing
            : Pages.Count > 0 ? Pages[0].Number : 1;
        MarkCurrent();

        OnPropertyChanged(nameof(CurrentCells));
        OnPropertyChanged(nameof(HasTree));
        UnspentText = $"{Unspent}";
        OnPropertyChanged(nameof(Unspent));
        OnPropertyChanged(nameof(CanRemoveUnspent));
        OnPropertyChanged(nameof(RemoveUnspentTip));
        OnPropertyChanged(nameof(RespecSkillsTip));
        OnPropertyChanged(nameof(Spent));
        OnPropertyChanged(nameof(EditsSkills));
        OnPropertyChanged(nameof(ShowsRespec));
        RespecSkillsCommand.NotifyCanExecuteChanged();
    }
}
