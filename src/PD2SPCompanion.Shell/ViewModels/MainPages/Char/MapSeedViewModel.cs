using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>One kept seed, as the list shows it.</summary>
public sealed partial class MapSeedRow(MapSeedNote note) : ObservableObject, IAsksTwice
{
    /// <summary>Its bin pressed once: the next press removes it (AskTwice) - a kept seed and its name are gone.</summary>
    [ObservableProperty] private bool _isArmed;

    public MapSeedNote Note { get; } = note;

    public uint Seed => Note.Seed;

    public string Remark => Note.Remark;

    /// <summary>Whether the character is on this seed.</summary>
    [ObservableProperty] private bool _isCurrent;
}

/// <summary>
/// The character's map seed, and a list of seeds worth keeping: a card on the Character page (it was a picker over the right page).
/// <para>
/// <b>A seed goes on at once.</b> Typed and applied with Enter or on leaving the box, rolled with the dice,
/// or clicked in the list - no queue and no confirm: a seed is an edit like any
/// other, undone by the history, the revert or a backup.
/// </para>
/// <para>
/// <b>The list is the person's own notes</b>, not an edit to the character: a seed is kept, renamed and
/// forgotten in the settings at once. A seed tells nobody anything, so each one is paired with a name.
/// </para>
/// </summary>
public sealed partial class MapSeedViewModel : ObservableObject
{
    public CharacterSession Session { get; }

    public EditorSettings Settings { get; }

    /// <summary>
    /// What the play mode edits: in Legit the seed is read - shown, kept under a name and found again in the
    /// list, which are the player's notes - and neither typed, rolled nor gone onto.
    /// </summary>
    public PlayRules Rules { get; }

    /// <summary>The session's changes, followed while the Character page is shown (VP_ViewModel_Char_L).</summary>
    public WhenShown Changes { get; }

    public MapSeedViewModel(CharacterSession session, EditorSettings settings, PlayRules rules)
    {
        Session = session;
        Settings = settings;
        Rules = rules;

        Order = new SortChoice(settings, "MapSeeds", N("Name"), N("Added"), N("Seed"));
        Order.Changed += Reload;

        Session.PropertyChanged += WhenSessionChanged;
        // Every edit and every move changed the session; behind the Items page the Character page's
        // cards were rebuilt for nobody - 7 ms of a move together (measured). Done while the
        // page is shown, once when it is shown next, or as it is laid out ahead (WhenShown).
        Changes = new WhenShown(Reload);
        Session.Changed += Changes.Ask;

        Reload();
    }

    private void WhenSessionChanged(object? sender, PropertyChangedEventArgs changed)
    {
        if (changed.PropertyName is nameof(CharacterSession.MapSeed)
            or nameof(CharacterSession.IsLoaded))
        {
            Reload();
        }
    }

    public ObservableCollection<MapSeedRow> Saved { get; } = [];

    /// <summary>
    /// How the list is ordered. Shared machinery - see <see cref="SortChoice"/> - so the drop box, the
    /// direction button and remembering the choice are not written out again here.
    /// </summary>
    public SortChoice Order { get; }

    public bool HasSaved => Saved.Count > 0;

    /// <summary>The character's own seed and its name, for the card's heading.</summary>
    public string CurrentSeed => Session.IsLoaded
        ? Session.MapSeed.ToString(CultureInfo.InvariantCulture)
        : "-";

    public string CurrentRemark => Named(Session.MapSeed);

    /// <summary>"9999 · Pit", or the seed alone where the list does not name it.</summary>
    public string Summary => CurrentRemark.Length > 0 ? $"{CurrentSeed} · {CurrentRemark}" : CurrentSeed;

    private string Named(uint seed) =>
        Settings.MapSeeds.FirstOrDefault(note => note.Seed == seed) is { Remark.Length: > 0 } known
            ? known.Remark
            : "";

    /// <summary>The seed box: the character's seed, until something else is typed.</summary>
    [ObservableProperty] private string _seedText = "";

    /// <summary>The name box: what the list calls the seed in the seed box.</summary>
    [ObservableProperty] private string _nameText = "";

    partial void OnSeedTextChanged(string value) => KeepCommand.NotifyCanExecuteChanged();

    partial void OnNameTextChanged(string value) => KeepCommand.NotifyCanExecuteChanged();

    /// <summary>What the seed box holds, where it is a seed the field can hold - four bytes, so up to 4,294,967,295.</summary>
    private uint? Typed => uint.TryParse(SeedText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out uint seed)
        ? seed
        : null;

    /// <summary>Keep: a seed not in the list yet, or one whose name was changed.</summary>
    public bool CanKeep => Typed is { } seed
        && (Settings.MapSeeds.FirstOrDefault(note => note.Seed == seed) is not { } kept || kept.Remark != NameText.Trim());

    /// <summary>The seed box, applied: the typed seed goes on the character, or the box goes back to it.</summary>
    public void CommitSeed()
    {
        if (Rules.EditsMapSeed && Session.IsLoaded && Typed is { } seed && seed != Session.MapSeed)
            Use(seed);
        else
            ShowCurrent();
    }

    /// <summary>
    /// A number nobody has seen, put on the character. Deliberately not drawn from the list: the list is
    /// maps already known to be worth something, and this is for finding new ones.
    /// </summary>
    [RelayCommand]
    private void RollSeed()
    {
        if (Rules.EditsMapSeed && Session.IsLoaded)
            Use((uint)Random.Shared.NextInt64(uint.MinValue, (long)uint.MaxValue + 1));
    }

    /// <summary>A kept seed, clicked: the character goes onto it.</summary>
    [RelayCommand]
    private void Choose(MapSeedRow? row)
    {
        if (Rules.EditsMapSeed && row is not null && Session.IsLoaded && row.Seed != Session.MapSeed)
            Use(row.Seed);
    }

    /// <summary>
    /// Writes the boxes to the list: a seed already there is renamed, one that is not is added. Written at
    /// once - the list is the person's notes, not part of the character.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanKeep))]
    private void Keep()
    {
        if (Typed is not { } seed)
            return;

        string name = NameText.Trim();

        if (Settings.MapSeeds.FirstOrDefault(each => each.Seed == seed) is { } already)
            already.Remark = name;
        else
            Settings.MapSeeds.Add(new MapSeedNote
            {
                Seed = seed,
                Remark = name,
                Created = DateTimeOffset.Now,
            });

        Settings.Save();
        Reload();
    }

    [RelayCommand]
    private void Forget(MapSeedRow? row)
    {
        if (row is null || AskTwice.First(row, Saved))
            return;

        Settings.MapSeeds.Remove(row.Note);
        Settings.Save();
        Reload();
    }

    private void Use(uint seed)
    {
        // Said, though the card's heading reads the new seed: the undo step and the backup are named by what the
        // strip last said (else a seed's step carries the act before it).
        string shown = seed.ToString(CultureInfo.InvariantCulture);
        Session.Messages.Post(L($"Map seed {shown}."));
        Session.MapSeed = seed;
        Reload();
    }

    /// <summary>Both boxes back on the character's own seed and its name.</summary>
    private void ShowCurrent()
    {
        SeedText = CurrentSeed;
        NameText = CurrentRemark;
    }

    private void Reload()
    {
        Saved.Clear();

        // Seed is the tie-break everywhere, so the order never wobbles between reloads: two seeds can
        // share a name, and everything the list starts with shares a creation time.
        var ordered = Order.By switch
        {
            "Added" => Settings.MapSeeds
                .OrderBy(each => each.Created)
                .ThenBy(each => each.Seed),
            "Seed" => Settings.MapSeeds
                .OrderBy(each => each.Seed)
                .ThenBy(each => each.Seed),
            _ => Settings.MapSeeds
                .OrderBy(each => each.Remark, StringComparer.OrdinalIgnoreCase)
                .ThenBy(each => each.Seed),
        };

        foreach (var note in Order.Arrange(ordered))
            Saved.Add(new MapSeedRow(note) { IsCurrent = Session.IsLoaded && note.Seed == Session.MapSeed });

        ShowCurrent();

        OnPropertyChanged(nameof(CurrentSeed));
        OnPropertyChanged(nameof(CurrentRemark));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasSaved));
        KeepCommand.NotifyCanExecuteChanged();
    }
}
