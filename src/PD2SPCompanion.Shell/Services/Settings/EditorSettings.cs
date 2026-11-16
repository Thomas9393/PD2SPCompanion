using CommunityToolkit.Mvvm.ComponentModel;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PD2SPCompanion.Shell.Services.Settings;

/// <summary>
/// The editor's own preferences, kept between runs.
/// <para>
/// Stored in the editor's own data folder (<c>EditorData</c>: <c>Data</c> beside the program),
/// never beside the game. The install is read-only to this program apart from the saves
/// themselves, and a settings file in the game folder would be both a write we promised not to
/// make and something a mod update could remove.
/// </para>
/// </summary>
public sealed partial class EditorSettings : ObservableObject
{
    /// <summary>
    /// How many older backups each character keeps - past the last fifteen minutes, which are kept
    /// whole: Never, 5, 10, 15 or 20, 20 at first (it was 10 to 50 by fives).
    /// "Never" keeps the recent ones only (SaveWriter.Prune with 0).
    /// </summary>
    public static IReadOnlyList<int> BackupChoices { get; } = [0, 5, 10, 15, 20];

    public static IReadOnlyList<string> BackupChoiceNames { get; } = [L("Never"), "5", "10", "15", "20"];

    public const int DefaultKept = 20;

    /// <summary>A stored count put on the nearest choice - a hand-edited file may hold any number.</summary>
    public static int NormalizedKept(int stored) =>
        stored < 0 ? DefaultKept : BackupChoices.MinBy(offered => Math.Abs(offered - stored));

    /// <summary>
    /// The count the settings file held before "Never" (BackupsToKeep, 10 to 50): 0 there meant "keep
    /// every one", never "Never", so it reads as the default; anything over 20 as 20.
    /// </summary>
    private static int LegacyKept(int stored) => stored <= 0 ? DefaultKept : NormalizedKept(stored);

    /// <summary>
    /// Where Diablo II is installed - the folder holding <c>d2data.mpq</c> and
    /// <c>ProjectD2</c>.
    /// <para>
    /// Empty until it has been chosen, which is what makes the first run ask. It is stored
    /// rather than found every time because finding it is a guess and this is not: once
    /// someone has said which folder, that is the answer until they say otherwise.
    /// </para>
    /// </summary>
    [ObservableProperty] private string _gameFolder = "";

    /// <summary>
    /// Whether a folder has been settled on at all. False on a first run, and false again
    /// if the folder it names has since gone.
    /// </summary>
    public bool HasGameFolder => GameLocation.Holds(GameFolder);

    [ObservableProperty] private int _backupsToKeep = DefaultKept;

    /// <summary>
    /// How many of the vault's older copies are kept - one is taken for each session that changed it. A number
    /// of its own: a vault's copy is far larger than a character's. The same choices.
    /// </summary>
    [ObservableProperty] private int _vaultBackupsToKeep = DefaultVaultKept;

    public const int DefaultVaultKept = 10;

    /// <summary>
    /// Whether to mark values the game would not have produced.
    /// <para>
    /// A mark, never a refusal: the editor still writes whatever is typed, and the file
    /// limit is the only thing that clamps. Turned off by anyone who would rather edit
    /// without being told.
    /// </para>
    /// </summary>
    [ObservableProperty] private bool _flagIllegitimateValues = true;

    /// <summary>
    /// Whether anything moves: fades, slides, the page cross-fade, the character GIFs. On to begin
    /// with; off, the whole app is still - see <see cref="Motion"/>.
    /// </summary>
    [ObservableProperty] private bool _animations = true;

    /// <summary>
    /// Whether the editor makes a sound: the game's button click, toggled in the Display
    /// section. On to begin with; off, it is silent - see SFXManager.
    /// </summary>
    [ObservableProperty] private bool _sounds = true;


    /// <summary>
    /// How each vault page is laid out, by the page's name - "Left", "Right": its folder tree, its strip of
    /// folders and its map each shown or put away, and how wide the tree is (every one
    /// a toggle of its own, from a minimalist view to full on, kept a page).
    /// </summary>
    public Dictionary<string, VaultLook> VaultLooks { get; } = [];

    /// <summary>What a vault page looks like before anyone has changed it: the map as the old one setting had it.</summary>
    private bool _vaultMinimapBefore = true;

    /// <summary>
    /// A vault page's look: as it was left, or everything shown where it never was changed. With
    /// PD2SPC_FOLDS=off (the UI tests) as a new install has it, whatever the player keeps - and not kept.
    /// </summary>
    public VaultLook VaultLookOf(string page)
    {
        if (LooksOff)
            return _looksUnkept.TryGetValue(page, out var fresh) ? fresh : _looksUnkept[page] = new VaultLook();

        if (!VaultLooks.TryGetValue(page, out var look))
            VaultLooks[page] = look = new VaultLook { Minimap = _vaultMinimapBefore };

        return look;
    }

    /// <summary>
    /// The window as it was left - where, how large, maximized or not (it opened at its least
    /// size in the screen's middle every time). Not the page it showed: that was kept too for a day, and a new start
    /// opens on the home page again, deliberately (within
    /// a session the page comes back as the game gives a character back,
    /// MainViewModel.Resume). Not with PD2SPC_FOLDS=off (the UI tests): they meet the window as a new install has it.
    /// </summary>
    public WindowPlace? Window { get; set; }

    /// <summary>Whether the window's place is kept and given back: not in the UI tests.</summary>
    public static bool KeepsPlace => !LooksOff;

    private static readonly bool LooksOff =
        string.Equals(Environment.GetEnvironmentVariable("PD2SPC_FOLDS"), "off", StringComparison.OrdinalIgnoreCase);

    private readonly Dictionary<string, VaultLook> _looksUnkept = [];

    /// <summary>A vault page's look has been changed: written at once.</summary>
    public void KeepVaultLooks()
    {
        if (!LooksOff)
            Save();
    }

    /// <summary>Every section that folds, by its name, as the user left it (<see cref="FoldMemory"/>).</summary>
    public Dictionary<string, bool> Folds { get; } = [];

    /// <summary>Every part drawn to a height of the user's, by its name, in pixels (<see cref="FoldMemory.Size"/>).</summary>
    public Dictionary<string, int> Sizes { get; } = [];

    /// <summary>
    /// How many steps undo and redo keep for the open character (SessionHistory): 10 to 50 in tens
    /// (it was a fixed 200, never shown).
    /// </summary>
    [ObservableProperty] private int _undoSteps = DefaultUndoSteps;

    /// <summary>
    /// The scale the window is drawn at over Windows' own, in percent - 0 is Automatic (<see cref="Settings.UiScale"/>):
    /// the main monitor's look carried to other screens.
    /// </summary>
    [ObservableProperty] private int _uiScale;

    /// <summary>
    /// The mode whose vault was last open - what the vault opens with when no
    /// character is loaded.
    /// </summary>
    [ObservableProperty] private bool _vaultHardcore;

    /// <summary>
    /// The vault each mode last had open, by name: a mode has several, and opens
    /// on the one it was left on. Empty for the mode's first.
    /// </summary>
    [ObservableProperty] private string _vaultNameSoftcore = "";

    [ObservableProperty] private string _vaultNameHardcore = "";

    /// <summary>
    /// The inventory lock: nothing of the inventory is picked, dragged, cut or stored, as a stash page's lock. Off
    /// unless turned on.
    /// </summary>
    [ObservableProperty] private bool _inventoryLock;

    /// <summary>
    /// Whether an item's tooltip shows, in a second box beside it, what is worn where a right click would put
    /// the item (TipActions): turned on and off by Tab over such a tooltip, and kept as left. Off unless turned on.
    /// </summary>
    [ObservableProperty] private bool _compareOnHover;

    /// <summary>
    /// Whether the Experimental page's online notice has been dismissed. Said once, ever: the
    /// modded tables apply only when the game is started with LAUNCH or the shortcut, and online
    /// the server's tables decide - so it is advice, not a condition to keep repeating.
    /// Once dismissed, a later Apply does not bring it back, and neither does the
    /// sidebar's mark.
    /// </summary>
    [ObservableProperty] private bool _onlineWarningDismissed;

    /// <summary>
    /// The PD2 build the saves were last checked against (PatchGuard): empty until the first check.
    /// A different build installed holds writes until the saves read back with it.
    /// </summary>
    [ObservableProperty] private string _verifiedBuild = "";

    /// <summary>
    /// The season whose new-season notice has been answered, migrated or put off: it is asked once a
    /// season, and the Settings row stays for later. Empty until the first.
    /// </summary>
    [ObservableProperty] private string _migrationNoticeSeason = "";

    /// <summary>
    /// The release number the editor last ran as (AppVersion.Number): started at a newer one, it says
    /// "Updated to ..." once (WhatsNewService). Empty until the first run.
    /// </summary>
    [ObservableProperty] private string _lastRunVersion = "";

    /// <summary>
    /// The language the editor shows: a pack's code, English when empty or
    /// when no such pack is found. Read once at start (<see cref="StoredLanguage"/>), so a change waits for
    /// the next one.
    /// </summary>
    [ObservableProperty] private string _language = PD2SPCompanion.Core.Localization.Language.English;

    /// <summary>
    /// Only the stored game folder, read at the program's first lines: the game's tables are begun on a worker
    /// before the settings are loaded (GameAhead). Empty where none is stored or the file cannot be read.
    /// </summary>
    public static string StoredGameFolder()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<Stored>(File.ReadAllText(FilePath))?.GameFolder is { Length: > 0 } folder)
                return folder;
        }
        catch (Exception)
        {
            // An unreadable file is the defaults: no folder, and the first run's setup asks for one.
        }

        return "";
    }

    /// <summary>
    /// Only the stored language, read before anything else: the language is put in use before the first
    /// view is built, which is before the rest of the settings are loaded.
    /// </summary>
    public static string StoredLanguage()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<Stored>(File.ReadAllText(FilePath))?.Language is { Length: > 0 } code)
                return code;
        }
        catch (Exception)
        {
            // An unreadable file is the defaults, English among them.
        }

        return PD2SPCompanion.Core.Localization.Language.English;
    }

    public const int DefaultUndoSteps = 50;

    /// <summary>What the undo history may keep: 10 to 50, in tens.</summary>
    public static IReadOnlyList<int> UndoStepChoices { get; } = [10, 20, 30, 40, 50];

    private static int NearestUndoSteps(int steps) => UndoStepChoices.MinBy(offered => Math.Abs(offered - steps));

    /// <summary>
    /// How each list was last ordered, by a key the list chooses for itself. One place for all
    /// of them, so a new sortable list costs no new setting. See <c>SortChoice</c>.
    /// </summary>
    public Dictionary<string, StoredSort> Sorts { get; } = [];

    /// <summary>What was remembered for this list, or an empty choice where nothing was.</summary>
    public StoredSort SortFor(string key) =>
        Sorts.TryGetValue(key, out var stored) ? stored : new StoredSort();

    public void RememberSort(string key, string by, bool descending)
    {
        Sorts[key] = new StoredSort { By = by, Descending = descending };
        Save();
    }

    /// <summary>
    /// The stash pages kept out of Store all, Select all and moving out (a page
    /// kept for maps, say), by a key the page builds - the character's own pages by character, the
    /// shared ones by their file. See <c>StashPageChoice.LockKey</c>. The inventory has its own lock.
    /// </summary>
    public HashSet<string> LockedPages { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsPageLocked(string key) => LockedPages.Contains(key);

    public void LockPage(string key, bool locked)
    {
        if (locked ? LockedPages.Add(key) : LockedPages.Remove(key))
            Save();
    }

    /// <summary>A renamed character keeps its pages' locks, which are kept by its name.</summary>
    public void RenamePageLocks(string oldName, string newName)
    {
        string from = $"character:{oldName}:", to = $"character:{newName}:";
        var moving = LockedPages.Where(key => key.StartsWith(from, StringComparison.OrdinalIgnoreCase)).ToList();
        if (moving.Count == 0)
            return;

        foreach (var key in moving)
        {
            LockedPages.Remove(key);
            LockedPages.Add(to + key[from.Length..]);
        }

        Save();
    }

    /// <summary>
    /// Map seeds worth keeping, each with whatever the person wrote about it.
    /// <para>
    /// A seed is a plain 32-bit number and says nothing about the world it makes, so the
    /// remark is the whole point of the list - "short path to Baal", "good cow level". The
    /// idea and the shape are taken from MapEasy, whose own library is the same two columns.
    /// </para>
    /// </summary>
    public ObservableCollection<MapSeedNote> MapSeeds { get; } = [];

    /// <summary>
    /// Vault searches worth keeping, each under a name - the map seed list's idea, on the
    /// vault's search panel.
    /// </summary>
    public ObservableCollection<SearchPreset> SearchPresets { get; } = [];

    /// <summary>The drop panel's kept multiples, each under a name; Vanilla is built in, not here.</summary>
    public ObservableCollection<DropPreset> DropPresets { get; } = [];

    /// <summary>
    /// What each trade currency is worth in HR, by item code - the player's own price table for
    /// Virtual Trading. The editor ships no rates: what a Ber is worth is the player's call, and
    /// a currency with no entry is simply not priced.
    /// </summary>
    public Dictionary<string, decimal> TradePrices { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The play mode, by name: Legit, Casual, Free - the fixed presets - or Custom, and then
    /// <see cref="CustomName"/> says which. Casual until chosen, which is also where someone who ran the
    /// editor before modes existed starts.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Policy))]
    [NotifyPropertyChangedFor(nameof(ModeLabel))]
    private string _playMode = PlayPolicy.CasualName;

    /// <summary>The custom mode that is the mode, while the mode is Custom. The presets are never edited.</summary>
    public PlayPolicy? CustomPolicy { get; private set; }

    /// <summary>
    /// The custom play modes, each under a name: made and edited in the Custom panel,
    /// and added to where a vault brings one this editor does not have.
    /// </summary>
    public ObservableCollection<PolicyPreset> CustomPresets { get; } = [];

    /// <summary>The name of the custom mode that is the mode, or was the last time the mode was Custom.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeLabel))]
    private string _customName = "";

    /// <summary>The mode in a word: "Legit", or "Custom - SSF rules" - what a vault keeps of it.</summary>
    public string ModeLabel => PlayMode == PlayPolicy.CustomName ? PlayPolicy.CustomLabel(CustomName) : PlayMode;

    /// <summary>A mode was made the mode, by any road: raised once it is whole - name, rules and all.</summary>
    public event Action? ModeChosen;

    /// <summary>The custom mode kept under a name, if one is.</summary>
    public PolicyPreset? Custom(string name) =>
        CustomPresets.FirstOrDefault(kept => kept.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Keeps rules under a name: a custom mode of that name takes them, else one is added. Not saved here.</summary>
    public PolicyPreset KeepCustom(string name, PlayPolicy policy)
    {
        if (Custom(name) is { } kept)
        {
            kept.Policy = policy;
            return kept;
        }

        var added = new PolicyPreset { Name = name.Trim(), Created = DateTimeOffset.Now, Policy = policy };
        CustomPresets.Add(added);
        return added;
    }

    /// <summary>A name no custom mode has: "1", "2", ... - for one confirmed with no name given.</summary>
    private string FreeCustomName()
    {
        for (int n = 1; ; n++)
        {
            if (Custom(n.ToString(System.Globalization.CultureInfo.InvariantCulture)) is null)
                return n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Makes a kept custom mode the mode, by its name. False where there is none of that name.</summary>
    public bool ChooseCustom(string name)
    {
        if (Custom(name) is not { } kept)
            return false;

        ChoosePolicy(kept.Policy, kept.Name);
        return true;
    }

    /// <summary>
    /// A custom mode renamed. The mode itself follows where it is the one renamed - and so, through
    /// <see cref="ModeChosen"/>, does what keeps the mode's name.
    /// </summary>
    public void RenameCustom(PolicyPreset preset, string name)
    {
        bool current = PlayMode == PlayPolicy.CustomName && preset.Name.Equals(CustomName, StringComparison.OrdinalIgnoreCase);
        preset.Name = name.Trim();
        if (current)
            CustomName = preset.Name;

        Save();
        if (current)
            ModeChosen?.Invoke();
    }

    /// <summary>A custom mode deleted from the list. The caller has made sure it is not the mode.</summary>
    public void ForgetCustom(PolicyPreset preset)
    {
        CustomPresets.Remove(preset);
        Save();
    }

    /// <summary>What the editor lets the player do now.</summary>
    public PlayPolicy Policy => PlayPolicy.Preset(PlayMode) ?? CustomPolicy ?? PlayPolicy.Casual;

    /// <summary>
    /// Makes a policy the mode: a preset's name where it is exactly one, otherwise Custom - so a
    /// Custom that matches Legit is Legit, and nothing extra is stored. A custom mode is kept under
    /// <paramref name="name"/>: with none given, under the name of the custom mode that is the mode now -
    /// its rules edited in place - or, where the mode is not Custom, a number no other has.
    /// </summary>
    public void ChoosePolicy(PlayPolicy policy, string? name = null)
    {
        string? preset = PlayPolicy.PresetNameOf(policy);
        CustomPolicy = preset is null ? policy : null;
        if (preset is null)
        {
            string named = name?.Trim() is { Length: > 0 } given ? given
                : PlayMode == PlayPolicy.CustomName && CustomName.Length > 0 ? CustomName
                : FreeCustomName();
            CustomName = KeepCustom(named, policy).Name;
        }

        if (PlayMode == (preset ?? PlayPolicy.CustomName))
        {
            Apply();
            OnPropertyChanged(nameof(Policy));
        }

        PlayMode = preset ?? PlayPolicy.CustomName;
        Save();
        ModeChosen?.Invoke();
    }

    partial void OnPlayModeChanged(string value)
    {
        Apply();
        Save();
    }

    /// <summary>Sets a currency's worth in HR, or clears it with null.</summary>
    public void SetTradePrice(string code, decimal? worth)
    {
        if (worth is { } value)
            TradePrices[code] = value;
        else if (!TradePrices.Remove(code))
            return;

        Save();
        TradePricesChanged?.Invoke();
    }

    /// <summary>Raised when the price table changes a currency's worth.</summary>
    public event Action? TradePricesChanged;

    /// <summary>The editor's own data folder (EditorData): settings sit at its root.</summary>
    public static string Folder => EditorData.Root;

    public static string FilePath => EditorData.SettingsFile;

    /// <summary>
    /// True while <see cref="Load"/> is filling this in, which stops <see cref="Save"/> from
    /// writing a half-built object back over the file.
    /// <para>
    /// Assigning <see cref="GameFolder"/> raises its change handler, which saves - so loading
    /// wrote the file out again before the rest of the settings had been read. It stamped the
    /// starter-seed marker over an empty list, which then looked like a list somebody had
    /// deliberately cleared.
    /// </para>
    /// </summary>
    private bool _loading;

    /// <summary>Beside the file: what a file that would not read is kept as.</summary>
    public const string UnreadableSuffix = ".unreadable";

    /// <summary>
    /// The file would not read and has not been copied aside yet: nothing writes over it until it has been. Replaced by
    /// the defaults within a second of starting, it was every fold, preset and choice gone with no trace.
    /// </summary>
    private bool _unreadable;

    /// <summary>Where the copy of a file that would not read is, until it has been said.</summary>
    private string? _unreadableCopy;

    /// <summary>Where the copy of a file that would not read is: once, for the strip.</summary>
    public string? TakeUnreadableCopy()
    {
        string? copy = _unreadableCopy;
        _unreadableCopy = null;
        return copy;
    }

    /// <summary>The file that would not read, copied aside; false where it cannot be yet - held, it stays held from writes too.</summary>
    private bool KeepUnreadable()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Copy(FilePath, FilePath + UnreadableSuffix, overwrite: true);
                _unreadableCopy = FilePath + UnreadableSuffix;
            }

            _unreadable = false;
            return true;
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            return false;
        }
    }

    /// <summary>The file's text, asked again a moment later while another program holds it: a sync tool, a virus scanner.</summary>
    private static string ReadHeld(string path)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException) when (attempt < 4)
            {
                System.Threading.Thread.Sleep(100);
            }
        }
    }

    /// <summary>Reads the saved settings, or the defaults when there are none.</summary>
    public static EditorSettings Load()
    {
        var settings = new EditorSettings { _loading = true };
        int starters = 0, priceStarters = 0;

        try
        {
            if (File.Exists(FilePath))
            {
                var stored = JsonSerializer.Deserialize<Stored>(ReadHeld(FilePath));
                if (stored is not null)
                {
                    settings.GameFolder = GameLocation.Tidy(stored.GameFolder);
                    settings.BackupsToKeep = stored.BackupsKept is { } kept ? NormalizedKept(kept) : LegacyKept(stored.BackupsToKeep);
                    settings.VaultBackupsToKeep = stored.VaultBackupsKept is { } ofVault ? NormalizedKept(ofVault) : DefaultVaultKept;
                    settings.FlagIllegitimateValues = stored.FlagIllegitimateValues;
                    settings.Animations = stored.Animations;
                    settings.Sounds = stored.Sounds;
                    settings.UndoSteps = NearestUndoSteps(stored.UndoSteps);
                    settings.UiScale = Settings.UiScale.Nearest(stored.UiScale);
                    settings.VaultHardcore = stored.VaultHardcore;
                    settings.VaultNameSoftcore = stored.VaultNameSoftcore ?? "";
                    settings.VaultNameHardcore = stored.VaultNameHardcore ?? "";
                    settings.InventoryLock = stored.InventoryLock;
                    settings.CompareOnHover = stored.CompareOnHover;
                    settings.Window = stored.Window;
                    settings.SocketGuard = stored.SocketGuard;
                    settings.RuneWizardCountsBases = stored.RuneWizardCountsBases ?? true;
                    settings._vaultMinimapBefore = stored.VaultMinimap;
                    foreach (var (page, look) in stored.VaultLooks ?? [])
                        settings.VaultLooks[page] = look;
                    // But for the vault's folders: their folds are a session's, and what older files kept
                    // of them - "VaultFold." by the vault and the page, "FolderPlace." by a folder's id alone
                    // before that - is let go, and out of the file at its next writing.
                    foreach (var (fold, open) in stored.Folds ?? [])
                    {
                        if (!fold.StartsWith("FolderPlace.", StringComparison.Ordinal) && !fold.StartsWith("VaultFold.", StringComparison.Ordinal))
                            settings.Folds[fold] = open;
                    }
                    foreach (var (part, size) in stored.Sizes ?? [])
                        settings.Sizes[part] = size;
                    settings.OnlineWarningDismissed = stored.OnlineWarningDismissed;
                    settings.VerifiedBuild = stored.VerifiedBuild ?? "";
                    settings.MigrationNoticeSeason = stored.MigrationNoticeSeason ?? "";
                    settings.LastRunVersion = stored.LastRunVersion ?? "";
                    settings.Language = stored.Language is { Length: > 0 } language ? language : PD2SPCompanion.Core.Localization.Language.English;
                    foreach (var (key, order) in stored.Sorts ?? [])
                    {
                        if (order is not null)
                            settings.Sorts[key] = order;
                    }

                    foreach (var key in stored.LockedPages ?? [])
                    {
                        if (!string.IsNullOrWhiteSpace(key))
                            settings.LockedPages.Add(key);
                    }

                    foreach (var note in stored.MapSeeds ?? [])
                    {
                        if (note is not null)
                            settings.MapSeeds.Add(note);
                    }

                    starters = stored.MapSeedStarterVersion;

                    foreach (var preset in stored.SearchPresets ?? [])
                    {
                        if (preset is not null)
                            settings.SearchPresets.Add(preset);
                    }

                    foreach (var preset in stored.DropPresets ?? [])
                    {
                        if (preset is not null)
                            settings.DropPresets.Add(preset);
                    }

                    foreach (var (code, worth) in stored.TradePrices ?? [])
                        settings.TradePrices[code] = worth;

                    priceStarters = stored.TradePriceStarterVersion;

                    foreach (var preset in stored.CustomPresets ?? [])
                    {
                        if (preset is { Name.Length: > 0, Policy: not null } && settings.Custom(preset.Name) is null)
                        {
                            preset.Policy = preset.Policy.Normalized();
                            settings.CustomPresets.Add(preset);
                        }
                    }

                    // The custom mode that is the mode: the kept one of its name, or - in a file written
                    // while there was one Custom and no names - that one, kept now as the first, "1".
                    settings.CustomName = stored.CustomName ?? "";
                    settings.CustomPolicy = settings.Custom(settings.CustomName)?.Policy ?? stored.CustomPolicy?.Normalized();
                    if (stored.PlayMode == PlayPolicy.CustomName && settings.CustomPolicy is { } custom
                        && settings.Custom(settings.CustomName) is null)
                    {
                        settings.CustomName = settings.KeepCustom(settings.CustomName.Length > 0 ? settings.CustomName : settings.FreeCustomName(), custom).Name;
                    }

                    // A file from before modes, or naming a Custom it does not hold, is Casual.
                    settings.PlayMode = stored.PlayMode is { } mode
                                        && (PlayPolicy.Preset(mode) is not null
                                            || (mode == PlayPolicy.CustomName && settings.CustomPolicy is not null))
                        ? mode
                        : PlayPolicy.CasualName;
                }
            }
        }
        catch (Exception problem)
        {
            // An unreadable or half-written file falls back to the defaults rather than
            // stopping the editor from opening - kept first, as it was.
            Log.Caught(problem);
            settings._unreadable = true;
            settings.KeepUnreadable();
        }

        // **A version, not a yes-or-no.** A flag cannot say "seeded with an older list", so a
        // starter added later would never reach anyone who had already run the editor. This
        // also heals a file stamped before the ordering bug above was found. Anything already
        // in the list is kept; only seeds it does not have are added, so a remark somebody
        // wrote themselves is never overwritten.
        if (starters < MapSeedStarters.Version)
        {
            foreach (var note in MapSeedStarters.All)
            {
                if (!settings.MapSeeds.Any(each => each.Seed == note.Seed))
                    settings.MapSeeds.Add(note);
            }
        }

        // The price table's starting prices, the same way: once per version, and only where a
        // currency has no price - one the player set or cleared since is theirs.
        if (priceStarters < TradePriceStarters.Version)
        {
            foreach (var (code, worth) in TradePriceStarters.All)
                settings.TradePrices.TryAdd(code, worth);
        }

        settings._loading = false;
        settings.Apply();

        // The sections that fold read these as they are made: with or without a settings file.
        FoldMemory.Settings = settings;
        return settings;
    }

    /// <summary>
    /// Writes the settings - whole or not at all: to a file beside them, then put in their place. Written in place, a
    /// program stopped mid-write (a crash, a kill, the machine off) left half a file, read at the next start as no
    /// settings at all: every fold, preset and choice gone (a test closing the program as a
    /// tooltip opened).
    /// </summary>
    public void Save()
    {
        if (_loading || (_unreadable && !KeepUnreadable()))
            return;

        try
        {
            Directory.CreateDirectory(Folder);
            string fresh = FilePath + ".new";
            File.WriteAllText(
                fresh,
                JsonSerializer.Serialize(
                    new Stored
                    {
                        GameFolder = GameFolder,
                        BackupsKept = BackupsToKeep,
                        BackupsToKeep = BackupsToKeep,
                        VaultBackupsKept = VaultBackupsToKeep,
                        FlagIllegitimateValues = FlagIllegitimateValues,
                        Animations = Animations,
                        Sounds = Sounds,
                        UndoSteps = UndoSteps,
                        UiScale = UiScale,
                        VaultHardcore = VaultHardcore,
                        VaultNameSoftcore = VaultNameSoftcore,
                        VaultNameHardcore = VaultNameHardcore,
                        InventoryLock = InventoryLock,
                        CompareOnHover = CompareOnHover,
                        Window = Window,
                        SocketGuard = SocketGuard,
                        RuneWizardCountsBases = RuneWizardCountsBases,
                        VaultMinimap = _vaultMinimapBefore,
                        VaultLooks = new Dictionary<string, VaultLook>(VaultLooks),
                        Folds = new Dictionary<string, bool>(Folds),
                        Sizes = new Dictionary<string, int>(Sizes),
                        OnlineWarningDismissed = OnlineWarningDismissed,
                        VerifiedBuild = VerifiedBuild,
                        MigrationNoticeSeason = MigrationNoticeSeason,
                        LastRunVersion = LastRunVersion,
                        Language = Language,
                        Sorts = new Dictionary<string, StoredSort>(Sorts),
                        LockedPages = [.. LockedPages.Order(StringComparer.OrdinalIgnoreCase)],
                        MapSeeds = [.. MapSeeds],
                        MapSeedStarterVersion = MapSeedStarters.Version,
                        SearchPresets = [.. SearchPresets],
                        DropPresets = [.. DropPresets],
                        TradePrices = new Dictionary<string, decimal>(TradePrices),
                        TradePriceStarterVersion = TradePriceStarters.Version,
                        PlayMode = PlayMode,
                        CustomPolicy = CustomPolicy,
                        CustomName = CustomName,
                        CustomPresets = [.. CustomPresets],
                    },
                    new JsonSerializerOptions { WriteIndented = true }));
            File.Move(fresh, FilePath, overwrite: true);
        }
        catch (Exception problem)
        {
            // Settings are a convenience; failing to store them must not fail anything else.
            Log.Caught(problem);
        }
    }

    /// <summary>Hands the current values to the parts of the core that act on them.</summary>
    public void Apply()
    {
        Motion.Set(off: !Animations);

        SaveWriter.BackupsToKeep = BackupsToKeep;
        PD2SPCompanion.Core.Collection.Vault.CopiesToKeep = VaultBackupsToKeep;
        SessionHistory.MostKept = UndoSteps;
        Settings.UiScale.Set(UiScale);
        StatRowViewModel.FlagIllegitimate = FlagIllegitimateValues;
        // The play mode's: free editing where properties may be anything - Free - and the lock
        // closed everywhere else. It replaced the "free editing by default" setting.
        StatRowViewModel.FreeEditing = Policy.Properties == Reach.Any;
        StatRowViewModel.ChargesReach = Policy.Details;
    }

    partial void OnGameFolderChanged(string value)
    {
        OnPropertyChanged(nameof(HasGameFolder));
        Save();
    }

    partial void OnBackupsToKeepChanged(int value)
    {
        Apply();
        Save();
    }

    partial void OnVaultBackupsToKeepChanged(int value)
    {
        Apply();
        Save();
    }

    partial void OnFlagIllegitimateValuesChanged(bool value)
    {
        Apply();
        Save();
    }

    partial void OnSoundsChanged(bool value) => Save();

    partial void OnAnimationsChanged(bool value)
    {
        Apply();
        Save();
    }


    partial void OnUndoStepsChanged(int value)
    {
        Apply();
        Save();
    }

    partial void OnUiScaleChanged(int value)
    {
        Apply();
        Save();
    }

    /// <summary>The scale as the Settings page's drop-down has it: which of <see cref="Settings.UiScale.Choices"/>.</summary>
    public int UiScaleIndex
    {
        get => Math.Max(0, Settings.UiScale.Choices.ToList().IndexOf(UiScale));
        set
        {
            if (value >= 0 && value < Settings.UiScale.Choices.Count)
                UiScale = Settings.UiScale.Choices[value];
        }
    }

    partial void OnVaultHardcoreChanged(bool value) => Save();

    partial void OnVaultNameSoftcoreChanged(string value) => Save();

    partial void OnVaultNameHardcoreChanged(string value) => Save();

    partial void OnOnlineWarningDismissedChanged(bool value) => Save();

    partial void OnInventoryLockChanged(bool value) => Save();

    /// <summary>
    /// The socket guard, against a socket filled by an accidental drop: a gem,
    /// a rune or a jewel let go of on an item is not socketed while it is on. Off by default.
    /// </summary>
    [ObservableProperty] private bool _socketGuard;

    /// <summary>
    /// Whether the rune wizard counts the bases in the vault's Bases folder with the runes.
    /// On to begin with.
    /// </summary>
    [ObservableProperty] private bool _runeWizardCountsBases = true;

    partial void OnRuneWizardCountsBasesChanged(bool value) => Save();

    partial void OnSocketGuardChanged(bool value) => Save();

    partial void OnVerifiedBuildChanged(string value) => Save();

    partial void OnMigrationNoticeSeasonChanged(string value) => Save();

    partial void OnLastRunVersionChanged(string value) => Save();

    partial void OnLanguageChanged(string value) => Save();

    /// <summary>The shape on disk, kept separate so the view model can change freely.</summary>
    private sealed class Stored
    {
        // Absent from a file written before the folder was a setting, which reads back as
        // null and asks on the next run rather than guessing.
        public string? GameFolder { get; set; }

        public int BackupsToKeep { get; set; } = DefaultKept;

        // Since "Never" (0) became a choice: its own name, so a 0 the old field held - "keep every one"
        // - is never read as "Never" and prune what it meant to keep.
        public int? BackupsKept { get; set; }

        // The vault's own count; absent from a file written before it had one.
        public int? VaultBackupsKept { get; set; }
        public bool FlagIllegitimateValues { get; set; } = true;

        // Absent from a file written before the setting, which reads back as on.
        public bool Animations { get; set; } = true;

        // Absent from a file written before the setting, which reads back as on.
        public bool Sounds { get; set; } = true;


        // Absent from a file written before the setting: the default.
        public int UndoSteps { get; set; } = DefaultUndoSteps;

        // Absent from a file written before the setting: Automatic.
        public int UiScale { get; set; }

        // Absent from a file written before the mode was kept: softcore.
        public bool VaultHardcore { get; set; }

        // Absent from a file written before a mode had several vaults: each mode's first.
        public string? VaultNameSoftcore { get; set; }

        public string? VaultNameHardcore { get; set; }

        // Absent from a file written before the lock: on.
        public bool InventoryLock { get; set; }

        // Absent from a file written before the comparison: off.
        public bool CompareOnHover { get; set; }

        // Absent from a file written before the window was kept: none, the window's own start.
        public WindowPlace? Window { get; set; }

        // Absent from a file written before the socket guard: off.
        public bool SocketGuard { get; set; }
        public bool? RuneWizardCountsBases { get; set; }

        // On where the file does not say: a settings file from before the map has no word of it. Read for the
        // pages' looks, which took it in (VaultLooks): what a page's map is before the page is changed.
        public bool VaultMinimap { get; set; } = true;

        // None where the file has none: every vault page as it is made.
        public Dictionary<string, VaultLook>? VaultLooks { get; set; }

        // None where the file has none: every section as it is made.
        public Dictionary<string, bool>? Folds { get; set; }

        // None where the file has none: every part its own height.
        public Dictionary<string, int>? Sizes { get; set; }

        // Absent from a file written before the notice: not yet seen.
        public bool OnlineWarningDismissed { get; set; }

        public string? VerifiedBuild { get; set; }

        public string? MigrationNoticeSeason { get; set; }

        // Absent from a file written before the notice: treated as a first run, which says nothing.
        public string? LastRunVersion { get; set; }

        // Absent from a file written before languages: English.
        public string? Language { get; set; }

        // Null on a file written before any list could be ordered.
        public Dictionary<string, StoredSort>? Sorts { get; set; }

        // Null on a file written before pages could be locked.
        public List<string>? LockedPages { get; set; }

        // Absent from a file written before the list existed, which reads back as null.
        public List<MapSeedNote>? MapSeeds { get; set; }

        /// <summary>
        /// Which starter list this file has seen. Zero on a file written before they existed,
        /// which is when they go in.
        /// </summary>
        public int MapSeedStarterVersion { get; set; }

        // Absent from a file written before the vault could be searched.
        public List<SearchPreset>? SearchPresets { get; set; }
        public List<DropPreset>? DropPresets { get; set; }

        // Absent from a file written before Virtual Trading had a price table.
        public Dictionary<string, decimal>? TradePrices { get; set; }

        /// <summary>Which starting prices this file has seen; zero before there were any.</summary>
        public int TradePriceStarterVersion { get; set; }

        // Absent from a file written before play modes, which reads back as Casual.
        public string? PlayMode { get; set; }

        public PlayPolicy? CustomPolicy { get; set; }

        // Absent from a file written while there was one Custom: that one becomes the first kept.
        public string? CustomName { get; set; }

        public List<PolicyPreset>? CustomPresets { get; set; }
    }
}

/// <summary>Where the window was, in screen pixels, and its size in its own units; maximized or not.</summary>
public sealed record WindowPlace(int X, int Y, double Width, double Height, bool Maximized);
