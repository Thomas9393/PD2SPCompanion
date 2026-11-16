using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PD2SPCompanion.Core.GameFiles;
using PD2SPCompanion.Core.Graphics;
using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Core.Testbed;
using PD2SPCompanion.Shell.Imaging;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// The drop panel's multiples: a stepper with an input steps through these, instead of a combo box,
/// and any whole number from 1 to <see cref="Most"/> can be typed.
/// </summary>
public static class DropSteps
{
    public static IReadOnlyList<int> Steps { get; } = [1, 2, 3, 4, 5, 10, 20, 50, 100, 200, 500, 1000];

    public const int Most = 1000;

    /// <summary>The next step above a multiple, or the most.</summary>
    public static int Up(int multiple) => Steps.FirstOrDefault(step => step > multiple, Most);

    /// <summary>The next step below a multiple, or 1.</summary>
    public static int Down(int multiple) => Steps.LastOrDefault(step => step < multiple, 1);

    /// <summary>Ctrl on a stepper: ten times the multiple, or a tenth of it - held within 1 to <see cref="Most"/>.</summary>
    public static int Leap(int multiple, bool up) => up ? Math.Min(Most, multiple * 10) : Math.Max(1, multiple / 10);

    /// <summary>Shift on a stepper: the end of the scale.</summary>
    public static int End(bool up) => up ? Most : 1;

    /// <summary>A typed multiple, where it is one: a whole number from 1 to <see cref="Most"/>.</summary>
    public static int? Parse(string? text) =>
        int.TryParse(text?.Trim(), out int typed) && typed is >= 1 and <= Most ? typed : null;
}

/// <summary>One entry of the drop panel: an item or a high rune, its multiple, and its odds before and after.</summary>
public sealed partial class DropRowViewModel : ObservableObject
{
    private readonly Action _changed;

    public DropRowViewModel(DropEntry entry, int multiple, Action changed)
    {
        Entry = entry;
        _multiple = multiple;
        _changed = changed;
        Name = entry.Name ?? ItemPresenter.BaseName(entry.Icon ?? entry.Key);
        Sprite = SpriteBitmap.From(SpriteLoader.Current?.Load(
            D2SLib.Core.TXT.ItemsTXT.GetByCode(entry.Icon ?? entry.Key)?.Get("invfile")?.Value?.Trim()));
    }

    public DropEntry Entry { get; }

    public string Name { get; }

    public Bitmap? Sprite { get; }

    /// <summary>The odds, a column each: "Hell 1 in 675 → 1 in 34".</summary>
    [ObservableProperty] private string _odds = "";

    private int _multiple;

    /// <summary>The multiple: 1 to <see cref="DropSteps.Most"/>.</summary>
    public int Multiple
    {
        get => _multiple;
        set
        {
            if (value == _multiple)
                return;
            _multiple = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MultipleText));
            _changed();
        }
    }

    /// <summary>The box: the multiple as typed, taken as soon as it is one.</summary>
    public string MultipleText
    {
        get => Multiple.ToString();
        set
        {
            if (DropSteps.Parse(value) is { } typed)
                Multiple = typed;
        }
    }

    [RelayCommand]
    private void StepUp() => Multiple = DropSteps.Up(Multiple);

    [RelayCommand]
    private void StepDown() => Multiple = DropSteps.Down(Multiple);

    /// <summary>Ctrl + click on a stepper: x10 or /10. Shift + click: the most, or 1.</summary>
    public void Leap(bool up) => Multiple = DropSteps.Leap(Multiple, up);

    public void Jump(bool up) => Multiple = DropSteps.End(up);

    /// <summary>Puts a multiple without telling the panel: a preset or a group setting every row at once.</summary>
    internal void Put(int multiple)
    {
        _multiple = multiple;
        OnPropertyChanged(nameof(Multiple));
        OnPropertyChanged(nameof(MultipleText));
    }
}

/// <summary>A group of the drop panel's entries - special items, high runes - with a stepper that sets them all.</summary>
public sealed partial class DropGroupViewModel(string title, string hint, IReadOnlyList<DropRowViewModel> rows, Action changed)
    : ObservableObject
{
    public string Title { get; } = title;

    /// <summary>What the odds in this group are per; empty for none.</summary>
    public string Hint { get; } = hint;

    public bool HasHint => Hint.Length > 0;

    /// <summary>What the group's multiples cost other drops, where it is worth saying (the old boxes' warning sign).</summary>
    [ObservableProperty] private string? _warning;

    public IReadOnlyList<DropRowViewModel> Rows { get; } = rows;

    /// <summary>A switch of the group's own, where it has one: "Leave out chipped and flawed gems" on the runes and gems.</summary>
    public string? SwitchText { get; set; }

    public bool HasSwitch => SwitchText is not null;

    internal Func<bool>? SwitchGet { get; set; }

    internal Action<bool>? SwitchSet { get; set; }

    public bool SwitchOn
    {
        get => SwitchGet?.Invoke() ?? false;
        set => SwitchSet?.Invoke(value);
    }

    /// <summary>Folded until opened, as items and runes normally are, then as it was left.</summary>
    [ObservableProperty] private bool _isOpen = PD2SPCompanion.Shell.Services.Settings.FoldMemory.IsOpen(nameof(DropGroupViewModel) + title, false);

    partial void OnIsOpenChanged(bool value) => PD2SPCompanion.Shell.Services.Settings.FoldMemory.Keep(nameof(DropGroupViewModel) + Title, value);

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;

    /// <summary>The multiple every row has - null while they differ, the box then empty under "Mixed".</summary>
    private int? Common
    {
        get
        {
            var multiples = Rows.Select(row => row.Multiple).Distinct().ToList();
            return multiples.Count == 1 ? multiples[0] : null;
        }
    }

    /// <summary>The group's box: typed, it is every row's multiple.</summary>
    public string MultipleText
    {
        get => Common?.ToString() ?? "";
        set
        {
            if (DropSteps.Parse(value) is { } typed && typed != Common)
                Set(typed);
        }
    }

    /// <summary>Up a step from the highest row, down a step from the lowest - from the one they share where they agree.</summary>
    [RelayCommand]
    private void StepUp() => Set(DropSteps.Up(Common ?? Rows.Max(row => row.Multiple)));

    [RelayCommand]
    private void StepDown() => Set(DropSteps.Down(Common ?? Rows.Min(row => row.Multiple)));

    /// <summary>Ctrl + click on the group's stepper: every row x10 or /10, from the highest or the lowest. Shift + click: the end.</summary>
    public void Leap(bool up) =>
        Set(DropSteps.Leap(Common ?? (up ? Rows.Max(row => row.Multiple) : Rows.Min(row => row.Multiple)), up));

    public void Jump(bool up) => Set(DropSteps.End(up));

    private void Set(int multiple)
    {
        foreach (var row in Rows)
            row.Put(multiple);
        changed();
    }

    internal void Said()
    {
        OnPropertyChanged(nameof(MultipleText));
        OnPropertyChanged(nameof(SwitchOn));
    }
}

/// <summary>One of the drop panel's presets: Vanilla, or a kept one.</summary>
public sealed class DropPresetRow(string name, string summary, IReadOnlyDictionary<string, int> boosts, DropPreset? preset)
    : PresetRowViewModel(name, summary)
{
    /// <summary>The multiples it sets.</summary>
    public IReadOnlyDictionary<string, int> Boosts { get; } = boosts;

    /// <summary>The kept preset; null for the built-in ones.</summary>
    public DropPreset? Preset { get; } = preset;

    /// <summary>The built-in ones are neither renamed nor deleted.</summary>
    public override bool CanForget => Preset is not null;
}

/// <summary>
/// The drop panel: a multiple for each of PD2's
/// special items and each high rune, every entry's odds in each difficulty before and after, and presets - Vanilla
/// built in, and as many kept as wanted, as the search's. Over the Experimental page; what it holds is the page's
/// drop choice, written by APPLY with the rest.
/// <para>
/// The odds are the tables' (DropBoosts): a special item's chance as its row has it, a rune's per rune from an Act
/// 5 champion or unique. The weighted materials keep their box on the page, and the event rows are left out (the design
/// note lists them).
/// </para>
/// </summary>
public sealed partial class DropPanelViewModel : ObservableObject, IPresetSection
{
    private readonly ISFXManager? _sfxManager;
    private readonly Func<string> _gameRoot;
    private DropBook? _book;
    private string _bookRoot = "";

    public DropPanelViewModel(ISFXManager? sfxManager, EditorSettings settings, Func<string> gameRoot)
    {
        _sfxManager = sfxManager;
        _gameRoot = gameRoot;
        Settings = settings;
        Order = new SortChoice(settings, "DropPresets", N("Name"), N("Added"));
        Order.Changed += Reload;
    }

    public EditorSettings Settings { get; }

    /// <summary>The multiples by entry, as the page writes them; an entry at 1x is left out.</summary>
    public IReadOnlyDictionary<string, int> Boosts => _boosts;

    private Dictionary<string, int> _boosts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The multiples changed: the page's line and APPLY follow.</summary>
    public event Action? Changed;

    public ObservableCollection<DropGroupViewModel> Groups { get; } = [];

    /// <summary>Set by the page from what is installed; the rows follow when the panel opens.</summary>
    public void Take(IReadOnlyDictionary<string, int>? boosts)
    {
        _boosts = (boosts ?? new Dictionary<string, int>())
            .Where(boost => boost.Value > 1)
            .ToDictionary(boost => boost.Key, boost => boost.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var row in Groups.SelectMany(group => group.Rows))
            row.Put(DropBoosts.Of(_boosts, row.Entry.Key));
        Refresh();
    }

    // ---- open and close -------------------------------------------------------------------------------

    [ObservableProperty] private bool _isPresent;

    [ObservableProperty] private bool _isOpen;

    partial void OnIsOpenChanged(bool value) => Presence.Keep(value, () => IsOpen, on => IsPresent = on);

    /// <summary>Why the panel could not open: the game's tables would not read.</summary>
    public string? Problem { get; private set; }

    [RelayCommand]
    private void Open()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        if (!Build())
            return;

        IsOpen = true;
    }

    [RelayCommand]
    private void Close()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        IsOpen = false;
    }

    /// <summary>The rows, from the installed table - read once a game folder, then answered from memory.</summary>
    private bool Build()
    {
        string root = _gameRoot();
        if (_book is null || root != _bookRoot)
        {
            try
            {
                using var game = GameData.Open(root);
                _book = DropBoosts.Book(game);
                _bookRoot = root;
            }
            catch (Exception problem)
            {
                Log.Caught(problem);
                Problem = problem.Message;
                OnPropertyChanged(nameof(Problem));
                return false;
            }

            Groups.Clear();
            // The items, the shard and the keys in one group - each row saying what its odds count; the corrupted zones' and the maps'
            // drops a group of their own.
            Groups.Add(Group([DropKind.Special, DropKind.Material], L("Special items"), L("Drop chance per monster kill")));
            Groups.Add(Group([DropKind.Zone], L("Corrupted zones and maps"), L("Drop chance per monster kill there")));
            // Every rune and the two gem tiers: all runes, and of the gems only flawless and perfect are touched.
            var runes = Group([DropKind.Gem, DropKind.Rune], L("Runes and gems"), L("Drop chance from an Act 5 Champion or Unique monster"));

            // Chipped and flawed gems out of the champions' and uniques' drops.
            runes.SwitchText = L("Leave out chipped and flawed gems");
            runes.SwitchGet = () => LowGemsOut;
            runes.SwitchSet = LeaveLowGemsOut;
            Groups.Add(runes);
        }

        Reload();
        Refresh();
        return true;
    }

    /// <summary>
    /// A group of these kinds, in their order - the Worldstone Shard first of all, the most
    /// common and iconic PD2 item - the rest as the tables give them.
    /// </summary>
    private DropGroupViewModel Group(DropKind[] kinds, string title, string hint) =>
        new(title, hint,
            [.. kinds.SelectMany(kind => _book!.Entries.Where(entry => entry.Kind == kind))
                .OrderBy(entry => entry.Key == "wss" ? 0 : 1)
                .Select(entry => new DropRowViewModel(entry, DropBoosts.Of(_boosts, entry.Key), RowChanged))],
            RowChanged);

    private void RowChanged()
    {
        bool gemsOut = LowGemsOut;
        _boosts = Groups.SelectMany(group => group.Rows)
            .Where(row => row.Multiple > 1)
            .ToDictionary(row => row.Entry.Key, row => row.Multiple, StringComparer.OrdinalIgnoreCase);
        if (gemsOut)
            _boosts[DropBoosts.LowGemsOut] = 2;
        Refresh();
        Changed?.Invoke();
    }

    /// <summary>Whether chipped and flawed gems are left out of the champions' drops: kept with the multiples (DropBoosts.LowGemsOut).</summary>
    private bool LowGemsOut => DropBoosts.Of(_boosts, DropBoosts.LowGemsOut) > 1;

    private void LeaveLowGemsOut(bool on)
    {
        if (on == LowGemsOut)
            return;

        if (on)
            _boosts[DropBoosts.LowGemsOut] = 2;
        else
            _boosts.Remove(DropBoosts.LowGemsOut);
        Refresh();
        Changed?.Invoke();
    }

    /// <summary>Every row's odds, every group's box and the preset line, after a change.</summary>
    private void Refresh()
    {
        if (_book is not null)
        {
            var odds = _book.Odds(_boosts);
            foreach (var row in Groups.SelectMany(group => group.Rows))
                row.Odds = odds.TryGetValue(row.Entry.Key, out var columns) ? Said(columns) : "";

            // What the multiples cost, said on the group's header with the warning sign the old boxes had,
            // from 15% on - the old high rune box said nothing at 5x (10%) and warned at 10x.
            foreach (var group in Groups)
            {
                // Each group's own multiples, so a group warns only of what its own rows cost.
                var own = group.Rows.Where(row => row.Multiple > 1)
                    .ToDictionary(row => row.Entry.Key, row => row.Multiple, StringComparer.OrdinalIgnoreCase);
                var said = new List<string>();
                if (group.Rows.Any(row => row.Entry.Kind == DropKind.Rune) && Loss(_book.OtherRunesKept(own)) is >= 15 and int lost)
                    said.Add(L($"Other runes drop about {lost}% less often"));
                if (_book.WorstCost(own) is { } cost && Loss(cost.Kept) is >= 15 and int loss)
                    said.Add(L($"{SourceOf(cost.Row)}: other drops about {loss}% less often"));
                group.Warning = said.Count > 0 ? string.Join("\n", said) : null;
            }
        }

        foreach (var group in Groups)
            group.Said();

        MarkCurrentRow();
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasBoosts));
        OnPropertyChanged(nameof(HasSummary));
        OnPropertyChanged(nameof(KeepHint));
        OnPropertyChanged(nameof(CurrentPresetName));
        KeepCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// The odds line: the kills counted said once in front ("Per undead kill: Normal – · Hell 1 in 22,500 → 1 in
    /// 980"), then a column each.
    /// </summary>
    private static string Said(IReadOnlyList<DropOdds> columns)
    {
        var sources = columns.Select(column => column.Source).Distinct().ToList();
        var froms = columns.Select(column => column.From).Distinct().ToList();
        string lead = sources is [{ } source] ? L($"Per {source} kill: ")
            : froms is [{ } from] ? L($"From {from}: ")
            : "";

        return lead + string.Join("  ·  ", columns.Select(column =>
        {
            string label = column.Column switch
            {
                "N" => L("Normal"),
                "NM" => L("Nightmare"),
                "H" => L("Hell"),
                "All" => L("All difficulties"),
                // "Tier 1: 1 in 5,440" - the colon keeps the tier's number from running into the odds'.
                _ => L($"Tier {column.Column[1..]}:"),
            };
            if (column.Before is not { } before || column.After is not { } after)
                return $"{label} –";
            return Math.Abs(after - before) < 1e-12
                ? $"{label} {OneIn(before)}"
                : $"{label} {OneIn(before)} → {OneIn(after)}";
        }));
    }

    private static string OneIn(double chance) => L($"1 in {1 / chance:N0}");

    /// <summary>A share kept, as the loss in whole percent to the nearest five.</summary>
    private static int Loss(double kept) => (int)Math.Round((1 - kept) * 20) * 5;

    private static readonly System.Text.RegularExpressions.Regex GoodClass =
        new(@"^Act (?<act>[1-5])( \((?<difficulty>N|H)\))? Good$");

    /// <summary>A class named as a player knows it: "Act 5 (H) Good" is Hell's Act 5 champions and uniques.</summary>
    private string SourceOf(string row)
    {
        if (GoodClass.Match(row) is { Success: true } good)
        {
            string difficulty = good.Groups["difficulty"].Value switch { "N" => L("Nightmare"), "H" => L("Hell"), _ => L("Normal") };
            string act = good.Groups["act"].Value;
            return L($"{difficulty} Act {act} champions and uniques");
        }

        return _book?.Entries.SelectMany(entry => entry.Columns).FirstOrDefault(column => column.Row == row)?.Source ?? row;
    }

    // ---- the summary --------------------------------------------------------------------------------

    /// <summary>What is boosted, in a line: "Vanilla", or the entries and their multiples.</summary>
    public string Summary => Describe(_boosts);

    /// <summary>Whether anything is boosted: the presets' line under the header says what, and only then.</summary>
    public bool HasBoosts => _boosts.Count > 0;

    public bool HasSummary => HasBoosts;

    public System.Collections.IEnumerable PresetRows => Saved;

    private string Describe(IReadOnlyDictionary<string, int> boosts)
    {
        var raised = boosts.Where(boost => boost.Value > 1).ToList();
        if (raised.Count == 0)
            return L("Vanilla");

        string NameOf(string key) =>
            _book?.Entries.FirstOrDefault(entry => entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) is { } entry
                ? entry.Name ?? ItemPresenter.BaseName(entry.Icon ?? entry.Key)
                : key;

        var shown = raised.Take(3).Select(boost => boost.Key == DropBoosts.LowGemsOut
            ? L("No chipped or flawed gems")
            : $"{NameOf(boost.Key)} {boost.Value}x");
        return raised.Count > 3
            ? L($"{string.Join(", ", shown)} and {raised.Count - 3} more")
            : string.Join(", ", shown);
    }

    // ---- presets ------------------------------------------------------------------------------------

    public ObservableCollection<DropPresetRow> Saved { get; } = [];

    public SortChoice Order { get; }

    /// <summary>The kept preset these multiples are, if any is.</summary>
    private DropPreset? Matching() => Settings.DropPresets.FirstOrDefault(kept => kept.Holds(_boosts));

    /// <summary>The preset row's name: Vanilla, a kept one's, or none.</summary>
    public string CurrentPresetName =>
        _boosts.Count == 0 ? L("Vanilla")
        : Matching()?.Name is { Length: > 0 } name ? name
        : L("No preset");

    /// <summary>The name box: the preset the multiples are, until something else is typed.</summary>
    [ObservableProperty] private string _nameText = "";

    partial void OnNameTextChanged(string value)
    {
        KeepCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(KeepHint));
    }

    /// <summary>Keep: a name, something boosted, and not already kept under that name.</summary>
    public bool CanKeep =>
        NameText.Trim() is { Length: > 0 } name && _boosts.Count > 0 && Matching()?.Name != name && !RenameTaken();

    /// <summary>
    /// A rename onto the name of another kept preset: it would leave two of one name, so
    /// it is not offered.
    /// </summary>
    private bool RenameTaken() =>
        Matching() is { } kept && Named() is { } other && !ReferenceEquals(kept, other);

    /// <summary>The presets, in the panel, with no new panel to open; open until folded.</summary>
    [ObservableProperty] private bool _presetsOpen = PD2SPCompanion.Shell.Services.Settings.FoldMemory.IsOpen(nameof(DropPanelViewModel) + nameof(PresetsOpen));

    partial void OnPresetsOpenChanged(bool value) => PD2SPCompanion.Shell.Services.Settings.FoldMemory.Keep(nameof(DropPanelViewModel) + nameof(PresetsOpen), value);

    [RelayCommand]
    private void TogglePresets() => PresetsOpen = !PresetsOpen;

    private void Reload()
    {
        Saved.Clear();

        // Vanilla first and always, the way back.
        Saved.Add(new DropPresetRow(L("Vanilla"), L("Every entry 1x"), new Dictionary<string, int>(), null));

        var ordered = Order.By switch
        {
            "Added" => Settings.DropPresets.OrderBy(each => each.Created),
            _ => Settings.DropPresets
                .OrderBy(each => each.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(each => each.Created),
        };

        foreach (var preset in Order.Arrange(ordered))
            Saved.Add(new DropPresetRow(preset.Name, Describe(preset.Boosts), preset.Boosts, preset));

        MarkCurrentRow();
    }

    private void MarkCurrentRow()
    {
        foreach (var row in Saved)
            row.IsCurrent = DropPreset.Same(row.Boosts, _boosts);
    }

    /// <summary>What Keep will do: rename the preset the multiples are, update the one of that name, or keep a new one.</summary>
    public string KeepHint =>
        RenameTaken() ? L("Name taken")
        : Matching() is not null ? L("Rename preset")
        : Named() is not null ? L("Update preset")
        : L("Keep preset");

    /// <summary>The kept preset the name box names, if any.</summary>
    private DropPreset? Named() =>
        Settings.DropPresets.FirstOrDefault(kept => kept.Name.Equals(NameText.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The name box written to the list: the preset the multiples already are is renamed; a kept preset of that
    /// name takes these multiples (presets lacked tools such as an edit); else they are kept
    /// under it.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanKeep))]
    private void Keep()
    {
        string name = NameText.Trim();
        if (name.Length == 0 || _boosts.Count == 0)
            return;

        if (RenameTaken())
            return;

        if (Matching() is { } kept)
        {
            kept.Name = name;
        }
        else if (Named() is { } same)
        {
            same.Boosts = new Dictionary<string, int>(_boosts, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            Settings.DropPresets.Add(new DropPreset
            {
                Name = name,
                Created = DateTimeOffset.Now,
                Boosts = new Dictionary<string, int>(_boosts, StringComparer.OrdinalIgnoreCase),
            });
        }

        Settings.Save();
        Reload();
        Refresh();
    }

    /// <summary>A preset clicked: the multiples become it, and the name box takes its name where it is a kept one.</summary>
    [RelayCommand]
    private void Choose(PresetRowViewModel? chosen)
    {
        if (chosen is not DropPresetRow row)
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        Take(row.Boosts);
        NameText = row.Preset?.Name ?? "";
        Changed?.Invoke();
    }

    [RelayCommand]
    private void Forget(PresetRowViewModel? chosen)
    {
        if ((chosen as DropPresetRow)?.Preset is not { } preset || AskTwice.First(chosen!, PresetRows))
            return;

        Settings.DropPresets.Remove(preset);
        Settings.Save();
        Reload();
        Refresh();
    }
}
