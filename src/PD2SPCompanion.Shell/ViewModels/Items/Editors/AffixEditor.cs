using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using D2SLib.Model.Save;
using CommunityToolkit.Mvvm.Input;
using PD2SPCompanion.Shell.Imaging;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Core.Graphics;
using System.Collections.ObjectModel;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// The item panel's Affixes section: the affixes an item names, its prefix and suffix slots, the
/// picker that fills them, and rerolling them. What it settles is staged as affix ids in the panel's
/// <see cref="ItemDraft"/>; the properties the affixes grant go through the rows.
/// </summary>
public sealed partial class AffixEditor : ViewModelBase
{
    private readonly IItemPanel _panel;

    public AffixEditor(IItemPanel panel) => _panel = panel;

    /// <summary>What the play mode allows, for the section's own bindings.</summary>
    public Services.Settings.PlayRules Rules => _panel.Rules;

    /// <summary>
    /// The affixes an item names, for a magic or rare one. Read-only for now: the point of
    /// showing them is that the numbers in the file mean something, and changing one has to
    /// wait until a property can be added to match it.
    /// </summary>
    public ObservableCollection<AffixName> Names { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAffixPairs))]
    private bool _affixesOpen = PD2SPCompanion.Shell.Services.Settings.FoldMemory.IsOpen(nameof(AffixesOpen));

    partial void OnAffixesOpenChanged(bool value) => PD2SPCompanion.Shell.Services.Settings.FoldMemory.Keep(nameof(AffixesOpen), value);

    [ObservableProperty] private bool _isAffixPickerOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PoolRowHeight))]
    private bool _isAffixPoolOpen = PD2SPCompanion.Shell.Services.Settings.FoldMemory.IsOpen(nameof(IsAffixPoolOpen));

    partial void OnIsAffixPoolOpenChanged(bool value) => PD2SPCompanion.Shell.Services.Settings.FoldMemory.Keep(nameof(IsAffixPoolOpen), value);

    public GridLength PoolRowHeight =>
        IsAffixPoolOpen ? new GridLength(2, GridUnitType.Star) : GridLength.Auto;

    /// <summary>What the pool is a pool of, which is the side being filled.</summary>
    public string AffixPoolHeader => AffixIsPrefix ? L("Prefixes") : L("Suffixes");

    [RelayCommand]
    private void ToggleAffixPool()
    {
        _panel.Press();
        IsAffixPoolOpen = !IsAffixPoolOpen;
    }

    /// <summary>Which side of which slot the list below is offering affixes for.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AffixChoices))]
    [NotifyPropertyChangedFor(nameof(NoAffixChoices))]
    [NotifyPropertyChangedFor(nameof(AffixSideName))]
    [NotifyPropertyChangedFor(nameof(AffixPoolHeader))]
    [NotifyPropertyChangedFor(nameof(StagedPairs))]
    [NotifyPropertyChangedFor(nameof(ChosenAffix))]
    [NotifyPropertyChangedFor(nameof(HasChosenAffix))]
    [NotifyPropertyChangedFor(nameof(NoChosenAffix))]
    [NotifyPropertyChangedFor(nameof(ChosenAffixName))]
    [NotifyPropertyChangedFor(nameof(ChosenAffixHeading))]
    private int _affixSlot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AffixChoices))]
    [NotifyPropertyChangedFor(nameof(NoAffixChoices))]
    [NotifyPropertyChangedFor(nameof(AffixSideName))]
    [NotifyPropertyChangedFor(nameof(AffixPoolHeader))]
    [NotifyPropertyChangedFor(nameof(StagedPairs))]
    [NotifyPropertyChangedFor(nameof(ChosenAffix))]
    [NotifyPropertyChangedFor(nameof(HasChosenAffix))]
    [NotifyPropertyChangedFor(nameof(NoChosenAffix))]
    [NotifyPropertyChangedFor(nameof(ChosenAffixName))]
    [NotifyPropertyChangedFor(nameof(ChosenAffixHeading))]
    private bool _affixIsPrefix = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AffixChoices))]
    [NotifyPropertyChangedFor(nameof(NoAffixChoices))]
    [NotifyPropertyChangedFor(nameof(NoChoicesNote))]
    private string _affixSearch = "";

    public string AffixSideName =>
        $"{(AffixIsPrefix ? "Prefix" : "Suffix")} {AffixSlot + 1}";

    /// <summary>
    /// The pick about to be written, by slot and side - held only from the click to <see cref="Apply"/>, which writes
    /// it at once. An id of zero empties the slot.
    /// </summary>
    private readonly Dictionary<(int Slot, bool Prefix), MagicAffixes.AffixPick> _affixPicks = [];

    /// <summary>
    /// The pool for the chosen slot, read against the item as the staged picks would leave
    /// it - so a group spent by a pick made a moment ago is already spent here.
    /// <para>
    /// <b>The list the picker last showed stands while the picker is shut, and the same rows are the same
    /// list</b>. Every read made a new list, and a new list is every row built again: so the rows
    /// were built on each item selected, seen or not, and again as the picker closed - 95 ms of fading out
    /// behind a frozen window (a trace), the same as opening it. Shut, nobody reads the list, so it is not
    /// worked out; open, a change that leaves the rows as they were leaves the list the view holds alone.
    /// </para>
    /// </summary>
    public IReadOnlyList<AffixChoiceRow> AffixChoices
    {
        get
        {
            if (!IsAffixPickerOpen)
                return _choices;

            var now = Choices();
            if (!now.SequenceEqual(_choices))
                _choices = now;

            _choicesFresh = true;
            return _choices;
        }
    }

    /// <summary>The rows the picker last showed.</summary>
    private IReadOnlyList<AffixChoiceRow> _choices = [];

    /// <summary>
    /// The rows worked out since the list was last said to have changed: what asks after it straight after (whether
    /// it is empty) takes the same rows instead of working them out again - twice for every key typed in the
    /// picker's search.
    /// </summary>
    private bool _choicesFresh;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AffixChoices))
            _choicesFresh = false;
        base.OnPropertyChanged(e);
    }

    private IReadOnlyList<AffixChoiceRow> Choices()
    {
        if (_panel.Selected is not { } tile)
            return [];

        var shadow = _panel.Staged(tile.Item);
        int held = MagicAffixes.At(shadow, AffixSlot, AffixIsPrefix)?.Id ?? 0;

        // With the groups another slot holds, greyed and said (AffixChoiceRow.IsTaken): hidden, a search for
        // what the item already carries found nothing and said nothing of why.
        var all = MagicAffixes.Available(shadow, AffixIsPrefix, AffixSlot, taken: true);
        var open = Matching(all);
        var taken = MagicAffixes.GroupsTaken(shadow, AffixIsPrefix, AffixSlot);

        // Rows that read the same are told apart (AffixChoiceRow.Telling) - across the whole list, so a search that
        // leaves one of them still says which it is.
        var twins = all.GroupBy(option => option.Readable).Where(same => same.Count() > 1)
            .SelectMany(same => same).Select(option => option.Id).ToHashSet();

        string Telling(MagicAffixes.AffixOption option)
        {
            if (!twins.Contains(option.Id))
                return "";

            // The affix level, the table's own: two such rows can both be in reach at every item level of the base
            // (a Vortex Shield rolls at 71 or more) and ask the same of the wearer, and differ in nothing else said.
            int from = option.Level, to = option.MaxLevel;
            var said = new List<string>
            {
                to > 0 && to < 99 ? L($"affix level {from}-{to}") : L($"affix level {from}"),
            };
            if (option.RequiredLevel > 0)
                said.Add(L($"Require level {option.RequiredLevel}"));
            if (option.RareOnly)
                said.Add(L("rare only"));
            return string.Join(" · ", said);
        }

        // What the item rolls affixes at, which is below its item level by half the
        // base's own level. See MagicAffixes.AffixLevel.
        int reach = MagicAffixes.AffixLevel(shadow);

        // As the PD2 wiki lists them, where a +10 fcr and a +20 fcr lie together rather than across the long list: by the
        // table's group - the stat an affix gives, every tier of it alike - then in the table's own order,
        // which rises with level and number. A row of no group comes last.
        return
        [
            .. open.OrderBy(option => int.TryParse(option.Group, out int group) ? group : int.MaxValue)
                .ThenBy(option => option.Id)
                .Select(option => new AffixChoiceRow(
                option, option.Id == held, option.Level > reach,
                MagicAffixes.ItemLevelFor(shadow, option.Level),
                IsTaken: option.Group.Length > 0 && taken.Contains(option.Group), Telling: Telling(option)))
        ];
    }

    /// <summary>The options the search asks for: by the affix's name or what it gives.</summary>
    private IReadOnlyList<MagicAffixes.AffixOption> Matching(IReadOnlyList<MagicAffixes.AffixOption> options) =>
        AffixSearch.Trim() is { Length: > 0 } hunt
            ? [.. options.Where(option => option.Name.Contains(hunt, StringComparison.OrdinalIgnoreCase)
                                          || option.Readable.Contains(hunt, StringComparison.OrdinalIgnoreCase))]
            : options;

    /// <summary>
    /// What the empty list says: nothing found here - and, where the search finds something on the other side, that it
    /// is there: a user may not know where the stat sought is, and may end up typing it several times. The search
    /// stays as the slot or side changes, so a click on the other side shows it.
    /// </summary>
    public string NoChoicesNote
    {
        get
        {
            if (_panel.Selected is not { } tile || AffixSearch.Trim().Length == 0)
                return L("No match.");

            var shadow = _panel.Staged(tile.Item);
            bool other = Matching(MagicAffixes.Available(shadow, !AffixIsPrefix, AffixSlot, taken: true)).Any();
            return other
                ? AffixIsPrefix ? L("No prefix matches. Suffixes do.") : L("No suffix matches. Prefixes do.")
                : L("No match.");
        }
    }

    /// <summary>
    /// Nothing to offer for this slot. Worth saying out loud, because the commonest reason
    /// is not the obvious one: an item holds one affix per group, so searching for an affix
    /// it already carries finds nothing - every tier of Shimmering is group 116, and one of
    /// them on any slot rules out all eleven everywhere else.
    /// </summary>
    public bool NoAffixChoices => (_choicesFresh ? _choices : AffixChoices).Count == 0;

    /// <summary>The pairs as the panel shows them, wearing whatever has been staged.</summary>
    public IReadOnlyList<AffixSlotRow> StagedPairs
    {
        get
        {
            if (_panel.Selected is not { } tile)
                return [];

            var rows = new List<AffixSlotRow>();

            foreach (var pair in MagicAffixes.PairsOn(_panel.Staged(tile.Item)))
            {
                rows.Add(new AffixSlotRow(
                    pair.Slot,
                    pair.PrefixName,
                    pair.SuffixName,
                    PrefixAimed: AffixSlot == pair.Slot && AffixIsPrefix,
                    SuffixAimed: AffixSlot == pair.Slot && !AffixIsPrefix));
            }

            // The slots opened but not yet filled. PairsOn shows what the item has, and an
            // empty slot is exactly what the item has nothing of - so an added one has to
            // be carried here until something is put in it.
            for (int n = 0; n < _slotsAdded && rows.Count < MagicAffixes.SlotsFor(tile.Item); n++)
            {
                int slot = rows.Count;

                rows.Add(new AffixSlotRow(
                    slot, "", "",
                    PrefixAimed: AffixSlot == slot && AffixIsPrefix,
                    SuffixAimed: AffixSlot == slot && !AffixIsPrefix));
            }

            return rows;
        }
    }


    #region Slot count

    /// <summary>
    /// Slots opened on the panel that the item has nothing in yet.
    /// <para>
    /// An empty slot is indistinguishable from a slot that is not there: both read as no
    /// affix. So opening one is remembered here rather than on the item, and stops being
    /// remembered the moment something is put in it - by then the item shows the slot
    /// itself.
    /// </para>
    /// </summary>
    private int _slotsAdded;

    /// <summary>
    /// An item holds three pairs at most, and one at least - a rare with no slots at all is
    /// not a thing the game makes.
    /// </summary>
    public bool CanAddSlot =>
        _panel.Selected is { } tile && StagedPairs.Count < MagicAffixes.SlotsFor(tile.Item);

    public bool CanRemoveSlot => StagedPairs.Count > 1;

    /// <summary>Opens a slot, and aims the list at it: the next pick fills it.</summary>
    [RelayCommand]
    private void AddSlot()
    {
        if (!CanAddSlot)
            return;

        _panel.Press();

        _slotsAdded++;
        AffixSlot = StagedPairs.Count - 1;
        AffixIsPrefix = true;

        Notify();
    }

    /// <summary>
    /// Takes the last slot off, at once - both its affixes and what they gave (every act of a
    /// picker applies; it was struck through until the tick). A slot only opened and never filled is simply closed
    /// again: there is nothing on the item to take off.
    /// </summary>
    [RelayCommand]
    private void RemoveSlot()
    {
        if (!CanRemoveSlot || StagedPairs.LastOrDefault() is not { } last)
            return;

        _panel.Press();

        int rows = StagedPairs.Count - 1;
        if (AffixSlot == last.Slot)
            AffixSlot = 0;

        if (_slotsAdded > 0 && last.PrefixName.Length == 0 && last.SuffixName.Length == 0)
        {
            _slotsAdded--;
            Notify();
            return;
        }

        _affixPicks[(last.Slot, true)] = new MagicAffixes.AffixPick(0, new Dictionary<string, int>());
        _affixPicks[(last.Slot, false)] = new MagicAffixes.AffixPick(0, new Dictionary<string, int>());
        Apply(rows);
    }

    private void NotifySlots()
    {
        OnPropertyChanged(nameof(CanAddSlot));
        OnPropertyChanged(nameof(CanRemoveSlot));
    }

    #endregion

    [RelayCommand]
    private void OpenAffixPicker()
    {
        if (_panel.Selected is not { } tile || !MagicAffixes.Carries(tile.Item))
            return;

        _panel.Press();

        _affixPicks.Clear();
        _slotsAdded = 0;
        _freshAffixes = false;
        AffixSearch = "";
        AffixSlot = 0;
        AffixIsPrefix = true;
        IsAffixPickerOpen = true;

        Notify();
        NotifySlots();
        _panel.Reveal(VP_ViewModel_Inv_R.AffixList);
    }

    /// <summary>Closes the picker. Nothing to take back: every act wrote as it was made.</summary>
    [RelayCommand]
    private void CloseAffixPicker()
    {
        _freshAffixes = false;
        _affixPicks.Clear();
        _slotsAdded = 0;
        IsAffixPickerOpen = false;
        Notify();
        NotifySlots();
    }

    /// <summary>
    /// What the slot being filled will hold: the affix staged for it, or the one the item
    /// already has where nothing has been staged.
    /// <para>
    /// The item's own affix counts, because aiming at a slot is asking what is in it. An
    /// earlier version showed only the staged pick, so clicking a slot holding Shimmering
    /// answered "None" - true of the choice, useless as an answer.
    /// </para>
    /// </summary>
    public MagicAffixes.AffixOption? ChosenAffix
    {
        get
        {
            if (_affixPicks.TryGetValue((AffixSlot, AffixIsPrefix), out var pick))
                return MagicAffixes.Find(pick.Id, AffixIsPrefix);

            return _panel.Selected is { } tile
                ? MagicAffixes.At(_panel.Staged(tile.Item), AffixSlot, AffixIsPrefix)
                : null;
        }
    }

    public bool HasChosenAffix => ChosenAffix is not null;

    /// <summary>Nothing in this slot: no affix on it.</summary>
    public bool NoChosenAffix => !HasChosenAffix;

    public string ChosenAffixName => ChosenAffix?.Name ?? "";

    /// <summary>
    /// The heading over the chosen affix: its name, so the section says what it is holding
    /// rather than restating what it is for - or, dim, that the slot holds nothing. On the one
    /// line either way, so the pool below does not move as a slot empties.
    /// </summary>
    public string ChosenAffixHeading =>
        ChosenAffixName is { Length: > 0 } named ? named : L("Empty. Pick one below, or roll for it.");

/// <summary>Aims the list at one side of one slot. The parameter is "slot:side".</summary>
    [RelayCommand]
    private void AimAffix(string? where)
    {
        if (where?.Split(':') is not [var slotText, var side] || !int.TryParse(slotText, out int slot))
            return;

        _panel.Press();

        // The search stays: what is looked for is the same whichever slot or side it is
        // looked for in.
        AffixSlot = slot;
        AffixIsPrefix = side == "prefix";
        OnPropertyChanged(nameof(NoChoicesNote));

        _panel.Reveal(VP_ViewModel_Inv_R.AffixList);
    }

    /// <summary>
    /// Puts one affix in the slot the list is aimed at, at once: every other picker's pick is the change, and one that
    /// had to be confirmed was inconsistent to use. Its numbers are the property
    /// rows' to change after, as a corruption's are.
    /// </summary>
    [RelayCommand]
    private void ChooseAffix(AffixChoiceRow? row)
    {
        if (row is null || !row.CanPick)
            return;

        _panel.Press();
        Stage(row.Option);
        Apply();
    }

    /// <summary>Stages an affix for the slot being filled, with its numbers rolled - written at once by the caller.</summary>
    private void Stage(MagicAffixes.AffixOption option)
    {
        _affixPicks[(AffixSlot, AffixIsPrefix)] =
            new MagicAffixes.AffixPick(option.Id, MagicAffixes.Roll(option, new Random()));

        Notify();
    }

    /// <summary>Empties the slot the list is aimed at, at once - the brush.</summary>
    [RelayCommand]
    private void ClearAffix()
    {
        _panel.Press();

        _affixPicks[(AffixSlot, AffixIsPrefix)] =
            new MagicAffixes.AffixPick(0, new Dictionary<string, int>());

        Apply();
    }

    /// <summary>
    /// Whether the staged picks are a whole new set - <see cref="RerollAllAffixes"/>'s - which
    /// <see cref="Apply"/> writes through <see cref="MagicAffixes.ApplyFresh"/> rather than slot by slot.
    /// </summary>
    private bool _freshAffixes;

    /// <summary>Whether every affix can be rolled again: a magic, rare or crafted item.</summary>
    public bool CanRerollAffixes =>
        _panel.Selected is { } tile && MagicAffixes.CountFor(tile.Item).Most > 0;

    /// <summary>
    /// A whole new set of affixes - which ones, how many, and their numbers - as the Perfect
    /// Skull recipe gives a rare (a reroll of all of them), and only legal ones: see
    /// <see cref="MagicAffixes.RollFresh"/>. The item level and the name stay. Staged as the
    /// picker's picks; <see cref="RerollAllAffixesNow"/> is the one caller, and confirms them.
    /// </summary>
    private void RerollAllAffixes()
    {
        if (_panel.Selected is not { } tile || !CanRerollAffixes)
            return;

        _panel.Press();

        var picks = MagicAffixes.RollFresh(_panel.Staged(tile.Item, withPicks: false), new Random());

        _affixPicks.Clear();
        foreach (var (key, pick) in picks)
            _affixPicks[key] = pick;

        _slotsAdded = 0;
        _freshAffixes = true;
        AffixSlot = 0;
        AffixIsPrefix = true;

        Notify();
        NotifySlots();
    }

    /// <summary>The same from the section header, confirmed at once - KEEP or UNDO as ever.</summary>
    [RelayCommand]
    private void RerollAllAffixesNow()
    {
        RerollAllAffixes();
        if (_freshAffixes)
            Apply(rows: 0);
    }

    /// <summary>One from the pool at random, which is what the game does - written at once, as a pick is.</summary>
    [RelayCommand]
    private void RollAffix()
    {
        if (_panel.Selected is not { } tile)
            return;

        var rollable = MagicAffixes
            .Available(_panel.Staged(tile.Item), AffixIsPrefix, AffixSlot)
            .Where(option => !option.NeedsParam)
            .ToList();
        if (rollable.Count == 0)
            return;

        _panel.Press();

        // Weighted by frequency, which is how often the game would land on each.
        int space = rollable.Sum(option => Math.Max(1, option.Frequency));
        int rolled = new Random().Next(1, space + 1);

        foreach (var option in rollable)
        {
            rolled -= Math.Max(1, option.Frequency);
            if (rolled <= 0)
            {
                Stage(option);
                break;
            }
        }

        Apply();
    }

    /// <summary>
    /// Writes the pick and rebuilds the item's properties to match it, which is the whole point of changing an affix
    /// rather than typing a property in. Each pick, roll, emptied slot and removed slot comes here at once, and the
    /// picker stays open, as the corruption's does (one that had to be confirmed was inconsistent to use); the
    /// properties rebuilt are said on the strip, where a warning stood by the tick.
    /// </summary>
    /// <param name="rows">
    /// How many slots the list shows after: an emptied slot stays where it was until removed, though the item - which
    /// cannot tell an empty last slot from none - drops it. Left out, the rows it shows now.
    /// </param>
    private void Apply(int? rows = null)
    {
        if (_panel.Selected is not { } tile || _affixPicks.Count == 0)
            return;

        // Counted without the pick: worn, an emptied last pair has already dropped off the item's list.
        int shown = rows ?? Math.Min(MagicAffixes.SlotsFor(tile.Item),
            MagicAffixes.PairsOn(_panel.Staged(tile.Item, withPicks: false)).Count + _slotsAdded);

        // Worked out on a copy of the item **as the page currently holds it**, and the
        // answer put in front of KEEP rather than on the item. Copying the file's own
        // version instead threw away every change already staged, so a second affix swap
        // silently undid the first.
        if (_panel.Pending() is not { } shadow)
        {
            _affixPicks.Clear();
            return;
        }

        // A whole new set comes off and goes on by subtraction - see ApplyFresh - and the
        // desecration it sits beside is told first and kept, as the corruption clean does.
        if (_freshAffixes && Desecrations.Held(shadow) is { } desecration)
            Desecrations.Assume(tile.Item, desecration);

        // The recipe too, on the item itself - a reading of it, so the page's later copies
        // still know it once the affixes that told it are gone.
        if (_freshAffixes && AffixRanges.PinRecipe(shadow))
            AffixRanges.CarryPinnedRecipe(shadow, tile.Item);

        // A socket affix goes before a socket corruption: told while the corruption can still tell the desecration
        // beside it, as the corruption's own clean does.
        if (Corruptions.IdOn(shadow) == Corruptions.Sockets && Desecrations.Held(shadow) is { } beside)
            Desecrations.Assume(tile.Item, beside);
        bool cleaned = MagicAffixes.CleanSocketCorruptionFor(shadow, _affixPicks, new Random());

        var told = _freshAffixes
            ? MagicAffixes.ApplyFresh(shadow, _affixPicks, new Random())
            : MagicAffixes.Apply(shadow, _affixPicks, new Random());
        _freshAffixes = false;

        // Sockets closed over what is in them, where it cannot come out: said, and nothing staged.
        if (_panel.WouldTurnOut(shadow))
        {
            _affixPicks.Clear();
            BuildPairs();
            Build();
            Notify();
            _panel.Say(L($"{tile.Name}: socketed items do not come out here."));
            return;
        }

        if (cleaned)
            _panel.Draft.Corruption = 0;

        foreach (var ((slot, prefix), _) in _affixPicks)
        {
            var ids = prefix ? shadow.MagicPrefixIds : shadow.MagicSuffixIds;
            if (ids is not null && slot >= 0 && slot < ids.Length)
                _panel.Draft.AffixIds[(slot, prefix)] = ids[slot];
        }

        _panel.StageStatsFrom(shadow, tile);

        // A socket prefix arriving bores its sockets and one leaving closes them (MagicAffixes.SettleSockets),
        // and the count is the header's, not a row: staged as the socket section would, as a corruption's are.
        // Left out, a rare made with a Jeweler's kept its three sockets through a redraw that took the
        // Jeweler's off - three on a rare, where the most anything bores is two (the Forge's roll).
        if (shadow.TotalNumberOfSockets != _panel.StagedSockets)
            _panel.StageSocketsFrom(shadow);

        _affixPicks.Clear();
        _slotsAdded = Math.Max(0, shown - MagicAffixes.PairsOn(shadow).Count);

        BuildPairs();
        Build();
        Notify();
        _panel.Changed();

        _panel.Say(cleaned
            ? L($"{tile.Name}: affixes changed, socket corruption cleaned.")
            : told.Count == 0
            ? L($"{tile.Name}: affixes changed.")
            : Plural(told.Count, $"{tile.Name}: affixes changed, {told.Count} property rebuilt.",
                     $"{tile.Name}: affixes changed, {told.Count} properties rebuilt."));
    }

    /// <summary>
    /// The item level the affixes on it ask for, where that is more than it has. Said
    /// beside the level field, since that is the field that would have to answer for it.
    /// </summary>
    public int AffixLevelNeeded
    {
        get
        {
            if (_panel.Selected is not { } tile)
                return 0;

            var shadow = _panel.Staged(tile.Item);

            // Asked in affix levels, which is the only place the two are comparable. Asking
            // it in item levels said a Brandistock at item level 12 needed item level 13 for
            // affixes it already had: its base outranks it, so its affix level is 8 whatever
            // the item level below 16 says, and 13 was a number it could never satisfy.
            if (MagicAffixes.OutOfReach(shadow) is not { } beyond)
                return 0;

            // Answered as an item level, since that is the field the page offers - the lowest
            // at which any base an upgraded item has been on would reach it.
            return MagicAffixes.ItemLevelAcrossUpgradesFor(shadow, beyond.Level);
        }
    }

    public bool NeedsHigherLevel => AffixLevelNeeded > 0;

    /// <summary>Which affix, and what it would take - the name is the point of it.</summary>
    public string AffixLevelNote =>
        _panel.Selected is { } tile
        && MagicAffixes.OutOfReach(_panel.Staged(tile.Item)) is { } beyond
            ? L($"Require item level {AffixLevelNeeded} ({beyond.Name})")
            : L($"Require item level {AffixLevelNeeded}");

    public void Notify()
    {
        OnPropertyChanged(nameof(AffixChoices));
        OnPropertyChanged(nameof(NoAffixChoices));
        OnPropertyChanged(nameof(NoChoicesNote));
        OnPropertyChanged(nameof(AffixLevelNeeded));
        OnPropertyChanged(nameof(NeedsHigherLevel));
        OnPropertyChanged(nameof(AffixLevelNote));
        OnPropertyChanged(nameof(StagedPairs));
        // And the slot buttons with the pairs: a pair emptied by the chosen affix's Empty slot drops off the list,
        // which makes room - the + stayed grey from when the slots were full until the picker was opened again
        // (on a rare jewel).
        NotifySlots();
        OnPropertyChanged(nameof(ChosenAffix));
        OnPropertyChanged(nameof(HasChosenAffix));
        OnPropertyChanged(nameof(NoChosenAffix));
        OnPropertyChanged(nameof(ChosenAffixName));
        OnPropertyChanged(nameof(ChosenAffixHeading));

        // The panel told this one itself while it held the affixes; the section's skull hangs on it.
        OnPropertyChanged(nameof(CanRerollAffixes));
    }

    public bool HasAffixes => Names.Count > 0;

    /// <summary>
    /// The section's title. It holds more than the affixes now - the name, crafting, automod, staffmods, the
    /// superior bonus - so it says so, and counts nothing: the count was the affixes' alone. It reads "Affixes &amp; mods".
    /// </summary>
    public string AffixesHeader => L("Affixes & mods");

    [RelayCommand]
    private void ToggleAffixes()
    {
        _panel.Press();
        AffixesOpen = !AffixesOpen;
    }

    public void Build()
    {
        Names.Clear();

        if (_panel.Selected is { } tile)
        {
            // A rare names itself from two lists and either half can be changed, so the
            // name is a row here - shown as it has been picked rather than as the item
            // still says, so the row and the button under the pointer never disagree.
            // A magic item's name is its prefix and suffix and nothing else, both already
            // listed below and neither changeable here, so a name row would only repeat
            // the heading.
            if (_panel.RareName.HasName && _panel.RareName.Name.Length > 0)
                Names.Add(new AffixName(L("Name"), _panel.RareName.Name, IsRareName: true));
        }

        BuildPairs();

        OnPropertyChanged(nameof(HasAffixes));
        OnPropertyChanged(nameof(AffixesHeader));
    }

    /// <summary>
    /// The affix pairs, laid out the way the save holds them: slot by slot, prefix beside
    /// suffix. A flat list of "Prefix: Bronze / Suffix: of Strength" lost which belonged
    /// with which, and on a rare that is most of what there is to see - one here carries
    /// three prefixes against two suffixes, another one prefix against three suffixes.
    /// </summary>
    public ObservableCollection<MagicAffixes.AffixPair> AffixPairs { get; } = [];

    public bool HasAffixPairs => AffixPairs.Count > 0;

    /// <summary>The pairs are part of the section, so folding it puts them away as well.</summary>
    public bool ShowAffixPairs => HasAffixPairs && AffixesOpen;

    /// <summary>
    /// A superior item has no affix, but its bonus is chosen here (SuperiorEditor), so the section stands
    /// for it too.
    /// </summary>
    private bool IsSuperior => SuperiorEditor.Stands(_panel);

    /// <summary>The base's own automagic and staff mods are chosen here too (AutomagicEditor, StaffModsEditor).</summary>
    private bool HasBaseMods => AutomagicEditor.Stands(_panel) || StaffModsEditor.Stands(_panel);

    /// <summary>Whether the section has anything at all in it.</summary>
    public bool ShowsAffixes => !_panel.IsTabTile && (HasAffixes || HasAffixPairs || IsSuperior || HasBaseMods);

    public void BuildPairs()
    {
        AffixPairs.Clear();

        if (_panel.Selected is { } tile)
        {
            // Wearing whatever has been settled on but not yet written, so the button
            // under the panel says what KEEP would put on the item.
            foreach (var pair in MagicAffixes.PairsOn(_panel.Staged(tile.Item)))
                AffixPairs.Add(pair);
        }

        OnPropertyChanged(nameof(HasAffixPairs));
        OnPropertyChanged(nameof(ShowAffixPairs));
        OnPropertyChanged(nameof(ShowsAffixes));
    }

    [ObservableProperty] private bool _isAffixPickerPresent;

    partial void OnIsAffixPickerOpenChanged(bool value) =>
        Presence.Keep(value, () => IsAffixPickerOpen, on => IsAffixPickerPresent = on, VP_ViewModel_Inv_R.VeilFade);

    /// <summary>Whether any affix id is staged to be written.</summary>
    public bool AnyStaged => _panel.Draft.AffixIds.Count > 0;

    /// <summary>The picker and its slots belong to the item that was showing; a rebuild drops them.</summary>
    public void Reset()
    {
        IsAffixPickerOpen = false;
        _affixPicks.Clear();

        // The slots the panel had opened belong to that item too.
        _slotsAdded = 0;
    }

    /// <summary>The picker's own picks, worn by a staged copy while the picker is open.</summary>
    public void WearPicks(Item shadow)
    {
        foreach (var ((slot, prefix), pick) in _affixPicks)
        {
            var ids = prefix ? shadow.MagicPrefixIds : shadow.MagicSuffixIds;
            if (ids is not null && slot >= 0 && slot < ids.Length)
                ids[slot] = (ushort)Math.Max(0, pick.Id);
        }
    }

    /// <summary>Writes the staged affix ids onto the item at KEEP, and lets them go.</summary>
    public void Commit(Item item)
    {
        foreach (var ((slot, prefix), id) in _panel.Draft.AffixIds)
        {
            var ids = prefix ? item.MagicPrefixIds : item.MagicSuffixIds;
            if (ids is not null && slot >= 0 && slot < ids.Length)
                ids[slot] = (ushort)id;
        }

        _panel.Draft.AffixIds.Clear();
    }
}
