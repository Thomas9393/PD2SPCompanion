using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D2SLib.Model.Save;
using PD2SPCompanion.Core.Graphics;
using PD2SPCompanion.Shell.Imaging;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// The item panel's corruption: what the item carries, the picker over the page, cleaning, and what
/// it all stages into the panel's <see cref="ItemDraft"/>. A corruption is several properties and
/// sometimes a socket count, confirmed together.
/// </summary>
public sealed partial class CorruptionEditor : ViewModelBase
{
    private readonly IItemPanel _panel;

    public CorruptionEditor(IItemPanel panel) => _panel = panel;

    private ItemDraft Draft => _panel.Draft;

    public bool IsStaged => Draft.Corruption is not null;

    /// <summary>The corruption the item will have, staged or stored.</summary>
    public int Now =>
        Draft.Corruption ?? (_panel.Selected is { } tile ? Corruptions.IdOn(tile.Item) ?? 0 : 0);

    /// <summary>Whether this item has been corrupted.</summary>
    public bool IsCorrupted => Now > 0;

    /// <summary>
    /// Whether a shard was spent on the item to no effect (<see cref="Corruptions.IsSpent"/>) and nothing
    /// is staged in its place: a corruption picked replaces the mark, a clean takes it off.
    /// </summary>
    public bool IsSpent => Draft.Corruption is null && _panel.Selected is { } tile && Corruptions.IsSpent(tile.Item);

    /// <summary>Whether the picker's head has something to say of the item: its corruption, or its spent shard.</summary>
    public bool HoldsMark => IsCorrupted || IsSpent;


    /// <summary>The bin's tooltip: a corruption's clean rerolls what it touched; a spent shard's moves nothing.</summary>
    public string CleanHint => CleanWouldTurnOut
        ? ShortcutText.Dimmed(L("Clean corruption"), L("Socketed items do not come out here."))
        : IsSpent ? L("Clear spent shard") : L("Clean corruption\nRerolls affected stats");

    /// <summary>The bin can take the mark off: not where that closes sockets holding something that cannot come out.</summary>
    public bool CanClean => HoldsMark && !CleanWouldTurnOut;

    /// <summary>
    /// The clean would close a socket that is filled, on a page gems do not come out of (the vault's Edit page): it said
    /// "cleaned" and then could not be written, and the next selection dropped it without a word.
    /// </summary>
    private bool CleanWouldTurnOut =>
        _panel.Selected is { } tile && !_panel.CanEmptySockets && Now == Corruptions.Sockets
        && Corruptions.SocketsWithout(_panel.Staged(tile.Item)) < tile.Item.SocketedItems.Count;

    /// <summary>
    /// Whether the sockets on this item came from corrupting it.
    /// <para>
    /// The one corruption whose effect is not a property, so the one that no stat row can
    /// carry the mark for. The game puts its asterisk on the socket count instead, and so
    /// does this.
    /// </para>
    /// </summary>
    public bool SocketsAreCorrupted => _panel.Selected is not null && Now == Corruptions.Sockets;

    /// <summary>
    /// The shard this item is corrupted with, drawn from the game's own art rather than from a
    /// shape of our own: the button does what putting one in the cube does. The Corrupted
    /// Worldstone Shard for Annihilus, the plain one for everything else.
    /// </summary>
    public Avalonia.Media.Imaging.Bitmap? Icon =>
        SpriteBitmap.From(SpriteLoader.Current?.Load(
            _panel.Selected is { } tile && Corruptions.ShardFor(_panel.Staged(tile.Item)) == Corruptions.AnnihilusShard
                ? "invcwss"
                : "invwss"));

    /// <summary>
    /// Writes the two stats a corruption keeps for itself - which one it was, and the flag
    /// that paints it red - or takes them off where the id is zero.
    /// </summary>
    public static void Write(Item item, int id)
    {
        if (item.StatLists.Count == 0)
            return;

        // Wherever they are kept, which is not always the first list.
        foreach (var list in item.StatLists)
        {
            list.Stats.RemoveAll(stat =>
                stat.Stat is Corruptions.IdStat or Corruptions.FlagStat);
        }

        if (id <= 0)
            return;

        foreach (var (name, value) in new[] { (Corruptions.IdStat, id), (Corruptions.FlagStat, Corruptions.FlagFor(item, id, Random.Shared)) })
        {
            if (StatCatalogue.IdOf(name) is { } statId)
            {
                ItemStats.Home(item).Stats.Add(
                    new ItemStat { Id = (ushort)statId, Stat = name, Value = value });
            }
        }
    }

    /// <summary>Writes what is staged onto the item itself, at KEEP, and lets it go.</summary>
    public void Commit(Item item)
    {
        if (Draft.Corruption is not { } settled)
            return;

        Write(item, settled);
        Draft.Corruption = null;
    }

    /// <summary>The picker belongs to the item that was showing when it opened; a rebuild closes it.</summary>
    public void Reset()
    {
        IsPickerOpen = false;
        Chosen = null;
    }

    /// <summary>What the corruption did, for the line that says so.</summary>
    public string Summary
    {
        get
        {
            if (IsSpent)
                return L("Shard spent, no effect.");

            if (_panel.Selected is not { } tile || !IsCorrupted)
                return "";

            var held = _panel.Staged(tile.Item);
            int? id = Corruptions.IdOn(held);

            // What the corruption is, which is a range - not what this one rolled. Where
            // it landed on a property the item already had, what it rolled cannot be known
            // exactly anyway: the two are stored added together.
            // Sockets are not a property and have no wording of their own; what the item
            // holds is the answer.
            if (id == Corruptions.Sockets)
                return L($"Corruption {id}: +{held.TotalNumberOfSockets} sockets");

            // Worded as its row in the list is - the four resistances "All Resistances" - where the row can be found.
            // Named by its first stat alone, an all-resistance corruption read "Fire Resist" here.
            if (Corruptions.Available(held, asIfClean: true).FirstOrDefault(option => option.Id == id && !option.GivesSockets)
                is { } row)
                return L($"Corruption {id}: {row.Readable}");

            var granted = Corruptions.On(held).Where(grant => !Corruptions.IsTint(grant.Code)).ToList();

            string what = granted.Count > 0
                ? string.Join(", ", granted.Select(grant =>
                    StatText.DescribeRange(
                        AffixRanges.StatsOf(grant.Code).FirstOrDefault() ?? grant.Code,
                        grant.Min, grant.Max, SkillNames.NameOf(grant.Param)) ?? grant.Code))
                : id == Corruptions.Sockets
                    ? "sockets"
                    : "unknown";

            return L($"Corruption {id}: {what}");
        }
    }

    /// <summary>
    /// Whether this item could be corrupted. The cube's own conditions decide - the right
    /// kind of item, and not one that has been corrupted already.
    /// </summary>
    public bool CanCorrupt => _panel.Selected is { } tile
        && !IsCorrupted
        && Corruptions.Available(_panel.Staged(tile.Item)).Count > 0;

    /// <summary>
    /// The button is offered on anything the cube would take, <b>and on anything already
    /// corrupted</b> - the panel is where a corrupted item says what happened to it, so
    /// hiding the way in once it has one leaves that unreadable.
    /// </summary>
    public bool Shows => CanCorrupt || IsCorrupted || IsSpent;

    [ObservableProperty] private bool _isPickerOpen;

    /// <summary>Kept apart from openness so the veil can fade out - see <see cref="Presence"/>.</summary>
    [ObservableProperty] private bool _isPickerPresent;

    partial void OnIsPickerOpenChanged(bool value) =>
        Presence.Keep(value, () => IsPickerOpen, on => IsPickerPresent = on, VP_ViewModel_Inv_R.VeilFade);

    /// <summary>
    /// Everything the cube could give this item, in three groups by how likely it is - the
    /// same three the wiki lists, and for the same reason: the band that selects an outcome
    /// is as wide as its chance.
    /// </summary>
    public IReadOnlyList<CorruptionTier> Choices
    {
        get
        {
            if (_panel.Selected is not { } tile)
                return [];

            return
            [
                // Asked as if the item were clean: a corrupted one shows this list to say
                // what could be had instead, and its own corruption is in the way of that.
                .. Corruptions.Available(_panel.Staged(tile.Item), asIfClean: true)
                    .GroupBy(option => option.Tier)
                    .Select(group => new CorruptionTier(
                        // Annihilus's five are one band each, 20% apiece - no rarity to tell
                        // apart, so named as the wiki names them.
                        group.All(option => option.Shard == Corruptions.AnnihilusShard) ? L("Corruption modifier") :
                        group.Key switch
                        {
                            Corruptions.Rarity.Sockets => L("Sockets"),
                            Corruptions.Rarity.Low => L("Low rarity"),
                            Corruptions.Rarity.Mid => L("Mid rarity"),
                            _ => L("High rarity"),
                        },
                        $"{group.Sum(option => option.Width) * 100.0 / group.First().Height:0.#}%",
                        [.. group.Select(option => new CorruptionChoiceRow(option, IsTheOneHeld(option)))])),
            ];
        }
    }

    /// <summary>
    /// Whether this row is the corruption the item is sitting on - the chosen one, or on an
    /// already corrupted item the one it actually carries.
    /// <para>
    /// The id alone is not enough for sockets: every socket outcome writes id 32, so
    /// matching on that marks all of them. What tells them apart is how many holes they
    /// bore, which is what the item is holding.
    /// </para>
    /// </summary>
    private bool IsTheOneHeld(Corruptions.CorruptionOption option)
    {
        if (Chosen is { } chosen)
        {
            return option.Id == chosen.Option.Id
                && (!option.GivesSockets
                    || option.SocketsGiven == chosen.Option.SocketsGiven);
        }

        if (Now != option.Id)
            return false;

        return !option.GivesSockets || option.SocketsGiven == _panel.StagedSockets;
    }

    /// <summary>
    /// Whether the list can be picked from: on anything the panel shows for. A pick on a corrupted item takes
    /// its corruption's place - the clean and the new one in one step, as the desecration picker's does, rather than
    /// clear, then pick again. The list was shut until the
    /// brush was pressed.
    /// </summary>
    public bool CanPick => Shows;

    /// <summary>
    /// The corruption showing is staged on the page and not on the item: nothing has been kept, so
    /// another pick or roll takes its place - the picker stays open for that.
    /// </summary>
    private bool IsOnlyStaged =>
        Draft.Corruption is > 0 && _panel.Selected is { } tile
        && (!Corruptions.IsCorrupted(tile.Item) || Corruptions.IsSpent(tile.Item));

    /// <summary>
    /// Whether cleaning this item has been asked for but not yet confirmed.
    /// <para>
    /// Cleaning stopped being reversible arithmetic the day it became a reroll: it throws
    /// the item's numbers away and rolls new ones, so it waits for the same confirmation
    /// every other edit on the page waits for. Staging it also unlocks the list, which lets
    /// a clean and the corruption that replaces it be confirmed together.
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPick))]
    [NotifyPropertyChangedFor(nameof(HasPlan))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private bool _cleanStaged;

    /// <summary>Whether there is anything for Confirm to stage.</summary>
    public bool HasPlan => Chosen is not null || CleanStaged;

    /// <summary>
    /// The corruption picked or rolled, its numbers rolled, on its way to the item: Confirm
    /// stages it at once, and the page holds it until KEEP, as every other field does.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlan))]
    private Corruptions.CorruptionDraft? _chosen;

    /// <summary>
    /// Whether the base could not take all the holes the corruption offered. The recipe says how
    /// many and the base how many it can hold, and the smaller wins - worth saying, or the item
    /// looks as though the roll was misread. Said with the corruption, on the strip.
    /// </summary>
    private static bool SocketsCapped(Corruptions.CorruptionDraft chosen) =>
        chosen.Option.GivesSockets && chosen.Sockets < chosen.Option.SocketsGiven;

    partial void OnIsPickerPresentChanged(bool value)
    {
        if (!value)
            return;
        OnPropertyChanged(nameof(Choices));
        OnPropertyChanged(nameof(CanPick));
    }

    /// <summary>Everything the panel shows of the corruption, told again.</summary>
    public void Notify()
    {
        OnPropertyChanged(nameof(IsCorrupted));
        OnPropertyChanged(nameof(IsSpent));
        OnPropertyChanged(nameof(HoldsMark));
        OnPropertyChanged(nameof(CanClean));
        OnPropertyChanged(nameof(CleanHint));
        OnPropertyChanged(nameof(SocketsAreCorrupted));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(CanCorrupt));
        OnPropertyChanged(nameof(Shows));
        OnPropertyChanged(nameof(Icon));

        // The list only while the picker is there: shut, nobody reads it, and each notification worked it out for
        // nobody - and its rows asked again whether they could be picked.
        if (IsPickerPresent)
        {
            OnPropertyChanged(nameof(Choices));
            OnPropertyChanged(nameof(CanPick));
        }
    }

    [RelayCommand]
    private void OpenPicker()
    {
        _panel.Press();

        Chosen = null;
        CleanStaged = false;
        OnPropertyChanged(nameof(Choices));
        IsPickerOpen = true;

        _panel.Reveal(VP_ViewModel_Inv_R.CorruptionList);
    }

    [RelayCommand]
    private void ClosePicker()
    {
        Chosen = null;
        CleanStaged = false;
        IsPickerOpen = false;

        // Nothing was settled, so nothing is left marked. Reopening starts from what the
        // item has - or from nothing, on one that is clean.
        OnPropertyChanged(nameof(Choices));
    }

    /// <summary>
    /// Takes one from the list, its numbers rolled inside their ranges, and stages it (a pick is the
    /// change - written at once, and taken back by Ctrl + Z). The picker
    /// stays open: what was staged shows at its top, and another pick or roll
    /// replaces it. Its numbers are the property rows' to change after.
    /// </summary>
    [RelayCommand]
    private void Pick(CorruptionChoiceRow? row)
    {
        if (row is null || _panel.Selected is not { } tile || !CanPick)
            return;

        _panel.Press();
        TakesPlace();
        Chosen = Corruptions.Draft(Uncorrupted(tile), row.Option, new Random());
        Confirm(close: false);
        Notify();
    }

    /// <summary>
    /// The item as the page holds it, without a corruption that is only staged: what a new pick is
    /// drafted against, since it takes that one's place.
    /// </summary>
    private Item Uncorrupted(ItemTileViewModel tile)
    {
        var staged = _panel.StagedToChange(tile.Item);
        if (IsOnlyStaged && !ReferenceEquals(staged, tile.Item))
            Write(staged, 0);
        return staged;
    }

    /// <summary>
    /// The whole thing the way the cube does it: a roll from 1 to 1000 picks which
    /// corruption, and its numbers roll inside their own ranges.
    /// </summary>
    [RelayCommand]
    private void Roll()
    {
        if (_panel.Selected is not { } tile)
            return;

        _panel.Press();
        TakesPlace();

        // Asked as the list is, as if clean: a corruption that bored sockets is in the way otherwise.
        var random = new Random();
        if (Corruptions.Roll(Uncorrupted(tile), random, asIfClean: true) is { } rolled)
        {
            Chosen = Corruptions.Draft(tile.Item, rolled, random);
            Confirm(close: false);
            Notify();
        }
    }
    /// <summary>
    /// A corruption the item carries - or a spent shard's mark - comes off first: the clean is staged with the
    /// new one, and the two confirm together. One only staged needs none: <see cref="Confirm"/> puts back
    /// what it shared with the item instead.
    /// </summary>
    private void TakesPlace()
    {
        if (HoldsMark && !IsOnlyStaged)
            CleanStaged = true;
    }

    /// <summary>
    /// Cleans the corruption off, staged at once (a pick is the change - written at once, and taken back by Ctrl + Z): it rerolls what the corruption touched -
    /// where it shared a property with an affix the original cannot be recovered, the two are stored
    /// added, so it is constrained from both ends and rolled inside what is left. The picker stays
    /// open on the clean item, so a new corruption can be picked next.
    /// </summary>
    [RelayCommand]
    private void Clean()
    {
        // The panel, not the file. A corruption staged and not yet kept is still a
        // corruption to clean, and asking the item left the button doing nothing.
        if (_panel.Selected is not { } tile || !HoldsMark)
            return;

        _panel.Press();
        CleanStaged = true;
        Confirm(close: false);
        Notify();
    }

    /// <summary>
    /// Works the plan out on the item as the panel holds it and stages the answer: the clean
    /// first, then the new corruption, so the two confirm together.
    /// </summary>
    private void Confirm(bool close = true)
    {
        if (_panel.Selected is not { } tile || !HasPlan)
            return;

        // Worked out on the item as the page holds it, and the answer staged. Written
        // straight to the item, this used to discard every other pending edit.
        if (_panel.Pending() is not { } shadow)
            return;

        var said = new List<string>();
        int? stagedBefore = Draft.Corruption;

        // Cleaning first, because a corruption chosen alongside it was chosen against the
        // item as it will be - the list is built as if the item were already clean.
        // A spent shard is taken off with nothing to reroll: the mark alone goes.
        if (CleanStaged && Corruptions.IsSpent(shadow))
        {
            Corruptions.Clean(shadow, new Random());
            Draft.Corruption = 0;
            said.Add(L("Spent shard cleared."));
        }
        else if (CleanStaged && Corruptions.IsCorrupted(shadow) && !Corruptions.IsSpent(shadow))
        {
            // Told while the corruption is still there to tell it by, and kept on the item -
            // a reading of it, not an edit. See Corruptions.Clean.
            if (Desecrations.Held(shadow) is { } desecration)
                Desecrations.Assume(tile.Item, desecration);

            var moved = Corruptions.Clean(shadow, new Random());
            Draft.Corruption = 0;
            int lost = moved.Count(each => each.Removed);
            int rolled = moved.Count - lost;

            said.Add((rolled, lost) switch
            {
                ( > 0, > 0) => L($"Corruption cleaned, {rolled} rerolled, {lost} lost with it."),
                ( > 0, _) => L($"Corruption cleaned, {rolled} rerolled."),
                (_, > 0) => L($"Corruption cleaned, {lost} lost with it."),
                _ => L("Corruption cleaned."),
            });
        }

        // A corruption staged and not kept gives way to the new one: taken off the pending item, and
        // what it shared with the item's own properties put back to what the item holds - the clean's
        // reroll is for a corruption the file carries, whose share of a number cannot be told.
        if (Chosen is not null && !CleanStaged && Corruptions.IsCorrupted(shadow) && !Corruptions.IsSpent(shadow)
            && (!Corruptions.IsCorrupted(tile.Item) || Corruptions.IsSpent(tile.Item)))
        {
            var shared = Corruptions.Clean(shadow, new Random()).Where(each => !each.Removed).ToList();
            var own = shadow.StatLists.FirstOrDefault()?.Stats ?? [];
            foreach (var stored in tile.Item.StatLists.FirstOrDefault()?.Stats ?? [])
            {
                if (shared.Any(each => string.Equals(each.Stat, stored.Stat, StringComparison.OrdinalIgnoreCase))
                    && own.FirstOrDefault(each => AffixRanges.KeyOf(each) == AffixRanges.KeyOf(stored)) is { } pending)
                    pending.Value = stored.Value;
            }

            // And the sockets the item holds: the staged one's bored count gives way to them, a quest's included,
            // which the clean cannot tell.
            if (Corruptions.IdOn(tile.Item) != Corruptions.Sockets && tile.Item.TotalNumberOfSockets > shadow.TotalNumberOfSockets)
            {
                shadow.TotalNumberOfSockets = tile.Item.TotalNumberOfSockets;
                shadow.IsSocketed = true;
            }
            Draft.Corruption = 0;
        }

        if (Chosen is { } chosen && (!Corruptions.IsCorrupted(shadow) || Corruptions.IsSpent(shadow)))
        {
            Corruptions.Apply(shadow, chosen);
            Draft.Corruption = chosen.Option.Id;
            said.Add(SocketsCapped(chosen)
                ? L($"Corrupted: {chosen.Option.Summary} (sockets capped at base max).")
                : L($"Corrupted: {chosen.Option.Summary}."));
        }

        // Sockets closed over what is in them, where it cannot come out: said, and nothing staged.
        if (_panel.WouldTurnOut(shadow))
        {
            Draft.Corruption = stagedBefore;
            Chosen = null;
            CleanStaged = false;
            _panel.Say(L($"{tile.Name}: socketed items do not come out here."));
            return;
        }

        _panel.StageStatsFrom(shadow, tile);

        // A corruption bores sockets and a clean closes them, and the panel stages those the
        // same way the socket section does.
        _panel.StageSocketsFrom(shadow);

        Chosen = null;
        CleanStaged = false;
        if (close)
            IsPickerOpen = false;
        else
            OnPropertyChanged(nameof(Choices));

        _panel.AffixesChanged(rebuild: false);
        _panel.Changed();

        _panel.Say(said.Count == 0
            ? L($"{tile.Name}: corruption changed.")
            : L($"{tile.Name}: {string.Join(" ", said)}"));
    }
}
