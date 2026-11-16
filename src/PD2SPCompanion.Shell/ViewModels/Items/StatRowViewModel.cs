using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D2SLib.Model.Save;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// One thing a stat's parameter could be, where the parameter is an index into a short list
/// rather than a skill id: a class's skill tree, a class, or an element.
/// </summary>
public sealed record IndexChoice(int Id, string Name);

/// <summary>
/// One property of an item, as an editable row.
/// <para>
/// The limits are the format's own: a stat is stored as (value + Save Add) in Save Bits
/// bits, and anything outside that wraps and corrupts the item. They are not a judgement
/// about what a reasonable roll is - the tables do not say - so the steppers stop where
/// the file stops and no sooner.
/// </para>
/// </summary>
public sealed partial class StatRowViewModel : ObservableObject
{
    private readonly ItemStat _stat;

    /// <summary>
    /// Where this stat keeps its skill, if it keeps one. A stat with a skill has more than
    /// one thing to edit: which skill, and how much of it - and a skill stored as a number
    /// is not something anyone can set by knowing the number.
    /// </summary>
    private enum SkillField
    {
        None,

        /// <summary>The parameter is a skill id and the value is how many levels of it.</summary>
        Param,

        /// <summary>The parameter packs a skill and a level beside a chance or a charge count.</summary>
        Packed,

        /// <summary>The parameter is a class and one of its three trees, <c>class * 8 + tab</c>.</summary>
        Tab,

        /// <summary>The parameter is a class.</summary>
        Class,

        /// <summary>The parameter is an element, and the one field that does anything.</summary>
        Element,

        /// <summary>The parameter is a monster, and the value the chance of raising one.</summary>
        Monster,

        /// <summary>The parameter is a state, and the value the single bit that turns it on.</summary>
        State,
    }

    private readonly SkillField _skillField;

    public string Name => _stat.Stat ?? "?";
    /// <summary>
    /// The dim line under the name: the id, and what else the file keeps about the property - a
    /// level, charges, a class and tree, a raw param. Only the id where the row's own fields show
    /// the rest: "stat 188 · Amazon · Javelin and Spear Skills · param 2" said again what the
    /// skill-tree box beside it says.
    /// </summary>
    public string Detail => HasFieldLines
        ? string.Join(" · ", new[] { L($"stat {_stat.Id}"), ClassIsDrawn ? L("random class") : "", SkillIsDrawn ? L("random skill") : "" }
                                  .Where(part => part.Length > 0))
        : _described;

    /// <summary>
    /// The class this property names was drawn at random by its source - Hellfire Torch's
    /// "+X to [random class] Skill Levels" (AffixRanges.ClassDrawnFor): any of the seven is one
    /// the game rolls, so the class box has a die of its own.
    /// </summary>
    public bool ClassIsDrawn => _classDrawn && HasIndex && CanEdit;

    private readonly bool _classDrawn;

    /// <summary>
    /// The skill this property names was drawn at random from a range of ids by its source -
    /// Spirit Keeper's "+2 to [a random Druid skill]" (AffixRanges.DrawnFor): any skill in the
    /// range is one the game rolls, so the skill has a die of its own.
    /// </summary>
    public bool SkillIsDrawn => (_skillDrawn is not null || DrawnTogether is not null) && HasSkill && CanEdit;

    /// <summary>
    /// Where this line's skill is one with another line's - Ormus' Robes' grant and cast - the die draws both, the cast's
    /// chance and level with them (RandomSkillCast). Null for any other line.
    /// </summary>
    public Action? DrawnTogether { get; init; }

    private readonly RollRange? _skillDrawn;

    /// <summary>
    /// What this property's source drew at random beside its number - the class, the skill - drawn again
    /// (the Demonic Cube, which rolls everything the item's properties roll). True where
    /// either came out another than it was.
    /// </summary>
    public bool RerollDrawn()
    {
        var skill = _skillId;
        var index = _index;
        // A line drawn with another is the cube's to draw once, after every row (DrawSkillCast).
        if (DrawnTogether is null)
            RerollSkill();
        RerollClass();
        return _skillId != skill || _index != index;
    }

    /// <summary>
    /// A random skill's line drawn again (RandomSkillCast, Ormus' Robes): the skill, and where it is the cast the
    /// chance and the level the skill has by the rule - not a choice of the row's, so no mode's limit holds it.
    /// </summary>
    public void TakeDrawnSkill(int skill, int? chance = null, int? level = null)
    {
        if (SkillNames.Current?.Option(skill) is { } option)
            Skill = option;

        if (chance is { } rolled)
            Value = rolled.ToString();

        if (level is { } casts && _stat.SkillLevel != casts)
        {
            _stat.SkillLevel = casts;
            Notify();
            OnPropertyChanged(nameof(SkillLevelText));
            OnPropertyChanged(nameof(InGame));
        }
    }

    /// <summary>A skill from the source's range at random, as the game draws it - another than the one held, where there is another.</summary>
    [RelayCommand]
    private void RerollSkill()
    {
        if (SkillIsDrawn && DrawnTogether is { } both)
        {
            both();
            return;
        }

        if (!SkillIsDrawn || _skillDrawn is not { } range || SkillNames.Current is not { } names)
            return;

        var pool = Enumerable.Range(range.Min, range.Max - range.Min + 1)
            .Select(names.Option)
            .OfType<SkillOption>()
            .Where(skill => skill.Id != _skillId)
            .ToList();
        if (pool.Count > 0)
            Skill = pool[Random.Shared.Next(pool.Count)];
    }

    /// <summary>One of the seven at random, as the game draws it - another than the one held, as the skill's die gives another skill.</summary>
    [RelayCommand]
    private void RerollClass()
    {
        if (!ClassIsDrawn)
            return;

        var pool = IndexChoices.Where(choice => choice.Id != _index).ToList();
        if (pool.Count > 0)
            ChosenIndex = pool[Random.Shared.Next(pool.Count)];
    }

    private readonly string _described;

    /// <summary>Whether the row has fields of its own under the name: a skill, a tree, a level, charges, a colour.</summary>
    public bool HasFieldLines => HasSkill || HasIndex || HasSkillLevel || HasMaxCharges || IsColour;

    /// <summary>
    /// The stat as the game words it, which is what says whether a number is a count, a
    /// percentage or something else. Follows the value, so the wording keeps up with it.
    /// </summary>
    public string InGame =>
        Poisoning()
        ?? IndexPhrase()
        ?? StatText.Describe(Name, Current, Skill?.Name, _stat.SkillLevel, _stat.MaxCharges)
        ?? StatText.Unworded(Name, Current)
        ?? SkillPhrase();

    /// <summary>
    /// How the game words a stat whose parameter picks one of a set of strings.
    /// <para>
    /// <c>descstrpos</c> names only the first of the set - <c>StrSklTabItem1</c> is the
    /// Amazon's first tree, <c>ModStr3a</c> is the Amazon herself - and the game walks from
    /// there by the parameter. Worded from the key alone, every skill tree in the game read
    /// as "Javelin and Spear Skills" however it was set, which made the picker look broken
    /// when it was the sentence that was wrong.
    /// </para>
    /// <para>
    /// Built here rather than walked, because the walk is over keys whose numbering is the
    /// string table's own and does not follow the order a save numbers classes in.
    /// </para>
    /// </summary>
    private string? IndexPhrase() =>
        HasIndex ? StatText.ByParameter(Name, Current, _index)
        // A skill held in the parameter is worded as the game words it, and as the item's tooltip
        // and the warnings do: "+3 to Amplify Damage" for an oskill, "(Sorceress Only)" after a class
        // skill. SkillPhrase's "(Necromancer)" was ours alone.
        : _skillField == SkillField.Param && _skillId is int skill ? StatText.ByParameter(Name, Current, skill)
        : null;

    /// <summary>
    /// How the game says poison damage, which is not what the file stores.
    /// <para>
    /// Poison is kept as damage <b>a frame</b> and a length in frames: this knife holds 8
    /// and 50, and the game shows "+2 poison damage over 2 seconds". The damage is the
    /// per-frame figure over the whole length - <c>value * length / 256</c> - and the
    /// seconds are the length at the game's 25 frames a second.
    /// </para>
    /// <para>
    /// Without this the row reads "+8 poison damage", which is not a number the player will
    /// ever see, and which makes a legitimate roll look like an edit.
    /// </para>
    /// </summary>
    private string? Poisoning()
    {
        if (Name is not ("poisonmindam" or "poisonmaxdam") || _poisonLength <= 0)
            return null;

        int shown = (int)Math.Round(Current * _poisonLength / 256.0, MidpointRounding.AwayFromZero);
        double seconds = _poisonLength / 25.0;
        string over = Math.Abs(seconds - 1) < 0.005 ? L("over 1 second") : L($"over {seconds:0.##} seconds");

        return Name == "poisonmindam"
            ? L($"+{shown} poison damage {over}")
            : L($"up to {shown} poison damage {over}");
    }

    /// <summary>
    /// The length beside a poison damage stat, in frames. Zero on any other stat, and on a
    /// poison one whose run could not be read - the wording needs both halves, and half of
    /// it is worse than none.
    /// </summary>
    private readonly int _poisonLength;

    /// <summary>
    /// The wording for a stat the tables cannot word on their own. A single-skill bonus
    /// has no description string - its text is built from the skill it names, which is
    /// why the tables leave it blank - so it is built here: how many, which skill, and
    /// whose it is.
    /// </summary>
    private string SkillPhrase()
    {
        if (!HasSkill || Skill is not { } option)
            return "";

        string amount = Current >= 0 ? $"+{Current}" : Current.ToString();
        string owner = option.ClassName.Length > 0 ? $" ({option.ClassName})" : "";

        return $"{amount} to {option.Name}{owner}";
    }

    public bool HasInGame => InGame.Length > 0;

    /// <summary>
    /// Stats whose numbers are the mechanic rather than a roll, and so are not for editing.
    /// <para>
    /// PD2's melee splash is the one so far: a proc that always triggers, always on the
    /// same skill. Its chance is 100 because that is what it is, not because it rolled
    /// well, and its skill is fixed. Offering steppers on that invites making an item the
    /// game cannot mean anything by.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> Mechanics = new(StringComparer.OrdinalIgnoreCase)
    {
        "item_splashonhit",
    };

    /// <summary>True for a stat that is what it is. Nothing on the row will change it.</summary>
    public bool IsMechanic => Mechanics.Contains(Name);

    /// <summary>
    /// Nothing on the row will change it: a mechanic, one already struck out, or a bonus the
    /// set gives rather than the item. A set bonus is the set's, and an item's copy of it is
    /// written when enough of the set is worn - editing it here would be editing a note the
    /// game keeps about something else.
    /// </summary>
    public bool CanEdit => !IsMechanic && !IsDeleted && !IsSetBonus;

    /// <summary>
    /// Whether properties may be added, removed, and given numbers the game would not
    /// have rolled.
    /// <para>
    /// Off by default, so the ordinary way to work on an item cannot quietly forge it: the
    /// numbers move inside the range their source allows and the list of properties is the
    /// list the item's affixes account for. Turned on deliberately, per item, for the times
    /// when that is exactly what is wanted.
    /// </para>
    /// </summary>
    public static bool FreeEditing { get; set; }

    /// <summary>
    /// How far a charged skill's maximum may go: the play mode's "Defense, durability, quantity, charges, picture"
    /// (<see cref="Services.Settings.PlayPolicy.Details"/>), not its properties' setting. Beside
    /// <see cref="FreeEditing"/>, as the settings hand both over and the rows have one place to ask.
    /// </summary>
    public static Services.Settings.Reach ChargesReach { get; set; } = Services.Settings.Reach.Any;

    /// <summary>What the format allows, or null for a stat whose row could not be read.</summary>
    public int? FieldMinimum { get; }
    public int? FieldMaximum { get; }

    /// <summary>
    /// What the number may be. Free editing answers with what the file could hold; held to
    /// the rules it answers with what the property's own sources could have rolled, which
    /// is the narrower and the honest one. A stat with no known range falls back to the
    /// field either way - there is nothing better to say about it.
    /// <para>
    /// Whatever the rules say, the bounds always take in the number the item came with. A
    /// box holds its bounds harder than its content: handed a value outside them it keeps
    /// the bound and writes that back as if it had been typed. So a ring holding 50 charges
    /// from an affix that grants 20 opened already edited, already pending, and one press of
    /// KEEP from losing the thirty the item actually had.
    /// </para>
    /// <para>
    /// Strict editing is there to stop a value being <b>put</b> outside the range, not to
    /// rewrite one that is already there. What is already there is marked instead - see
    /// IsOutsideRoll.
    /// </para>
    /// </summary>
    public int? Minimum => !FreeEditing && Roll is { } roll
        ? Math.Min(Math.Min(roll.Min, _startedAt), _keptAt)
        : FieldMinimum;

    public int? Maximum => !FreeEditing && Roll is { } roll
        ? Math.Max(Math.Max(roll.Max, _startedAt), _keptAt)
        : FieldMaximum;

    /// <summary>The number the item held when the row was built.</summary>
    private readonly int _startedAt;

    /// <summary>
    /// And the number it holds since one was kept on it (<see cref="Kept"/>): the item came with that too, as far as a
    /// mode changed afterwards can tell. A number set in Free and a page gone to Casual is held there, not corrected.
    /// </summary>
    private int _keptAt;

    /// <summary>
    /// Taking a property off the item is a free-editing act, and about the property, not its numbers: a mechanic (melee splash), whose
    /// numbers are fixed, comes off like any other - added by mistake it had no bin, and only UNDO,
    /// which drops every other pending change too, took it back.
    /// </summary>
    public bool CanDelete => FreeEditing && !IsDeleted && !IsSetBonus;

    /// <summary>
    /// Putting one back is a free-editing act too.
    /// <para>
    /// Held to the rules, the only struck-through row is one an affix change took away,
    /// and keeping it would leave a property nothing on the item accounts for - the very
    /// thing the lock is closed against. The change itself is still reversible: UNDO drops
    /// it whole, and the picker can unstage the slot before it is confirmed.
    /// </para>
    /// </summary>
    public bool CanRestore => FreeEditing && IsDeleted;

    public string Limits => Minimum is { } low && Maximum is { } high
        ? $"{low}-{high}"
        : "unbounded";

    /// <summary>
    /// The limit with the field it applies to named. A stat that also names a skill has two
    /// fields, and a bare range sitting near both answers for neither.
    /// </summary>
    public string LimitsText => L($"field range: {Limits}");

    /// <summary>
    /// What the affix that granted this could have rolled, when one did. Not a limit: the
    /// steppers still go to what the file holds, because rolling outside is a thing the
    /// game would not have done rather than a thing the file cannot say.
    /// </summary>
    public RollRange? Roll { get; private set; }

    /// <summary>
    /// Points the row at a different range, for when the item's affixes change under it.
    /// <para>
    /// The range is what the value is held to while editing is strict, and it was fixed
    /// when the row was built - from the item as the <b>file</b> had it. So swapping a
    /// prefix for a higher tier staged a number the old tier's ceiling promptly clipped,
    /// and the change looked as though it had not happened.
    /// </para>
    /// </summary>
    public void Retune(RollRange? roll, RollRange? shown = null)
    {
        Roll = roll;
        Shown = shown;

        OnPropertyChanged(nameof(Roll));
        OnPropertyChanged(nameof(Shown));
        OnPropertyChanged(nameof(HasRoll));
        OnPropertyChanged(nameof(RollText));
        OnPropertyChanged(nameof(IsOutsideRoll));
        OnPropertyChanged(nameof(CanReroll));
        LimitsChanged();
    }

    /// <summary>
    /// The bracket the <b>game</b> paints on this line, which is a wider thing: the union
    /// over every affix the base could have rolled at its item level, not the one that did.
    /// Said beside the affix range where the two differ, so the row can be read against a
    /// tooltip without either number being mistaken for the other.
    /// <para>
    /// Never a limit. Two affixes writing one stat are summed by the file and <b>not</b> by
    /// the game's bracket, so a real value can sit outside it - see
    /// <see cref="AffixRanges.DisplayedFor"/>. <see cref="Roll"/> is what the row holds the
    /// value to.
    /// </para>
    /// </summary>
    public RollRange? Shown { get; private set; }

    /// <summary>The range on the dim line - not on a switch, where "affix 1" says nothing.</summary>
    public bool HasRoll => Roll is not null && CanEdit && !IsToggle;

    public string RollText
    {
        get
        {
            if (Roll is null)
                return "";

            // The mark that would have prefixed this is an icon on the same line now:
            // the words wrapped it onto three and said little the colour did not. Worded short,
            // on the line under the name: "affix 432–580 (summed) · game 10–400".
            string said = L($"affix {Span(Roll.Min, Roll.Max)}") + (Roll.Sources > 1 ? " (summed)" : "");

            return Shown is { } bracket && bracket != Roll
                ? L($"{said} · game {Span(bracket.Min, bracket.Max)}")
                : said;
        }
    }

    /// <summary>
    /// What the item's other lines for this same property hold.
    /// <para>
    /// A property can sit on an item more than once and the game adds them up: a Leaf staff
    /// carries Fire Bolt twice, +2 from the staff's own roll and +3 from the runeword, and
    /// gives +5. The range is the sum of the sources, so the number to hold against it is the
    /// sum of the lines - judged one at a time, both were outside a range they only reach
    /// together.
    /// </para>
    /// </summary>
    private readonly int _alongside;

    /// <summary>Everything this item gives for this property, this line included.</summary>
    public int Total => Current + _alongside;

    /// <summary>A value no affix would have produced, which is worth pointing out.</summary>
    public bool IsOutsideRoll => Roll is not null && !Roll.Holds(Total);

    /// <summary>
    /// Whether to mark what the game would not have rolled. Set from the editor's
    /// settings, so it can be turned off by anyone editing freely.
    /// </summary>
    public static bool FlagIllegitimate { get; set; } = true;

    /// <summary>The concerns, a line each: the "?" badge's tooltip, which said nothing.</summary>
    public string? ConcernsText => Concerns is { Count: > 0 } said ? string.Join(Environment.NewLine, said) : null;

    /// <summary>
    /// What the item's own base and level say this property could not have been. Empty
    /// when nothing we can resolve contradicts it - which is not the same as legitimate,
    /// only as unrefuted.
    /// </summary>
    public IReadOnlyList<string> Concerns
    {
        get
        {
            var found = new List<string>();

            if (IsOutsideRoll && Roll is not null)
                found.Add(L($"Value {Current} outside affix range {Roll}."));

            // Only what the base itself could have rolled is held to the staffmod rules. A
            // staffmod is part of the base's own roll, so it lives in the item's own property
            // list and can never be in the runeword's or the set's - and `IsRuneword` and
            // `IsSetBonus` are read from which list the game put it in, not inferred.
            //
            // Without this, Purity on a Stalagmite flagged its own aura: the base is a staff,
            // so it rolls Sorceress staffmods, and the runeword grants Level 12 Prayer - a
            // Paladin skill. "Prayer is a Paladin skill; this base rolls Sorceress skills" is
            // true of the base and irrelevant to the runeword, and the two staffmods actually
            // on the item (Enchant Fire, Fire Mastery) were both correctly Sorceress and
            // unflagged beside it.
            //
            // And only the staffmod's own stat, +N to one skill: a chance to cast or charges name a skill too,
            // and a Hitpower Succubus Skull's "5% Chance to cast level 4 Frost Nova when struck" - the recipe's
            // own line - was flagged as a Sorceress skill on a Necromancer base.
            if (Skill is { } skill && _staffClass is { Length: > 0 } owner
                && string.Equals(Stat.Stat, StaffMods.Stat, StringComparison.OrdinalIgnoreCase)
                && !IsRuneword && !IsSetBonus)
            {
                // A dagger rolls a list of its own - Poison Strike and the Assassin's melee skills (StaffMods.SkillsFor).
                if (_staffSkills is { } listed)
                {
                    if (!listed.Contains(skill.Id))
                        found.Add(L($"{skill.Name} is not a staffmod this base rolls."));
                }
                else if (skill.CharClass.Length > 0
                    && !skill.CharClass.Equals(owner, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(
                        L($"{skill.Name} is a {SkillNames.ClassNameOf(skill.CharClass)} skill; this base rolls {SkillNames.ClassNameOf(owner)} skills."));
                }

                int needed = StaffMods.LowestItemLevelFor(skill);
                if (_itemLevel > 0 && _itemLevel < needed)
                {
                    found.Add(
                        L($"{skill.Name} needs item level {needed} to roll; this item is {_itemLevel}."));
                }
            }

            return found;
        }
    }

    /// <summary>
    /// A value no affix on this item accounts for.
    /// <para>
    /// Only where there is something to judge against. A stat nothing resolvable grants -
    /// a base item's own property, a runeword, whatever a socketed jewel adds - is not
    /// questionable, it is unaccounted, and saying otherwise would be the editor guessing.
    /// </para>
    /// </summary>
    public bool IsQuestionable => FlagIllegitimate && Concerns.Count > 0;

    [ObservableProperty]
    private string _value;

    [ObservableProperty]
    private string? _error;

    /// <summary>The last value that parsed, to fall back on when the next one does not.</summary>
    private int _lastGood;

    public bool IsDirty { get; private set; }

    /// <summary>
    /// The stat this row stands for, so whoever owns the item can act on it.
    /// </summary>
    public ItemStat Stat => _stat;

    /// <summary>
    /// Held back rather than written through. A staged row changes nothing until it is
    /// applied, which is what lets a panel offer to keep or throw away what was typed.
    /// The old shell edits write-through, so this is off unless asked for.
    /// </summary>
    private readonly bool _staged;

    /// <summary>Marked for removal. Nothing has gone until the row is applied.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(ShowsNumber))]
    [NotifyPropertyChangedFor(nameof(IsCompact))]
    [NotifyPropertyChangedFor(nameof(ShowsNumberLine))]
    [NotifyPropertyChangedFor(nameof(HasRoll))]
    [NotifyPropertyChangedFor(nameof(HasFieldLines))]
    [NotifyPropertyChangedFor(nameof(Detail))]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    [NotifyPropertyChangedFor(nameof(CanRestore))]
    private bool _isDeleted;

    /// <summary>Whether anything about this row differs from the item.</summary>
    public bool IsChanged => IsDeleted
        || Value != _original
        || (HasMaxCharges && _stat.MaxCharges != _originalMaxCharges)
        || (HasSkill && Skill?.Id != _originalSkill)
        || (HasSkillLevel && _stat.SkillLevel != _originalSkillLevel)
        || (HasIndex && _index != _originalIndex);

    /// <summary>
    /// What the row says about this property beyond its name: that the engine no longer reads
    /// it, or that it only puts a line on the tooltip. Both mean the same thing to whoever is
    /// setting the number - it will not do what it appears to do.
    /// </summary>
    public string StatNote => StatCatalogue.NoteOn(Name);

    public bool HasStatNote => StatNote.Length > 0;

    private string _original;
    private int? _originalSkill;
    private int? _originalSkillLevel;
    private int? _originalMaxCharges;

    /// <summary>
    /// The class, tree or element the item came in with. Without it, changing one was a
    /// change the page could not see: no KEEP appeared, and nothing was written.
    /// </summary>
    private int _originalIndex;

    [RelayCommand]
    private void Delete()
    {
        if (CanDelete)
            IsDeleted = true;
    }

    [RelayCommand]
    private void Restore() => IsDeleted = false;

    /// <summary>
    /// Says what removing this would take with it, when the file stores it in a run with
    /// others. See <see cref="ItemStats"/> for why half a run cannot be left behind.
    /// </summary>
    public string DeleteHint => _stat.Id is { } id && ItemStats.IsInRun(id)
        ? L("Remove property") + Environment.NewLine + L("Adjacent run entries go too")
        : L("Remove property");

    /// <summary>
    /// What the row has staged besides its number - the skill, the class, tree or element it names - put on
    /// another stat: the copy the item's summary is drawn from. <see cref="Apply"/>'s writing, less the
    /// number, which the caller has from <see cref="Number"/>.
    /// </summary>
    public void Wear(ItemStat held)
    {
        if (IsDeleted)
            return;

        if (HasSkill && Skill is { } option)
        {
            switch (_skillField)
            {
                case SkillField.Packed: held.SkillId = option.Id; break;
                case SkillField.Param: held.Param = option.Id; break;
            }
        }

        if (HasIndex)
        {
            held.Param = _index;

            if (_skillField == SkillField.Tab)
            {
                held.SkillTab = _index & 0x7;
                held.CharacterClass = (_index >> 3) & 0x1fff;
            }
        }
    }

    /// <summary>
    /// What the row holds is what the item holds now: written by <see cref="Apply"/> and not to be read as
    /// a change any longer. For a host that keeps at once (the Forge), where a number typed is written
    /// without the rows being built again - a rebuild would take the box from under the caret.
    /// </summary>
    public void Kept()
    {
        if (!IsChanged || IsDeleted)
            return;

        _original = Value;
        _originalSkill = Skill?.Id;
        _originalSkillLevel = _stat.SkillLevel;
        _originalMaxCharges = _stat.MaxCharges;
        _originalIndex = _index;
        if (int.TryParse(Value, out int kept))
            _keptAt = kept;

        OnPropertyChanged(nameof(IsChanged));
    }

    /// <summary>Writes what was typed into the item. Only a staged row needs this.</summary>
    public void Apply()
    {
        if (!_staged || IsDeleted)
            return;

        // The number that held last where the box was left mid-number (see OnValueChanged).
        if (int.TryParse(Value, out int parsed) && Error is null)
            _stat.Value = PD2SPCompanion.Shell.Helper.TypedNumber.IsUnfinished(parsed, Minimum, Maximum) ? _lastGood : parsed;

        if (HasSkill && Skill is { } option)
        {
            switch (_skillField)
            {
                case SkillField.Packed: _stat.SkillId = option.Id; break;
                case SkillField.Param: _stat.Param = option.Id; break;
            }
        }

        // The parameter that is not a skill, which is written the same way and for the same
        // reason: every row on this page is staged, so nothing it holds reaches the item
        // until here. Left out, a skill tree picked and kept came back as whatever it was.
        if (HasIndex)
        {
            _stat.Param = _index;

            if (_skillField == SkillField.Tab)
            {
                _stat.SkillTab = _index & 0x7;
                _stat.CharacterClass = (_index >> 3) & 0x1fff;
            }
        }
    }

    /// <summary>
    /// What the item around this property says about it: the class its base rolls skills
    /// from, and the level it rolled at. Both are needed to say a skill was impossible, and
    /// neither belongs to the property itself.
    /// </summary>
    private readonly string? _staffClass;

    /// <summary>The skills the base's staffmods are drawn from where it is not its class's (a dagger's), or null.</summary>
    private readonly IReadOnlySet<int>? _staffSkills;
    private readonly int _itemLevel;

    public StatRowViewModel(ItemStat stat, RollRange? roll = null, bool staged = false,
                            string? staffClass = null, IReadOnlySet<int>? staffSkills = null, int itemLevel = 0,
                            int poisonLength = 0, int alongside = 0,
                            RollRange? shown = null, bool classDrawn = false, RollRange? skillDrawn = null)
    {
        _classDrawn = classDrawn;
        _skillDrawn = skillDrawn;
        Shown = shown;
        _alongside = alongside;
        _poisonLength = poisonLength;
        _staffClass = staffClass;
        _staffSkills = staffSkills;
        _itemLevel = itemLevel;
        _stat = stat;
        _staged = staged;
        _value = stat.Value.ToString();
        _lastGood = stat.Value;
        Roll = roll;

        var range = StatFields.RangeOf(Name);
        FieldMinimum = range?.Min;
        FieldMaximum = range?.Max;

        // Sixteen bits, but not one number: a byte of charges left and a byte of charges
        // held. Offered as one, the box took 65535 and a minus sign, and -20 came back from
        // the game as 236 charges.
        if (StatFields.EncodingOf(Name) == 3)
        {
            FieldMinimum = 0;
            FieldMaximum = 255;
        }

        // Which stats keep a bare skill id in their parameter is a question for the
        // table, not for a list here. descfunc 27 and 28 are the two single-skill forms,
        // and 16 is the "Level [n] [skill] ... When Equipped" one - item_aura and
        // item_skillonequip - which had no picker at all and showed its parameter as a
        // number nobody could read.
        // Asked of the one place that knows, so the audit export and the fields drawn here
        // cannot come to different conclusions about the same property.
        _fixedChoices = null;
        _skillField = StatShapes.Of(stat.Stat) switch
        {
            StatShape.SkillTree => SkillField.Tab,
            StatShape.Class => SkillField.Class,
            StatShape.Element => SkillField.Element,
            StatShape.Monster => SkillField.Monster,
            StatShape.State => SkillField.State,
            StatShape.Chance or StatShape.Charges => SkillField.Packed,
            StatShape.Skill => SkillField.Param,
            _ => SkillField.None,
        };

        _index = stat.Param ?? 0;

        _skillId = _skillField switch
        {
            SkillField.Packed => stat.SkillId,
            SkillField.Param => stat.Param,
            _ => null,
        };

        if (_skillId is int held)
            _skill = SkillNames.Current?.Option(held);

        _original = _value;
        _startedAt = _keptAt = stat.Value;
        _originalSkill = _skillId;
        _originalSkillLevel = stat.SkillLevel;
        _originalMaxCharges = stat.MaxCharges;
        _originalIndex = _index;

        _described = Describe(stat);
    }

    /// <summary>
    /// True while this property has been picked but not kept. It is shown on the list the
    /// way it will read, and the item does not have it until KEEP - the same bargain every
    /// other field on the page makes.
    /// </summary>
    public bool IsAdded { get; init; }

    /// <summary>
    /// True when this property came from corrupting the item. The game paints a red
    /// asterisk beside it; this is what paints it red here.
    /// </summary>
    public bool IsCorrupted { get; private set; }

    /// <summary>
    /// Whether the runeword put this property here rather than the base or an affix.
    /// <para>
    /// Not worked out from the tables but read from the file: the game keeps a runeword's
    /// properties in a property list of their own, after the item's. So this is the game's
    /// own answer to which is which, and it holds even where the base grants the same
    /// property - a Steel scimitar carries maximum damage in both lists.
    /// </para>
    /// </summary>
    public bool IsRuneword { get; private set; }

    /// <summary>A map's alone - see <see cref="StatCatalogue.IsMapOnly"/>.</summary>
    public bool IsMapOnly => StatCatalogue.IsMapOnly(_stat.Stat);

    /// <summary>Whether the set this item belongs to gave this property, not the item.</summary>
    public bool IsSetBonus { get; private set; }

    /// <summary>
    /// What the group this property belongs to is called, on the first of its rows only -
    /// which is where the list draws the line between what the item is and what was added
    /// to it. Empty on every other row.
    /// </summary>
    public string GroupHeading { get; private set; } = "";

    public bool StartsGroup => GroupHeading.Length > 0;

    // ---- the row's optional blocks, built only where one shows -----------------------------------
    //
    // A row is a template of a dozen blocks - a heading, a skill picker, a class box, a colour box,
    // charges, the value controls twice over - and most properties show two of them. Every row built
    // every block and hid the rest, again on each item selected. Each is this row where it shows and
    // null where it does not, so a ContentPresenter builds it only then; it is asked again exactly
    // when the flag it follows says it changed, as the flag's IsVisible was.

    public StatRowViewModel? GroupPart => StartsGroup ? this : null;
    public StatRowViewModel? CompactPart => IsCompact ? this : null;
    public StatRowViewModel? NumberPart => ShowsNumberLine ? this : null;
    public StatRowViewModel? SkillPart => HasSkill ? this : null;
    public StatRowViewModel? IndexPanelPart => IndexInPanel ? this : null;
    public StatRowViewModel? IndexBoxPart => IndexInBox ? this : null;
    public StatRowViewModel? SkillLevelPart => HasSkillLevel ? this : null;
    public StatRowViewModel? ColourPart => IsColour ? this : null;
    public StatRowViewModel? ChargesPart => HasMaxCharges ? this : null;

    private static readonly Dictionary<string, System.ComponentModel.PropertyChangedEventArgs> PartOf = new()
    {
        [nameof(StartsGroup)] = new(nameof(GroupPart)),
        [nameof(IsCompact)] = new(nameof(CompactPart)),
        [nameof(ShowsNumberLine)] = new(nameof(NumberPart)),
        [nameof(HasSkill)] = new(nameof(SkillPart)),
        [nameof(IndexInPanel)] = new(nameof(IndexPanelPart)),
        [nameof(IndexInBox)] = new(nameof(IndexBoxPart)),
        [nameof(HasSkillLevel)] = new(nameof(SkillLevelPart)),
        [nameof(IsColour)] = new(nameof(ColourPart)),
        [nameof(HasMaxCharges)] = new(nameof(ChargesPart)),
    };

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName is { } name && PartOf.TryGetValue(name, out var part))
            base.OnPropertyChanged(part);
    }

    /// <summary>
    /// How many of the set have to be worn for this bonus, as the wiki says it: "(2 items)".
    /// Empty on anything that is not a set bonus.
    /// </summary>
    public string SetPieces { get; private set; } = "";

    /// <summary>
    /// Says a property came from somewhere other than the item's own row: a runeword, or the
    /// rest of a set. Read from the file rather than worked out - the game keeps each in a
    /// property list of its own, after the item's.
    /// </summary>
    public void MarkGroup(bool runeword, bool set, string heading = "", int pieces = 0)
    {
        IsRuneword = runeword;
        IsSetBonus = set;
        GroupHeading = heading;
        SetPieces = set && pieces > 0 ? L($"({pieces} items)") : "";

        OnPropertyChanged(nameof(IsRuneword));
        OnPropertyChanged(nameof(IsSetBonus));
        OnPropertyChanged(nameof(GroupHeading));
        OnPropertyChanged(nameof(StartsGroup));
        OnPropertyChanged(nameof(SetPieces));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(ShowsNumber));
        OnPropertyChanged(nameof(IsCompact));
        OnPropertyChanged(nameof(ShowsNumberLine));
        OnPropertyChanged(nameof(HasRoll));
        OnPropertyChanged(nameof(HasFieldLines));
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(CanDelete));
    }

    /// <summary>What the mark says: which of the two wrote the property, or both.</summary>
    public string CorruptionMark { get; private set; } = "(corrupted)";

    /// <summary>
    /// Says whether a corruption is what wrote this property.
    /// <para>
    /// Settable rather than fixed at construction, because a corruption can be staged onto
    /// a property the item already had - adding to a cold resist it already carries - and
    /// that row is not rebuilt, only renumbered. Left fixed, the mark never appeared on
    /// exactly the properties where a corruption is hardest to spot.
    /// </para>
    /// </summary>
    public void MarkCorrupted(bool corrupted, bool desecrated = false)
    {
        string mark = corrupted && desecrated ? L("(corrupted, desecrated)")
            : desecrated ? "(desecrated)"
            : "(corrupted)";

        if (IsCorrupted == (corrupted || desecrated) && CorruptionMark == mark)
            return;

        IsCorrupted = corrupted || desecrated;
        CorruptionMark = mark;
        OnPropertyChanged(nameof(IsCorrupted));
        OnPropertyChanged(nameof(CorruptionMark));
    }

    /// <summary>True when this stat names a skill that can be changed.</summary>
    public bool HasSkill => _skillField is SkillField.Param or SkillField.Packed && CanEdit;

    /// <summary>
    /// Whether this stat's parameter is an index into something that is not the skill list:
    /// a class's skill tree, a class, or an element.
    /// </summary>
    public bool HasIndex =>
        _skillField is SkillField.Tab or SkillField.Class or SkillField.Element
                    or SkillField.Monster or SkillField.State
        && CanEdit;

    /// <summary>
    /// Whether the list is long enough to want a page and a search rather than a drop box.
    /// Seven classes are a drop box; seven hundred monsters are not.
    /// </summary>
    public bool IndexNeedsPanel => (LongList?.Count ?? IndexChoices.Count) > 30;

    /// <summary>The long list this row picks from - the monsters, the states - or null for a short one.</summary>
    private IReadOnlyList<(int Id, string Name)>? LongList => _skillField switch
    {
        SkillField.Monster => MonsterNames.All,
        SkillField.State => StateNames.All,
        _ => null,
    };

    public bool IndexInBox => HasIndex && !IndexNeedsPanel;
    public bool IndexInPanel => HasIndex && IndexNeedsPanel;

    /// <summary>What the row calls the thing being picked.</summary>
    public string IndexLabel => _skillField switch
    {
        SkillField.Tab => L("Skill tree"),
        SkillField.Class => L("Class"),
        SkillField.Element => L("Element"),
        SkillField.Monster => L("Monster"),
        _ => L("State"),
    };

    /// <summary>The same, as the heading over the list.</summary>
    public string IndexTitle => _skillField switch
    {
        SkillField.Tab => L("Skill trees"),
        SkillField.Class => L("Classes"),
        SkillField.Element => L("Elements"),
        SkillField.Monster => L("Monsters"),
        _ => L("States"),
    };

    /// <summary>
    /// What this parameter could be, worded. Short lists - twenty-one trees, seven classes,
    /// five elements - so they are offered whole, with no search over them.
    /// </summary>
    public IReadOnlyList<IndexChoice> IndexChoices =>
        _skillField is SkillField.Tab or SkillField.Class or SkillField.Element
            ? _fixedChoices ??= MadeChoices()
            : MadeChoices();

    /// <summary>
    /// The short lists as made once for the row: each read made them anew - the box's width reads them, the name shown
    /// reads them - and a new list is a new list in the box. The long ones follow
    /// what is typed and are made at each read.
    /// </summary>
    private IReadOnlyList<IndexChoice>? _fixedChoices;

    private IReadOnlyList<IndexChoice> MadeChoices()
    {
        {
            switch (_skillField)
            {
                case SkillField.Tab:
                    var trees = new List<IndexChoice>();

                    for (int charClass = 0; charClass < SkillTabs.Classes.Count; charClass++)
                    {
                        for (int tab = 0; tab < 3; tab++)
                        {
                            // The parameter is the two fields in one number: the class above
                            // the tree, three bits apart.
                            trees.Add(new IndexChoice(
                                charClass * 8 + tab,
                                $"{SkillTabs.Classes[charClass]} - "
                                + (SkillTabs.Name(charClass, tab) ?? L($"tree {tab + 1}"))));
                        }
                    }

                    return trees;

                case SkillField.Class:
                    return
                    [
                        .. SkillTabs.Classes.Select((name, id) => new IndexChoice(id, name))
                    ];

                case SkillField.Element:
                    return
                    [
                        .. StatText.Elements.Select((name, id) => new IndexChoice(id, name))
                    ];

                case SkillField.Monster:
                    return Narrowed(MonsterNames.All);

                case SkillField.State:
                    return Narrowed(StateNames.All);

                default:
                    return [];
            }
        }
    }

    private int _index;

    /// <summary>
    /// A long list, cut down by what has been typed - by name or by number, the way the
    /// skill list matches. The short ones are offered whole and ignore the search.
    /// </summary>
    private IReadOnlyList<IndexChoice> Narrowed(IReadOnlyList<(int Id, string Name)> all)
    {
        string wanted = SkillSearch.Trim();
        if (_narrowed is { } kept && ReferenceEquals(kept.All, all) && kept.Search == wanted)
            return kept.Choices;

        var whole = WholeOf(all);
        IReadOnlyList<IndexChoice> choices = wanted.Length == 0
            ? whole.Choices
            : [.. all.Select((entry, at) => (entry, at))
                     .Where(pair => pair.entry.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase)
                                    || pair.entry.Id.ToString().StartsWith(wanted, StringComparison.Ordinal))
                     .Select(pair => whole.Choices[pair.at])];

        _narrowed = (all, wanted, choices);
        return choices;
    }

    /// <summary>
    /// The long list as last cut down: made again only when what is typed changes. Made at every read, the panel's
    /// open read it several times over and each keystroke again - 1,240 monsters each time, and the panel was very slow.
    /// </summary>
    private (IReadOnlyList<(int Id, string Name)> All, string Search, IReadOnlyList<IndexChoice> Choices)? _narrowed;

    /// <summary>A long list whole, as choices and by id: made once for every row, while the tables are the same.</summary>
    private sealed record Whole(IReadOnlyList<IndexChoice> Choices, IReadOnlyDictionary<int, IndexChoice> ById);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IReadOnlyList<(int Id, string Name)>, Whole> _wholes = new();

    private static Whole WholeOf(IReadOnlyList<(int Id, string Name)> all) => _wholes.GetValue(all, list =>
    {
        var choices = list.Select(entry => new IndexChoice(entry.Id, $"{entry.Id} - {entry.Name}")).ToList();
        var byId = new Dictionary<int, IndexChoice>();
        foreach (var choice in choices)
            byId.TryAdd(choice.Id, choice);
        return new Whole(choices, byId);
    });

    /// <summary>The choice a number is, whatever is typed: a long list's by id, a short one's from its list.</summary>
    private IndexChoice? ChoiceOf(int id) =>
        LongList is { } all
            ? WholeOf(all).ById.GetValueOrDefault(id)
            : IndexChoices.FirstOrDefault(choice => choice.Id == id);

    public string IndexDisplay =>
        ChoiceOf(_index) is { } held
            ? held.Name
            : L($"parameter {_index}");

    /// <summary>
    /// The one chosen, for a drop box to sit on. Null where the file holds a number the
    /// list has no entry for, which the box shows as empty rather than as something else.
    /// </summary>
    public IndexChoice? ChosenIndex
    {
        get => ChoiceOf(_index);
        set
        {
            if (value is not null && value.Id != _index)
                ChooseIndex(value);
        }
    }

    [RelayCommand]
    private void ChooseIndex(IndexChoice? choice)
    {
        if (choice is null)
            return;

        _index = choice.Id;

        if (!_staged)
        {
            _stat.Param = choice.Id;

            // A skill tree keeps the same number twice: once as the parameter the file
            // writes, and once split into the two fields the wording reads.
            if (_skillField == SkillField.Tab)
            {
                _stat.SkillTab = choice.Id & 0x7;
                _stat.CharacterClass = (choice.Id >> 3) & 0x1fff;
            }
        }

        PickerOpen = false;

        Notify();
        OnPropertyChanged(nameof(ChosenIndex));
        OnPropertyChanged(nameof(IndexDisplay));
        OnPropertyChanged(nameof(InGame));
        OnPropertyChanged(nameof(IsChanged));
    }

    /// <summary>
    /// Whether this property is a charged skill, whose value is two numbers in one: the
    /// charges it has left in the low byte and the charges it holds in the high one.
    /// </summary>
    public bool IsCharged => StatFields.EncodingOf(Name) == 3;

    /// <summary>
    /// Whether the charges it holds can be set. A byte each, so a hundred and one is a
    /// charge count and two hundred and fifty-six is not.
    /// </summary>
    public bool HasMaxCharges => IsCharged && CanEdit;

    /// <summary>The play mode locks the charges a skill holds: the box only shows them.</summary>
    public bool MaxChargesLocked => ChargesReach == Services.Settings.Reach.Locked;

    /// <summary>
    /// The field's whole byte where the mode lets it be any value. Within limits (Casual), what the row's source grants,
    /// as a row's number is held to its <see cref="Roll"/>: Shift + on this box wrote 255 onto a ring granted 20, in a
    /// mode whose rule says within limits. A line no source accounts for has nothing better than the field, as
    /// <see cref="Minimum"/> says. Locked, where it is. Each takes in what the item came with: a value is never
    /// corrected by being read.
    /// </summary>
    public int MaxChargesMinimum => ChargesReach switch
    {
        Services.Settings.Reach.Locked => _originalMaxCharges ?? 0,
        Services.Settings.Reach.Any => 0,
        _ => Roll is { } roll ? Math.Min(roll.Min, _originalMaxCharges ?? 0) : 0,
    };

    public int MaxChargesMaximum => ChargesReach switch
    {
        Services.Settings.Reach.Locked => _originalMaxCharges ?? 0,
        Services.Settings.Reach.Any => 255,
        _ => Roll is { } roll ? Math.Max(roll.Max, _originalMaxCharges ?? 0) : 255,
    };

    public string MaxChargesText
    {
        get => (_stat.MaxCharges ?? 0).ToString();
        set
        {
            // Only on a charged skill - see SkillLevelText: a hidden box's "0" was written into
            // a plain line as a charge count of nought where it had none.
            if (!HasMaxCharges || !int.TryParse(value, out int typed))
            {
                OnPropertyChanged();
                return;
            }

            int held = Math.Clamp(typed, MaxChargesMinimum, MaxChargesMaximum);

            if (_stat.MaxCharges != held)
            {
                _stat.MaxCharges = held;
                Notify();
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(InGame));
            OnPropertyChanged(nameof(CanRaiseMaxCharges));
            OnPropertyChanged(nameof(CanLowerMaxCharges));
        }
    }

    public bool CanRaiseMaxCharges => (_stat.MaxCharges ?? 0) < MaxChargesMaximum;
    public bool CanLowerMaxCharges => (_stat.MaxCharges ?? 0) > MaxChargesMinimum;

    [RelayCommand]
    private void RaiseMaxCharges() =>
        MaxChargesText = Math.Min((_stat.MaxCharges ?? 0) + 1, MaxChargesMaximum).ToString();

    [RelayCommand]
    private void LowerMaxCharges() =>
        MaxChargesText = Math.Max((_stat.MaxCharges ?? 0) - 1, MaxChargesMinimum).ToString();

    /// <summary>
    /// Whether the skill this row names casts at a level of its own.
    /// <para>
    /// A chance-to-cast stat packs three numbers: how often it fires, which skill, and
    /// what level it casts at. Only the first two had anywhere to be edited, so every such
    /// property read "casts level 0" and stayed that way.
    /// </para>
    /// </summary>
    public bool HasSkillLevel => _skillField == SkillField.Packed && CanEdit;

    /// <summary>
    /// Six bits in the parameter, so one to sixty-three - while editing is free.
    /// <para>
    /// Strict editing holds it where it is. The level a property casts at is not rolled: the
    /// affix names it outright, the same way it names the chance, so there is no range to
    /// move inside and nothing to roll for. <c>of Nova</c> is ten percent at level three and
    /// the next tier up is twelve at four; a level three-and-a-half does not exist.
    /// </para>
    /// </summary>
    public int SkillLevelMinimum => FreeEditing ? 1 : SkillLevelHeld;
    public int SkillLevelMaximum => FreeEditing ? 63 : SkillLevelHeld;

    private int SkillLevelHeld => Math.Clamp(_stat.SkillLevel ?? 1, 1, 63);

    public bool CanRaiseSkillLevel => HasSkillLevel && SkillLevelHeld < SkillLevelMaximum;
    public bool CanLowerSkillLevel => HasSkillLevel && SkillLevelHeld > SkillLevelMinimum;

    /// <summary>
    /// What a number does not carry, taken from the line as a change worked it out anew: the level a skill is
    /// cast at, and its charges. A change that puts another affix of the same skill on the item - "16% Chance
    /// to cast level 24 Fire Ball on striking" for the level 18 one, by the Perfect Skull or the picker - finds
    /// this row by its stat and skill and gave it the new number alone: the row kept the old level, and the
    /// item was kept with an affix of one level casting another's, which the legitimacy check says no source
    /// grants (found by 150 rolls of the Forge's roll; the fault was the staging's, of any page).
    /// As the row's own boxes write them: live, with what the item held kept for UNDO.
    /// </summary>
    public void TakePacked(ItemStat worked)
    {
        if (!ReferenceEquals(worked, _stat) && _skillField == SkillField.Packed && worked.SkillLevel is { } level && _stat.SkillLevel != level)
        {
            _stat.SkillLevel = level;
            OnPropertyChanged(nameof(SkillLevelText));
            OnPropertyChanged(nameof(CanRaiseSkillLevel));
            OnPropertyChanged(nameof(CanLowerSkillLevel));
            OnPropertyChanged(nameof(InGame));
        }

        if (!ReferenceEquals(worked, _stat) && IsCharged && worked.MaxCharges is { } charges && _stat.MaxCharges != charges)
        {
            _stat.MaxCharges = charges;
            OnPropertyChanged(nameof(MaxChargesText));
            OnPropertyChanged(nameof(CanRaiseMaxCharges));
            OnPropertyChanged(nameof(CanLowerMaxCharges));
            OnPropertyChanged(nameof(InGame));
        }
    }

    public string SkillLevelText
    {
        get => (_stat.SkillLevel ?? 0).ToString();
        set
        {
            // Only where the row has a level to set. The box is in every row's template, and a
            // refresh made a hidden one on a plain line push its "1" back - written straight into
            // the item's own line, before KEEP: a Small Charm's life read "level 1" after UNDO.
            if (!HasSkillLevel || !int.TryParse(value, out int typed))
            {
                OnPropertyChanged();
                return;
            }

            int held = Math.Clamp(typed, SkillLevelMinimum, SkillLevelMaximum);

            if (_stat.SkillLevel != held)
            {
                _stat.SkillLevel = held;
                Notify();
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(InGame));
            OnPropertyChanged(nameof(CanRaiseSkillLevel));
            OnPropertyChanged(nameof(CanLowerSkillLevel));
        }
    }

    [RelayCommand]
    private void RaiseSkillLevel() =>
        SkillLevelText = Math.Min((_stat.SkillLevel ?? 0) + 1, SkillLevelMaximum).ToString();

    [RelayCommand]
    private void LowerSkillLevel() =>
        SkillLevelText = Math.Max((_stat.SkillLevel ?? 0) - 1, SkillLevelMinimum).ToString();

    /// <summary>
    /// What the number means once it is not the only thing on the row. A stat with a skill
    /// has two fields, and "2" beside a skill name reads as neither one nor the other
    /// until it is told which it is.
    /// <para>
    /// Three different things wear this box. Where the parameter holds the skill, the value
    /// is how many levels of it. Where the skill is packed in beside the value, the value is
    /// the chance it fires - or, for a charged skill, the charges left.
    /// </para>
    /// </summary>
    public string ValueLabel => IsToggle ? L("On") : _skillField switch
    {
        SkillField.Packed => IsCharged ? L("Charges") : L("Chance"),
        SkillField.Param or SkillField.Tab or SkillField.Class or SkillField.Element => L("Levels"),
        SkillField.Monster => L("Chance"),
        _ => L("Value"),
    };

    /// <summary>
    /// A property the file keeps in a single bit: it is on or it is not. A stepper between
    /// nought and one reads as a number with a range, which is two more numbers than there
    /// are. Shown as a switch instead, and the wording says which way it is.
    /// </summary>
    public bool IsToggle => CanEdit && FieldMinimum == 0 && FieldMaximum == 1;

    public bool ShowsNumber => CanEdit && !IsToggle && !IsColour;

    /// <summary>
    /// A row whose only field is its number: its steppers sit on the name's line, and the row is
    /// two lines, not four. A per-level row too, its "1.25 per level" on the
    /// dim line. A skill, a tree, a skill level or charges keep the field lines beneath, where each
    /// is named.
    /// </summary>
    public bool IsCompact => ShowsNumber && _skillField == SkillField.None && !HasIndex && !HasMaxCharges;

    /// <summary>A switch's tick, on the name's line where a number's steppers would be.</summary>
    public bool ShowsToggle => IsToggle;

    public string ToggleHint => IsOn ? L("On") : L("Off");

    /// <summary>The number on a field line of its own, under the name: every row that is not compact.</summary>
    public bool ShowsNumberLine => ShowsNumber && !IsCompact;

    /// <summary>A range in words: "10–20", or "15" where the two ends meet.</summary>
    private static string Span(int min, int max) => min == max ? $"{min}" : $"{min}–{max}";

    /// <summary>
    /// A dye: its number is a colour, so it is picked by name - "Crystal Red", not 10.
    /// The 21 colours of colors.txt; the five bits the file keeps would hold 22-31 too,
    /// which name no colour and show as an empty box.
    /// </summary>
    public bool IsColour => CanEdit && string.Equals(Name, Dyes.Stat, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A drop box's width: its longest choice, read at the body size, and the chevron - right-aligned,
    /// not stretched across half the row. Kept between the steppers' width and 300.
    /// </summary>
    public double IndexBoxWidth => BoxWidth(IndexChoices);

    public double ColourBoxWidth => BoxWidth(ColourChoices);

    private static double BoxWidth(IReadOnlyList<IndexChoice> choices) =>
        Math.Clamp(choices.Select(choice => choice.Name.Length).DefaultIfEmpty(0).Max() * 7.5 + 44, 160, 300);

    public IReadOnlyList<IndexChoice> ColourChoices =>
        IsColour ? _colours ??= [.. Dyes.Colours.Select(each => new IndexChoice(each.Dye, each.Name))] : [];

    /// <summary>The dyes, made once for the row (see <see cref="_fixedChoices"/>).</summary>
    private IReadOnlyList<IndexChoice>? _colours;

    public IndexChoice? ChosenColour
    {
        get => ColourChoices.FirstOrDefault(choice => choice.Id == Current);
        set
        {
            if (value is not null && value.Id != Current)
                Value = value.Id.ToString();
        }
    }

    public bool IsOn
    {
        get => Current != 0;
        set => Value = value ? "1" : "0";
    }

    [RelayCommand]
    private void ToggleOn()
    {
        IsOn = !IsOn;
        OnPropertyChanged(nameof(ToggleHint));
    }

    /// <summary>
    /// The skills worth offering, in sections. A number is not something anyone can set.
    /// <para>
    /// A stat worded "Level [n] [skill] ... When Equipped" - descfunc 16, which is
    /// <c>item_aura</c> and <c>item_skillonequip</c> - grants an aura, so only auras are
    /// offered there. Everything else gets the lot.
    /// </para>
    /// </summary>
    private IReadOnlyList<SkillChoiceGroup> AllSkillGroups
    {
        get
        {
            // Rebuilt when the row points somewhere else, so the marked entry follows the
            // skill rather than staying on whichever one was current when the list was
            // first asked for.
            if (_skillGroups is not null && _markedSkill == Skill?.Id)
                return _skillGroups;

            _markedSkill = Skill?.Id;

            return _skillGroups = SkillChoiceGroup.From(
                (StatText.FunctionOf(Name) == 16
                    ? SkillNames.Current?.Auras
                    : SkillNames.Current?.Groups) ?? [],
                Choose,
                current: _markedSkill);
        }
    }

    private IReadOnlyList<SkillChoiceGroup>? _skillGroups;
    private int? _markedSkill;

    /// <summary>
    /// What the picker shows. Seven hundred skills is too many to scroll, so a section
    /// with nothing matching drops out entirely rather than sitting there empty.
    /// </summary>
    public IReadOnlyList<SkillChoiceGroup> SkillGroups
    {
        get
        {
            string search = SkillSearch.Trim();
            if (search.Length == 0)
                return AllSkillGroups;

            // Filtered sections are built fresh each time, so they get the same action.
            // A search opens what it finds. Leaving the sections folded would mean
            // typing a name and then still hunting for which class it was under.
            return
            [
                .. AllSkillGroups
                    .Select(group => new SkillChoiceGroup(
                        group.Name,
                        [.. group.Skills.Where(skill => skill.Matches(search))],
                        isOpen: true))
                    .Where(group => group.Skills.Count > 0),
            ];
        }
    }

    [ObservableProperty] private string _skillSearch = "";

    /// <summary>Whether the picker is showing.</summary>
    [ObservableProperty] private bool _pickerOpen;

    [RelayCommand]
    private void TogglePicker() => PickerOpen = !PickerOpen;

    public bool NothingMatches =>
        PicksSkill ? SkillGroups.Count == 0 : IndexNeedsPanel && IndexChoices.Count == 0;

    /// <summary>Which of the two lists the picker shows for this row.</summary>
    public bool PicksSkill => _skillField is SkillField.Param or SkillField.Packed;
    public bool PicksIndex => !PicksSkill;

    /// <summary>Whether the panel that is open offers a search over what it lists.</summary>
    public bool PickerSearches => PicksSkill || IndexNeedsPanel;

    /// <summary>The heading over whichever list it is.</summary>
    public string PickerTitle => PicksSkill ? L("Skills") : IndexTitle;

    /// <summary>
    /// Taking one. The picker closes behind it unless it has been pinned, since choosing
    /// is usually the end of the errand.
    /// </summary>
    private void Choose(SkillOption option)
    {
        Skill = option;
        PickerOpen = false;
    }

    partial void OnSkillSearchChanged(string value)
    {
        OnPropertyChanged(nameof(SkillGroups));
        OnPropertyChanged(nameof(IndexChoices));
        OnPropertyChanged(nameof(NothingMatches));
    }

    /// <summary>
    /// What the picker's button says, worded exactly as the list words it, id first, so what
    /// the button says and what you picked are plainly the same thing.
    /// </summary>
    public string SkillDisplay => Skill is { } option
        ? $"{option.Id} - {option.Display}"
        : _skillId is int id ? L($"skill {id}") : L("Choose skill");


    /// <summary>
    /// The skill this stat names. Null when the file holds a number no skill has, which is
    /// worth leaving visible rather than snapping to something that was never there.
    /// </summary>
    [ObservableProperty] private SkillOption? _skill;

    /// <summary>What the file holds, when no skill answers to it.</summary>
    public string UnknownSkill => Skill is null && _skillId is int id ? L($"skill {id}") : "";

    /// <summary>The line under an unknown skill: what the file holds that nothing answers to.</summary>
    public string UnknownSkillNote => L($"the file holds {UnknownSkill}, which no skill answers to");

    /// <summary>
    /// The file holds a number no skill answers to. Not the same as holding nothing yet:
    /// a property just picked from the list has no skill on purpose, and saying it holds
    /// one that cannot be found is a complaint about a state nobody is in.
    /// </summary>
    public bool SkillIsUnknown => Skill is null && HasSkill && _skillId is not null;

    private int? _skillId;

    partial void OnSkillChanged(SkillOption? value)
    {
        if (value is null)
            return;

        _skillId = value.Id;

        if (!_staged)
        {
            switch (_skillField)
            {
                case SkillField.Packed:
                    _stat.SkillId = value.Id;
                    break;
                case SkillField.Param:
                    _stat.Param = value.Id;
                    break;
            }
        }

        OnPropertyChanged(nameof(IsChanged));

        OnPropertyChanged(nameof(UnknownSkill));
        OnPropertyChanged(nameof(UnknownSkillNote));
        OnPropertyChanged(nameof(SkillIsUnknown));
        OnPropertyChanged(nameof(SkillDisplay));
        OnPropertyChanged(nameof(InGame));

        // Picking a skill of the wrong class, or one this item is too low to have rolled,
        // is a concern about the skill rather than about the number beside it.
        OnPropertyChanged(nameof(Concerns));
        OnPropertyChanged(nameof(ConcernsText));
        OnPropertyChanged(nameof(IsQuestionable));
    }

    /// <summary>
    /// What the numbers beside a stat actually mean. A skill or a class stored as an index
    /// tells you nothing on its own, so it is named wherever the tables can name it.
    /// </summary>
    private static string Describe(ItemStat stat)
    {
        var parts = new List<string> { L($"stat {stat.Id}") };

        // A stat the game fixes has nothing else worth saying about it. Its skill and
        // level are part of the mechanic, not choices, and listing them reads as though
        // they were.
        if (Mechanics.Contains(stat.Stat ?? ""))
            return parts[0];

        // The skill itself is not repeated here: it has a box of its own, and naming it
        // twice with one of them editable is a way to end up reading the stale one.
        if (stat.SkillLevel is int level && level > 0)
            parts.Add(L($"level {level}"));

        if (stat.MaxCharges is int charges && charges > 0)
            parts.Add(L($"{charges} charges"));

        // A skill-tab stat names three things: the class, which of its three trees, and
        // how many levels - the last being the value, not part of this.
        if (stat.SkillTab is int tab)
        {
            parts.Add(ClassName(stat.CharacterClass ?? 0));
            parts.Add(TabName(stat.CharacterClass ?? 0, tab));
        }

        // Everything else keeps its raw param, which means different things per stat.
        if (stat.SkillId is null && stat.Param is int param
            && stat.Stat is not ("item_singleskill" or "item_nonclassskill" or "item_elemskill"))
        {
            parts.Add(Param(stat, param));
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// A bare param, read according to the stat carrying it. Only the cases the tables
    /// settle are named; the rest say what they are rather than guessing.
    /// </summary>
    private static string Param(ItemStat stat, int param) => stat.Stat switch
    {
        // The class whose skills are raised, stored as the class id.
        "item_addclassskills" => ClassName(param),

        // A single skill, stored as its id in skills.txt.
        "item_singleskill" or "item_nonclassskill" or "item_elemskill"
            => SkillNames.Current?.Describe(param) ?? L($"skill {param}"),

        _ => L($"param {param}"),
    };

    /// <summary>
    /// The tab's name as the game words it, from the class's own three keys in
    /// charstats.txt rather than from a list here that could drift out of step.
    /// </summary>
    private static string TabName(int classId, int tab) =>
        SkillTabs.Name(classId, tab) ?? L($"tree {tab}");

    private static string ClassName(int id) =>
        CharacterClasses.NameOf(id) ?? L($"class {id}");

    public bool CanRaise => CanEdit && (Maximum is null || Current < Maximum);
    public bool CanLower => CanEdit && (Minimum is null || Current > Minimum);

    /// <summary>
    /// Said in the tooltip rather than on the row. With the controls gone there is nothing
    /// left to explain, and a sentence under every one of these crowds out the stats that
    /// can be changed.
    /// </summary>
    public string MechanicNote =>
        L("Fixed by game. No editable fields.");

    /// <summary>
    /// The row's tooltip, or null for a row that has nothing to say. Null rather than an
    /// empty string, and certainly rather than a hidden child: a tooltip whose content is
    /// merely invisible still opens, as an empty frame following the pointer.
    /// </summary>
    public string? RowTooltip => IsMechanic ? MechanicNote : null;

    /// <summary>
    /// What the picker's button says when hovered. Says the field's own limit the way the
    /// steppers below it do, so both rows of a two-field stat answer the same question the
    /// same way.
    /// <para>
    /// The two shapes hold the id differently: a proc packs it into ten bits above the
    /// skill level, while a plain skill stat keeps the whole parameter for it. Ten bits is
    /// the narrower of the two and the one worth stating - a skill id past it would be
    /// silently masked away on write.
    /// </para>
    /// </summary>
    public string SkillPickHint =>
        SkillFieldMaximum is { } most
            ? L("Choose skill")
              + Environment.NewLine
              + L($"field range: 0-{most}")
            : L("Choose skill");

    /// <summary>
    /// Ten bits when the id is packed above a skill level, and the whole parameter when the
    /// stat has nothing else to store there - the second read from <c>Save Param Bits</c>
    /// rather than assumed, since it is the stat that decides how much room it has.
    /// </summary>
    private int? SkillFieldMaximum => _skillField switch
    {
        SkillField.Packed => 1023,
        SkillField.Param => StatFields.ParamMaximumOf(Name),
        _ => null,
    };

    private int Current => int.TryParse(Value, out int parsed) ? parsed : _lastGood;

    /// <summary>
    /// Whether rows are written with this one - Enhanced Damage's other half, a flat damage's copies, the four
    /// resistances of an all-resistances affix, the four attributes - which take its number when asked
    /// (<see cref="AskFellows"/>): the row shows its link button. Set by the page.
    /// </summary>
    public bool HasFellows { get; set; }

    /// <summary>The rows rolled with this one are to take its number (VP_ViewModel_Inv_R.SyncFellows).</summary>
    public event Action<StatRowViewModel>? FellowsAsked;

    /// <summary>
    /// The rows written with this one take its number - its link button, or Alt + Enter in its box. Syncing is
    /// no longer forced: nothing follows by itself, every row is edited on its own.
    /// </summary>
    [RelayCommand]
    private void AskFellows() => FellowsAsked?.Invoke(this);

    /// <summary>Takes the number given, held to the row's own limits: how a row takes the number of one written with it.</summary>
    public void MoveTo(int number) =>
        Value = PD2SPCompanion.Shell.Helper.TypedNumber.Held(number, Minimum, Maximum).ToString();

    /// <summary>
    /// The number the row holds, for working the item out before KEEP: what is typed, or - while a number
    /// is still being typed and not yet inside the range - the last one that was. Null for an empty box.
    /// </summary>
    public int? Number =>
        !int.TryParse(Value, out int parsed) ? null
        : PD2SPCompanion.Shell.Helper.TypedNumber.IsUnfinished(parsed, Minimum, Maximum) ? _lastGood
        : parsed;

    /// <summary>
    /// Puts a corrected number back, on screen as well as in the row.
    /// <para>
    /// Setting the property is not enough on its own. This runs inside the binding's own
    /// write to the source, and a change raised there is dropped - the binding takes it for
    /// the echo of what it is already writing. So the number lands, the box goes on showing
    /// what was typed, and the two disagree until something else makes the box read again.
    /// Raising it once the write has finished is what the box actually sees.
    /// </para>
    /// </summary>
    private void Correct(int number)
    {
        Value = number.ToString();
        Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(Value)));
    }

    /// <summary>
    /// Rolls the stat again, the way the game would have: somewhere in what its affix can
    /// produce. Only offered when an affix accounts for the stat - rolling inside what the
    /// file merely permits would produce numbers no item ever had.
    /// </summary>
    public bool CanReroll => CanEdit && Roll is not null && Roll.Min < Roll.Max;

    /// <summary>
    /// Said when free editing is turned on or off: the limits are computed from it, and
    /// the controls that read them have no other way of hearing about it.
    /// </summary>
    public void LimitsChanged()
    {
        OnPropertyChanged(nameof(Minimum));
        OnPropertyChanged(nameof(Maximum));
        OnPropertyChanged(nameof(CanRaise));
        OnPropertyChanged(nameof(CanLower));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanRestore));
        OnPropertyChanged(nameof(Limits));
        OnPropertyChanged(nameof(LimitsText));
        OnPropertyChanged(nameof(LowerHint));
        OnPropertyChanged(nameof(RaiseHint));
        OnPropertyChanged(nameof(SkillLevelMinimum));
        OnPropertyChanged(nameof(SkillLevelMaximum));
        OnPropertyChanged(nameof(CanRaiseSkillLevel));
        OnPropertyChanged(nameof(CanLowerSkillLevel));
        OnPropertyChanged(nameof(MaxChargesLocked));
        OnPropertyChanged(nameof(MaxChargesMinimum));
        OnPropertyChanged(nameof(MaxChargesMaximum));
        OnPropertyChanged(nameof(CanRaiseMaxCharges));
        OnPropertyChanged(nameof(CanLowerMaxCharges));
    }

    public string RerollHint => IsMechanic
        ? MechanicNote
        : Roll is null
        ? L("No affix range")
        : Roll.Min == Roll.Max
            ? L($"Fixed affix value: {Roll.Min}")
            : L($"Reroll within affix range: {Roll}");

    [RelayCommand]
    private void Reroll()
    {
        if (Roll is not { } roll)
            return;

        Value = Random.Shared.Next(roll.Min, roll.Max + 1).ToString();
    }

    /// <summary>
    /// Straight to the end of whatever bounds the row has - its roll range, or the field's
    /// own limit where the row is being edited freely. <see cref="Move"/> clamps to the
    /// first and the value setter to the second, so an outsized step lands on the right
    /// one without either being restated here.
    /// </summary>
    public void JumpValue(bool up) => Move(up ? Far : -Far);

    /// <summary>
    /// The steppers' tooltips: Ctrl ten at a time; Shift the affix range's end - or, where no affix accounts for the
    /// stat, the value's own end, which is where it goes (<see cref="JumpToRoll"/>); Ctrl + Shift the field's end,
    /// said only where free editing lets it be reached.
    /// </summary>
    public string LowerHint => Hint(up: false);

    public string RaiseHint => Hint(up: true);

    private string Hint(bool up)
    {
        string end = Roll is not null
            ? up ? L("Shift + LMB: affix max") : L("Shift + LMB: affix min")
            : up ? L("Shift + LMB: max value") : L("Shift + LMB: min value");
        string said = L($"Ctrl + LMB: {LeapSize} at a time") + Environment.NewLine + end;
        return FreeEditing
            ? said + Environment.NewLine + (up ? L("Ctrl + Shift + LMB: field max") : L("Ctrl + Shift + LMB: field min"))
            : said;
    }

    /// <summary>A larger step - Ctrl on a stepper, as the Basic section's boxes take it.</summary>
    public void Leap(bool up) => Move(up ? LeapSize : -LeapSize);

    private const int LeapSize = 10;

    /// <summary>The link button's tooltip.</summary>
    public string FellowsHint => ShortcutText.With(L("Same number on its group"), L("Alt + Enter"));

    /// <summary>
    /// To the end of the affix range - ctrl on a stepper. Where no affix accounts for the stat
    /// there is no such range, and it goes as far as <see cref="JumpValue"/> would. Set through
    /// the value, so the play mode's bounds still hold.
    /// </summary>
    public void JumpToRoll(bool up)
    {
        if (Roll is not { } roll)
        {
            JumpValue(up);
            return;
        }

        Value = (up ? roll.Max : roll.Min).ToString();
    }

    /// <summary>The two fields a packed property carries beside its number.</summary>
    public void JumpSkillLevel(bool up) =>
        SkillLevelText = (up ? SkillLevelMaximum : SkillLevelMinimum).ToString();

    public void JumpMaxCharges(bool up) =>
        MaxChargesText = (up ? MaxChargesMaximum : MaxChargesMinimum).ToString();

    /// <summary>A larger step on those two - Ctrl on their steppers, as on a property's value.</summary>
    public void LeapSkillLevel(bool up) =>
        SkillLevelText = Math.Clamp(SkillLevelHeld + (up ? LeapSize : -LeapSize), SkillLevelMinimum, SkillLevelMaximum).ToString();

    public void LeapMaxCharges(bool up) =>
        MaxChargesText = Math.Clamp((_stat.MaxCharges ?? 0) + (up ? LeapSize : -LeapSize), MaxChargesMinimum, MaxChargesMaximum).ToString();

    private const int Far = 1_000_000;

    [RelayCommand]
    private void Raise() => Move(1);

    [RelayCommand]
    private void Lower() => Move(-1);

    /// <summary>
    /// Moves the value, stopping at what the format allows rather than running past it.
    /// </summary>
    public void Move(int by)
    {
        int wanted = Current + by;

        if (Maximum is { } high)
            wanted = Math.Min(wanted, high);
        if (Minimum is { } low)
            wanted = Math.Max(wanted, low);

        Value = wanted.ToString();
    }

    /// <summary>
    /// Keeps the typed value inside what the field can hold.
    /// <para>
    /// A number past the limit is pushed back to it as it is typed rather than reported.
    /// The limit comes from the format - <c>Save Bits</c> in <c>itemstatcost</c> - so it is
    /// not a mistake to explain, it is simply where the field stops.
    /// </para>
    /// </summary>
    partial void OnValueChanged(string value)
    {
        // A box being emptied on the way to a new number is not yet a number, and
        // putting one back under the caret makes it impossible to retype.
        if (value.Length == 0 || value == "-")
        {
            Notify();
            return;
        }

        if (!int.TryParse(value, out int parsed))
        {
            // Nothing to clamp towards, so the last value that parsed stands.
            Correct(_lastGood);
            return;
        }

        // A number more digits could still bring into the range is still being typed (Helper/
        // TypedNumber): put back to the minimum here, a range from 12 could not be typed into - the box
        // showed 12 again after every first digit. The last number that held
        // stands until this one is finished; the box holds it to the range when it is left.
        if (PD2SPCompanion.Shell.Helper.TypedNumber.IsUnfinished(parsed, Minimum, Maximum))
        {
            Notify();
            return;
        }

        int held = PD2SPCompanion.Shell.Helper.TypedNumber.Held(parsed, Minimum, Maximum);

        if (held != parsed)
        {
            // Re-enters here with a number that is in range, which is where the rest of
            // this runs.
            Correct(held);
            return;
        }

        try
        {
            StatFields.Validate(Name, parsed);
        }
        catch (Exception ex)
        {
            Log.Caught(ex);
            // Only reachable for a stat whose limits could not be read, so there was
            // nothing to clamp against.
            Error = ex.Message.Split(" (Parameter")[0];
            Notify();
            return;
        }

        Error = null;
        _lastGood = parsed;
        IsDirty = parsed != _stat.Value;

        if (!_staged)
            _stat.Value = parsed;

        Notify();
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(CanRaise));
        OnPropertyChanged(nameof(CanLower));
        OnPropertyChanged(nameof(InGame));
        OnPropertyChanged(nameof(HasInGame));
        OnPropertyChanged(nameof(IsOutsideRoll));
        OnPropertyChanged(nameof(RollText));
        OnPropertyChanged(nameof(LowerHint));
        OnPropertyChanged(nameof(RaiseHint));
        OnPropertyChanged(nameof(Concerns));
        OnPropertyChanged(nameof(ConcernsText));
        OnPropertyChanged(nameof(IsQuestionable));
        OnPropertyChanged(nameof(IsChanged));
        OnPropertyChanged(nameof(PerLevelNote));
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(CanRaiseMaxCharges));
        OnPropertyChanged(nameof(CanLowerMaxCharges));
        OnPropertyChanged(nameof(ChosenColour));
    }

    /// <summary>
    /// What one character level of this property is worth, for the stats that grow with
    /// the character. The file keeps eighths, so a stored 39 is a shade under five a
    /// level - and the box shows 39, which says nothing on its own.
    /// </summary>
    public bool IsPerLevel => StatText.PerLevelShift(Name) > 0;

    public string PerLevelNote
    {
        get
        {
            int shift = StatText.PerLevelShift(Name);

            return shift <= 0
                ? ""
                : L($"{Current / (double)(1 << shift):0.##} per level");
        }
    }

    /// <summary>True when the typed value is usable; a save must not proceed otherwise.</summary>
    public bool IsValid => Error is null;
}
