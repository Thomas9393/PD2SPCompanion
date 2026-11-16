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
/// The item panel's desecration: what an amulet has, the picker over the page, and what it
/// stages into the panel's <see cref="ItemDraft"/>. Lucion's desecration takes a corrupted
/// amulet only.
/// </summary>
public sealed partial class DesecrationEditor : ViewModelBase
{
    private readonly IItemPanel _panel;

    public DesecrationEditor(IItemPanel panel) => _panel = panel;

    private ItemDraft Draft => _panel.Draft;

    public bool IsStaged => Draft.Desecration is not null;

    /// <summary>The desecration's value the item will have, staged or stored.</summary>
    public int Now =>
        Draft.Desecration ?? (_panel.Selected is { } tile ? Desecrations.ValueOn(tile.Item) ?? 0 : 0);

    public bool IsDesecrated => Now > 0;

    /// <summary>The button's picture: a desecrated amulet.</summary>
    public Avalonia.Media.Imaging.Bitmap? Icon =>
        SpriteBitmap.From(SpriteLoader.Current?.Load("invamu2d"));

    /// <summary>A corrupted amulet, and only that - every desecrated item on the market is one.</summary>
    public bool CanDesecrate => _panel.Selected is { } tile && Desecrations.CanTake(_panel.Staged(tile.Item));

    /// <summary>Offered on a desecrated one too, as corruption is: the panel says what it has.</summary>
    public bool Shows => CanDesecrate || IsDesecrated;

    [ObservableProperty] private bool _isPickerOpen;

    /// <summary>Kept apart from openness so the veil can fade out - see <see cref="Presence"/>.</summary>
    [ObservableProperty] private bool _isPickerPresent;

    partial void OnIsPickerOpenChanged(bool value) =>
        Presence.Keep(value, () => IsPickerOpen, on => IsPickerPresent = on, VP_ViewModel_Inv_R.VeilFade);

    /// <summary>
    /// Writes the pair a desecration keeps - the value, and the flag - or takes them off where
    /// the value is zero.
    /// </summary>
    public static void Write(Item item, int value)
    {
        foreach (var list in item.StatLists)
        {
            list.Stats.RemoveAll(stat =>
                stat.Stat is Desecrations.ValueStat or Desecrations.FlagStat);
        }

        if (value <= 0)
            return;

        foreach (var (name, held) in new[] { (Desecrations.ValueStat, value), (Desecrations.FlagStat, Desecrations.FlagFor(item)) })
        {
            if (StatCatalogue.IdOf(name) is { } statId)
            {
                ItemStats.Home(item).Stats.Add(
                    new ItemStat { Id = (ushort)statId, Stat = name, Value = held });
            }
        }
    }

    /// <summary>Writes what is staged onto the item itself, at KEEP, and lets it go.</summary>
    public void Commit(Item item)
    {
        if (Draft.Desecration is not { } desecrated)
            return;

        Write(item, desecrated);
        Desecrations.Assume(item, Draft.DesecrationMod);
        Draft.Desecration = null;
        Draft.DesecrationMod = null;
    }

    /// <summary>The picker belongs to the item that was showing when it opened; a rebuild closes it.</summary>
    public void Reset()
    {
        IsPickerOpen = false;
        Chosen = null;
    }

    /// <summary>The mod the desecration is, where it can be told.</summary>
    private Corruptions.CorruptionOption? Held =>
        _panel.Selected is { } tile && IsDesecrated ? Desecrations.Held(_panel.Staged(tile.Item)) : null;

    /// <summary>What the desecration added, for the line that says so.</summary>
    public string Summary =>
        !IsDesecrated ? ""
        : Held is { } held ? held.Readable
        : L("Mod unknown");

    /// <summary>
    /// The mods it could be, where the item cannot tell - the file keeps only whether it was
    /// enhanced damage. The player's answer is what a clean takes off.
    /// </summary>
    public IReadOnlyList<Corruptions.CorruptionOption> Candidates =>
        _panel.Selected is { } tile && IsDesecrated && !IsStaged
        && Desecrations.Assumed(tile.Item) is null
        && Desecrations.Identify(_panel.Staged(tile.Item)) is { Found: null } unclear
            ? unclear.Candidates
            : [];

    /// <summary>
    /// Whether the player has to say which mod it is: the item's own lines cannot tell. Stays up once they have, so
    /// the answer can be changed - but not for a mod the lines tell, which is never asked. It stood up after every
    /// pick: the pick is remembered as the item's mod, and the remembering was read as an answer given.
    /// </summary>
    public bool IsUnclear =>
        _panel.Selected is { } tile && IsDesecrated && !IsStaged
        && Desecrations.Identify(_panel.Staged(tile.Item)).Found is null;

    /// <summary>
    /// What the answer is picked from: the candidates, or the whole pool where none fit - an
    /// item edited by hand can hold numbers no mod explains.
    /// </summary>
    public IReadOnlyList<DesecrationChoiceRow> Answers
    {
        get
        {
            if (_panel.Selected is not { } tile || !IsUnclear)
                return [];

            var item = _panel.Staged(tile.Item);
            var told = Desecrations.Assumed(tile.Item);
            var from = Candidates is { Count: > 0 } candidates
                ? candidates
                : Desecrations.Identify(item).Candidates is { Count: > 0 } fits ? fits
                : [.. Desecrations.Pool(item).Where(option =>
                    Desecrations.ValueOn(item) is not { } value || Desecrations.Fits(item, option, value))];

            return [.. from.Select(option => new DesecrationChoiceRow(option, told?.Id == option.Id, ""))];
        }
    }

    /// <summary>The player's answer to which mod it is - a reading of the item, not an edit.</summary>
    [RelayCommand]
    private void Tell(DesecrationChoiceRow? row)
    {
        if (row is null || _panel.Selected is not { } tile)
            return;

        _panel.Press();
        Desecrations.Assume(tile.Item, row.Option);
        Notify();
    }

    /// <summary>The eight, from the top band of the item's corruption ladder.</summary>
    public IReadOnlyList<DesecrationChoiceRow> Choices
    {
        get
        {
            if (_panel.Selected is not { } tile)
                return [];

            var item = _panel.Staged(tile.Item);
            var held = Chosen?.Option ?? (CleanStaged ? null : Held);

            return [.. Desecrations.Pool(item).Select(option => new DesecrationChoiceRow(
                option, held is not null && held.Id == option.Id, Desecrations.ChanceOf(item, option)))];
        }
    }

    /// <summary>
    /// Whether the pool can be picked from: always, on an amulet that takes one. A pick on a desecrated amulet takes
    /// its place - the clean and the new mod in one step, as the corruption's picker replaces, the pattern the other
    /// pickers follow. It was
    /// clean first, then pick.
    /// </summary>
    public bool CanPick => (CanDesecrate || IsDesecrated) && !AwaitsAnswer;

    /// <summary>
    /// Whether the amulet's mod has to be said before it is changed: desecrated, its lines cannot tell which, and no
    /// answer given - a clean would reroll the wrong lines. The list, the skull and the brush are greyed till then,
    /// the question at the top asking (greyed where blocked, as the other pickers are; a click
    /// was answered with "Require mod chosen." on the strip).
    /// </summary>
    public bool AwaitsAnswer => IsDesecrated && !CleanStaged && Held is null;

    /// <summary>Whether the brush has something to clean, and knows what.</summary>
    public bool CanClean => IsDesecrated && !AwaitsAnswer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPick))]
    [NotifyPropertyChangedFor(nameof(AwaitsAnswer))]
    [NotifyPropertyChangedFor(nameof(CanClean))]
    [NotifyPropertyChangedFor(nameof(HasPlan))]
    [NotifyPropertyChangedFor(nameof(Choices))]
    private bool _cleanStaged;

    public bool HasPlan => Chosen is not null || CleanStaged;

    /// <summary>The mod picked or rolled, on its way to the amulet: Confirm stages it at once.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlan))]
    [NotifyPropertyChangedFor(nameof(Choices))]
    private Corruptions.CorruptionDraft? _chosen;

    partial void OnIsPickerPresentChanged(bool value)
    {
        if (!value)
            return;
        OnPropertyChanged(nameof(Choices));
    }

    /// <summary>Everything the picker and the button read from the item, told again.</summary>
    public void Notify()
    {
        OnPropertyChanged(nameof(IsDesecrated));
        OnPropertyChanged(nameof(CanDesecrate));
        OnPropertyChanged(nameof(Shows));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Candidates));
        OnPropertyChanged(nameof(IsUnclear));
        OnPropertyChanged(nameof(Answers));
        OnPropertyChanged(nameof(CanPick));
        OnPropertyChanged(nameof(AwaitsAnswer));
        OnPropertyChanged(nameof(CanClean));

        // The list only while the picker is there: shut, nobody reads it, and each notification worked it out for
        // nobody - and its rows asked again whether they could be picked.
        if (IsPickerPresent)
        {
            OnPropertyChanged(nameof(Choices));
        }
    }

    [RelayCommand]
    private void OpenPicker()
    {
        _panel.Press();

        Chosen = null;
        CleanStaged = false;
        Notify();
        IsPickerOpen = true;

        _panel.Reveal(VP_ViewModel_Inv_R.DesecrationList);
    }

    [RelayCommand]
    private void ClosePicker()
    {
        Chosen = null;
        CleanStaged = false;
        IsPickerOpen = false;
        Notify();
    }

    /// <summary>
    /// Takes one from the list, its numbers rolled inside their ranges, and puts it on - in place of the one the
    /// amulet has. The picker stays open, as the corruption's does: the pick is marked in the list and named at its
    /// top, and another takes its place.
    /// </summary>
    [RelayCommand]
    private void Pick(DesecrationChoiceRow? row)
    {
        if (row is null || _panel.Selected is not { } tile || !CanPick || !TakesPlace())
            return;

        _panel.Press();
        Chosen = Corruptions.Draft(_panel.Staged(tile.Item), row.Option, new Random());
        Confirm(close: false);
    }

    /// <summary>One of the eight at random, and its numbers inside their ranges - in place of the one the amulet has.</summary>
    [RelayCommand]
    private void Roll()
    {
        if (_panel.Selected is not { } tile || !TakesPlace())
            return;

        _panel.Press();

        var random = new Random();
        var item = _panel.Staged(tile.Item);
        if (Desecrations.Roll(item, random) is { } rolled)
        {
            Chosen = Corruptions.Draft(item, rolled, random);
            Confirm(close: false);
        }
    }

    /// <summary>
    /// A desecrated amulet's mod comes off first: the clean is staged with the new one. Refused where the mod is not
    /// known - the clean would reroll the wrong lines; the list is greyed then (<see cref="AwaitsAnswer"/>).
    /// </summary>
    private bool TakesPlace()
    {
        if (!IsDesecrated || CleanStaged)
            return true;

        if (AwaitsAnswer)
            return false;

        CleanStaged = true;
        return true;
    }

    /// <summary>
    /// Cleans the mod off, staged at once as corruption's is (a pick is the change - written at once, and taken back by Ctrl + Z) - it rerolls what the mod
    /// touched. Refused while the mod is unknown: taking off the wrong one would reroll the wrong
    /// lines. The picker stays open on the clean amulet, so a new mod can be picked next.
    /// </summary>
    [RelayCommand]
    private void Clean()
    {
        if (_panel.Selected is not { } tile || !CanClean)
            return;

        _panel.Press();
        CleanStaged = true;
        Confirm(close: false);
    }

    /// <summary>
    /// Writes the plan onto the panel's copy and stages the result: the clean first, then the
    /// new mod - so a clean and its replacement confirm together, which is the reroll.
    /// </summary>
    private void Confirm(bool close = true)
    {
        if (_panel.Selected is not { } tile || !HasPlan)
            return;

        if (_panel.Pending() is not { } shadow)
            return;

        var said = new List<string>();

        if (CleanStaged && Desecrations.IsDesecrated(shadow))
        {
            if (!Desecrations.Clean(shadow, null, new Random(), out var moved))
            {
                _panel.Say(L("Require mod chosen."));
                return;
            }

            Draft.Desecration = 0;
            Draft.DesecrationMod = null;
            int lost = moved.Count(each => each.Removed);
            int rolled = moved.Count - lost;

            said.Add((rolled, lost) switch
            {
                ( > 0, > 0) => L($"Desecration cleaned, {rolled} rerolled, {lost} lost with it."),
                ( > 0, _) => L($"Desecration cleaned, {rolled} rerolled."),
                (_, > 0) => L($"Desecration cleaned, {lost} lost with it."),
                _ => L("Desecration cleaned."),
            });
        }

        if (Chosen is { } chosen && !Desecrations.IsDesecrated(shadow))
        {
            Desecrations.Apply(shadow, chosen);
            Draft.Desecration = Desecrations.ValueFor(shadow, chosen.Option);
            Draft.DesecrationMod = chosen.Option;
            said.Add(L($"Desecrated: {chosen.Option.Summary}."));
        }

        _panel.StageStatsFrom(shadow, tile);

        Chosen = null;
        CleanStaged = false;
        if (close)
            IsPickerOpen = false;

        Notify();
        _panel.Changed();

        _panel.Say(said.Count == 0
            ? L($"{tile.Name}: desecration changed.")
            : L($"{tile.Name}: {string.Join(" ", said)}"));
    }
}
