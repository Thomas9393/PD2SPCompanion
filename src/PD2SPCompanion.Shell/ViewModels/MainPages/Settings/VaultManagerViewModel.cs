using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Vault;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// A copy of a vault, as a row of the backups' list: the vault it is a copy of, and under it why it was taken,
/// when, and how large. Its bin asks twice, as every row's bin does.
/// </summary>
public sealed partial class VaultCopyRow(PD2SPCompanion.Core.Collection.Vault.BackupCopy copy) : ObservableObject, IAsksTwice
{
    public PD2SPCompanion.Core.Collection.Vault.BackupCopy Copy { get; } = copy;

    public string Name => Copy.Vault;

    public string Summary => $"{Reason(Copy.Why)} · {Copy.At:yyyy-MM-dd HH:mm} · {VP_ViewModel_Settings_L.SizeText(Copy.Bytes)}";

    [ObservableProperty] private bool _isArmed;

    /// <summary>Why a copy was taken, in words: the tag the vault wrote in its name (Vault.BackUp's callers).</summary>
    public static string Reason(string why) => why switch
    {
        "deleted" => L("Deleted vault"),
        "cleared" or "emptied" => L("Before cleaning"),
        "session" => L("Start of a session"),
        "before-merge" or "before-collections" => L("Before a merge"),
        "grail-forgotten" => L("Before clearing the grail"),
        _ when why.Length > 1 && why[0] == 'v' && why[1..].All(char.IsDigit) => L("Before an update"),
        _ => why,
    };
}

/// <summary>One vault in the settings page's list.</summary>
public sealed partial class VaultListRow(string name, string summary, bool isOpen) : ObservableObject
{
    public string Name { get; } = name;

    public string Summary { get; } = summary;

    /// <summary>The vault the pages are showing.</summary>
    public bool IsOpen { get; } = isOpen;

    public bool CanDelete => !IsOpen;

    [ObservableProperty] private bool _isRenaming;

    [ObservableProperty] private string _editName = name;

    public string DeleteHint => IsOpen ? L("Require another vault open") : L("Delete");
}

/// <summary>
/// A row of the import's check list, the root listing what folders it hosts and a last entry for the items in
/// this folder: a folder of the file being brought in -
/// the file itself at the top - or, last under a folder that holds folders, the items lying in that folder
/// itself. A folder with no folders in it stands for its own items.
/// </summary>
public sealed partial class ImportNode : ObservableObject
{
    public ImportNode(long? folder, bool isItems, string name, int depth, int own, ImportNode? parent)
    {
        Folder = folder;
        IsItems = isItems;
        Name = name;
        Depth = depth;
        Own = own;
        Parent = parent;
        parent?.Children.Add(this);
    }

    /// <summary>The file's folder this row is - or whose own items it is; null for the file's top.</summary>
    public long? Folder { get; }

    /// <summary>The entry for the items lying in a folder itself.</summary>
    public bool IsItems { get; }

    public string Name { get; }

    public int Depth { get; }

    public Thickness Indent => new(Depth * 16, 0, 0, 0);

    /// <summary>The items this row itself stands for: a folder's own, where the row has none beneath it.</summary>
    public int Own { get; }

    public ImportNode? Parent { get; }

    public List<ImportNode> Children { get; } = [];

    public bool HoldsRows => Children.Count > 0;

    /// <summary>Raised when the player ticks or unticks the row.</summary>
    public Action<ImportNode>? Ticked { get; set; }

    /// <summary>Set by the list itself - a folder following what is beneath it - not by the player.</summary>
    internal bool Following { get; set; }

    [ObservableProperty] private bool _isTicked = true;

    partial void OnIsTickedChanged(bool value)
    {
        if (!Following)
            Ticked?.Invoke(this);
    }

    /// <summary>Unfolded: what is beneath it is listed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FoldHint))]
    private bool _isOpen = true;

    /// <summary>The chevron's tooltip: an icon button says what it is.</summary>
    public string FoldHint => IsOpen ? L("Fold") : L("Unfold");

    /// <summary>Not folded away under a folder above.</summary>
    [ObservableProperty] private bool _isShown = true;

    /// <summary>How many of its items come: "12 items", or "3 / 12" where only some do.</summary>
    [ObservableProperty] private string _count = "";

    /// <summary>The rows that stand for items, beneath this one or this one itself.</summary>
    public IEnumerable<ImportNode> Leaves() => HoldsRows ? Children.SelectMany(child => child.Leaves()) : [this];
}

/// <summary>
/// The settings page's Vault section, the one place a mode's vaults are handled:
/// which mode, which of its vaults is open - picked from a list in a panel over the page, where one is
/// also made, copied, renamed, written out and deleted - and the one Import, which asks whether the file
/// comes in as a vault of its own or as a folder of the open one, the folders to bring ticked. What a
/// vault is on disk is <see cref="VaultLibrary"/>'s; this is the buttons.
/// </summary>
public sealed partial class VaultManagerViewModel : ViewModelBase
{
    private readonly CharacterSession _session;
    private readonly VaultService _vaults;
    private readonly ISFXManager? _sfx;
    private readonly ConfirmService? _confirm;

    public VaultManagerViewModel(CharacterSession session, VaultService vaults, ISFXManager? sfx, ConfirmService? confirm = null)
    {
        _session = session;
        _vaults = vaults;
        _sfx = sfx;
        _confirm = confirm;

        vaults.Switched += Refresh;
        vaults.Changed += Refresh;
        vaults.GoldChanged += Refresh;
        vaults.Listed += Refresh;
        session.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(CharacterSession.IsLoaded))
            {
                OnPropertyChanged(nameof(CanChangeMode));
                OnPropertyChanged(nameof(ModeHint));
            }
        };
    }

    public static string[] Modes { get; } = [L("Softcore"), L("Hardcore")];

    /// <summary>
    /// The open mode. A loaded character decides it, so it is chosen by hand only with none
    /// loaded.
    /// </summary>
    public int ModeIndex
    {
        get => _vaults.IsHardcore ? 1 : 0;
        set
        {
            if (value is < 0 or > 1 || (value == 1) == _vaults.IsHardcore || !CanChangeMode)
                return;

            _vaults.UseMode(value == 1);
            Say(L($"Opened {Modes[value].ToLowerInvariant()} vault."));
        }
    }

    public bool CanChangeMode => !_session.IsLoaded;

    /// <summary>The Mode pair's lit button.</summary>
    public bool IsHardcore => _vaults.IsHardcore;

    /// <summary>The Mode pair's two buttons: softcore (0) or hardcore (1).</summary>
    [RelayCommand]
    private void ChooseMode(string? index)
    {
        ModeIndex = index == "1" ? 1 : 0;
        OnPropertyChanged(nameof(IsHardcore));
    }

    public string? ModeHint => CanChangeMode ? null : L("Follows open character.");

    /// <summary>The open vault's name, as the row's box shows it.</summary>
    [ObservableProperty] private string _openName = "";

    /// <summary>What the open vault holds: items, trades, gold - and the play mode it keeps.</summary>
    [ObservableProperty] private string _summary = "";

    // ---- the list -----------------------------------------------------------------------------

    public ObservableCollection<VaultListRow> Rows { get; } = [];

    /// <summary>Every vault of the mode; <see cref="Rows"/> is what the search leaves of it.</summary>
    private readonly List<VaultListRow> _all = [];

    public void Refresh()
    {
        OnPropertyChanged(nameof(ModeIndex));
        OnPropertyChanged(nameof(IsHardcore));
        OnPropertyChanged(nameof(CanChangeMode));

        _all.Clear();
        string open = _vaults.VaultName;
        foreach (var each in _vaults.Vaults())
        {
            string items = Plural(each.Items, "1 item", $"{each.Items} items");
            _all.Add(new VaultListRow(each.Name, each.PlayMode.Length > 0 ? $"{items} · {each.PlayMode}" : items,
                                  string.Equals(each.Name, open, StringComparison.OrdinalIgnoreCase)));
        }

        Filter();
        OpenName = open;

        if (_vaults.Store is { } vault)
        {
            _items = vault.Count();
            _trades = vault.Trades().Count;
            _gold = vault.GoldBalance();
            string mode = vault.About(VaultLibrary.PlayModeKey) is { Length: > 0 } kept ? " · " + kept : "";
            Summary = Plural(_items, "1 item", $"{_items} items") + " · "
                      + L($"{_trades} trades · {_gold:N0} gold") + mode;
            CanClear = true;
        }
        else
        {
            Summary = _vaults.Trouble;
            CanClear = false;
        }

    }

    private void Say(string message) => _session.Status = message;

    /// <summary>Whether a save's moves into or out of the vault wait: it is not closed under them.</summary>
    private bool Held()
    {
        if (_vaults.HasPendingMoves?.Invoke() != true)
            return false;

        Say(L("Require save or undo."));
        return true;
    }

    // ---- the picker panel ---------------------------------------------------------------------

    /// <summary>
    /// The vaults in a picker panel over the page, as the app's other pickers are: a long list would push
    /// the page apart inline. Shown fades; present follows it.
    /// </summary>
    [ObservableProperty] private bool _isPickerPresent;

    [ObservableProperty] private bool _isPickerOpen;

    partial void OnIsPickerOpenChanged(bool value) =>
        Presence.Keep(value, () => IsPickerOpen, on => IsPickerPresent = on);

    [RelayCommand]
    private void OpenPicker()
    {
        _sfx?.Method_ButtonSFX_Press();
        Search = "";
        IsNaming = false;
        Refresh();
        IsPickerOpen = true;
    }

    [RelayCommand]
    private void ClosePicker()
    {
        IsNaming = false;
        foreach (var row in _all)
            row.IsRenaming = false;

        IsPickerOpen = false;
    }

    /// <summary>What the search box holds: a part of a name.</summary>
    [ObservableProperty] private string _search = "";

    partial void OnSearchChanged(string value) => Filter();

    public bool NoMatches => Rows.Count == 0 && _all.Count > 0;

    private void Filter()
    {
        string wanted = Search.Trim();
        Rows.Clear();
        foreach (var row in _all)
        {
            if (wanted.Length == 0 || row.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase))
                Rows.Add(row);
        }

        OnPropertyChanged(nameof(NoMatches));
    }

    /// <summary>A row clicked: that vault is opened, the one open before stays in the list.</summary>
    [RelayCommand]
    private void OpenVault(VaultListRow row)
    {
        _sfx?.Method_ButtonSFX_Press();

        if (row.IsOpen)
        {
            IsPickerOpen = false;
            return;
        }

        if (Held())
            return;

        try
        {
            _vaults.SwitchTo(row.Name);
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            Say(L($"Not opened: {problem.Message}"));
            return;
        }

        // Picked, as the other pickers pick: the panel has done its job.
        IsPickerOpen = false;
        Say(L($"Opened {row.Name}."));
    }

    // ---- new ----------------------------------------------------------------------------------

    [ObservableProperty] private bool _isNaming;

    [ObservableProperty] private string _newName = "";

    [RelayCommand]
    private void StartNew()
    {
        _sfx?.Method_ButtonSFX_Press();
        NewName = "";
        IsNaming = true;
    }

    [RelayCommand]
    public void CommitNew()
    {
        if (!IsNaming)
            return;

        if (_vaults.RefuseName(NewName) is { } refused)
        {
            Say(refused);
            return;
        }

        try
        {
            _vaults.Create(NewName, null);
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            Say(L($"Not created: {problem.Message}"));
            return;
        }

        IsNaming = false;
        Say(L($"Created {NewName.Trim()}."));
    }

    [RelayCommand]
    public void CancelNew() => IsNaming = false;

    // ---- a row's buttons ----------------------------------------------------------------------

    /// <summary>A copy of the row's vault - everything in it - under a free name, its name box open to change it.</summary>
    [RelayCommand]
    private void CopyVault(VaultListRow row)
    {
        _sfx?.Method_ButtonSFX_Press();

        string name = row.Name;
        for (int n = 2; _vaults.RefuseName(name) is not null; n++)
        {
            string tail = $" ({n})";
            name = (row.Name.Length + tail.Length > VaultLibrary.LongestName ? row.Name[..(VaultLibrary.LongestName - tail.Length)] : row.Name) + tail;
        }

        try
        {
            _vaults.Create(name, row.Name);
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            Say(L($"Not copied: {problem.Message}"));
            return;
        }

        Say(L($"Cloned {row.Name} as {name}."));
        if (_all.FirstOrDefault(each => string.Equals(each.Name, name, StringComparison.OrdinalIgnoreCase)) is { } made)
            made.IsRenaming = true;
    }

    [RelayCommand]
    private void StartRename(VaultListRow row)
    {
        _sfx?.Method_ButtonSFX_Press();
        foreach (var other in _all)
            other.IsRenaming = false;

        row.EditName = row.Name;
        row.IsRenaming = true;
    }

    public void CommitRename(VaultListRow row)
    {
        if (!row.IsRenaming)
            return;

        if (string.Equals(row.EditName.Trim(), row.Name, StringComparison.Ordinal))
        {
            row.IsRenaming = false;
            return;
        }

        // A name refused keeps the box open with what was typed, as the character's rename does.
        if (_vaults.RefuseName(row.EditName, row.Name) is { } refused)
        {
            Say(refused);
            return;
        }

        // The open one is closed to be renamed.
        if (row.IsOpen && Held())
            return;

        try
        {
            _vaults.Rename(row.Name, row.EditName);
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            Say(L($"Not renamed: {problem.Message}"));
            return;
        }

        row.IsRenaming = false;
        Say(L($"Renamed to {row.EditName.Trim()}."));
    }

    [RelayCommand]
    public void CancelRename(VaultListRow row) => row.IsRenaming = false;

    [RelayCommand]
    private void DeleteVault(VaultListRow row)
    {
        _sfx?.Method_ButtonSFX_Press();

        if (row.IsOpen)
            return;

        // A whole vault, everything in it at once: a question, as a character's deletion asks
        // (a thing deleted is the bin clicked twice; a whole file, or many at once, is asked about).
        void Go()
        {
            try
            {
                _vaults.Delete(row.Name);
                Say(L($"Deleted {row.Name}. Copy in vault backups."));
            }
            catch (Exception problem)
            {
                Log.Caught(problem);
                Say(L($"Not deleted: {problem.Message}"));
            }
        }

        if (_confirm is null)
        {
            Go();
            return;
        }

        _confirm.Ask(L("Delete vault"),
            L($"Delete {row.Name} and everything in it?") + Environment.NewLine + L("A copy is kept in vault backups."),
            L("DELETE"), Go, deletes: true);
    }

    [RelayCommand]
    private async Task ExportVault(VaultListRow row)
    {
        _sfx?.Method_ButtonSFX_Press();

        if (await FilePicker.Save(L("Export vault"), row.Name + VaultLibrary.VaultExtension, _vaults.ExportsFolder) is not { } target)
            return;

        try
        {
            _vaults.ExportVault(row.Name, target);
            Say(L($"Exported {row.Name}."));
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            Say(L($"Not exported: {problem.Message}"));
        }
    }

    // (No vault Width here: a folder's sheet is as wide as the page it is shown on.)

    // ---- clean ----------------------------------------------------------------------------------

    /// <summary>Clean vault's tooltip, with why where it is dimmed: the vault could not be read.</summary>
    public string ClearHint => CanClear ? L("Clean vault") : L("Clean vault") + Environment.NewLine + Summary;

    /// <summary>What the open vault holds, as last read: the Clean check list says each beside its tick.</summary>
    private int _items, _trades;

    private long _gold;

    /// <summary>A vault is open to clean.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClearHint))]
    private bool _canClear;

    /// <summary>
    /// The Clean check list, in a panel over the page, in place of the grail's bin (a popup choosing items, trade
    /// records or gold to delete; no grail, as the editor keeps none). Nothing ticked to begin with.
    /// Named Clean, not Delete: the vaults' list deletes a vault, and this is not that.
    /// Its button wears the bin, as everything that deletes and asks does.
    /// </summary>
    [ObservableProperty] private bool _isClearPresent;

    [ObservableProperty] private bool _isClearOpen;

    partial void OnIsClearOpenChanged(bool value) =>
        Presence.Keep(value, () => IsClearOpen, on => IsClearPresent = on);

    [ObservableProperty] private string _clearTitle = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmClearCommand))]
    private bool _clearItems;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmClearCommand))]
    private bool _clearTrades;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmClearCommand))]
    private bool _clearGold;

    [ObservableProperty] private string _itemsCount = "";

    [ObservableProperty] private string _tradesCount = "";

    [ObservableProperty] private string _goldCount = "";

    [RelayCommand]
    private void OpenClear()
    {
        _sfx?.Method_ButtonSFX_Press();
        Refresh();
        if (!CanClear)
            return;

        ClearTitle = L($"Clean {_vaults.VaultName}");
        ItemsCount = Plural(_items, "1 item", $"{_items} items");
        TradesCount = N($"{_trades}");
        GoldCount = N($"{_gold:N0}");
        ClearItems = ClearTrades = ClearGold = false;
        IsClearOpen = true;
    }

    [RelayCommand]
    private void CloseClear() => IsClearOpen = false;

    private bool AnyToClear => ClearItems || ClearTrades || ClearGold;

    /// <summary>Deletes what is ticked from the open vault, a copy of it kept in Backups first.</summary>
    [RelayCommand(CanExecute = nameof(AnyToClear))]
    private void ConfirmClear()
    {
        if (_vaults.Store is not { } store)
            return;

        _sfx?.Method_ButtonSFX_Press();

        // Rows a save is waiting to move would be deleted under it.
        if (ClearItems && Held())
            return;

        try
        {
            // Not a step to undo: the copy in Backups is the way back.
            _vaults.WriteQuietly(() => store.Clear(ClearItems, ClearTrades, ClearGold));
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            Say(L($"Not cleaned: {problem.Message}"));
            return;
        }

        IsClearOpen = false;
        _vaults.CurrentFolder = ClearItems ? null : _vaults.CurrentFolder;
        _vaults.SayChanged();
        Say(L("Cleaned. Copy in vault backups."));
    }

    // ---- the vaults' backups -----------------------------------------------
    // A vault is copied before it is deleted, cleaned, updated or merged, and the messages say so ("Copy in vault
    // backups."); Settings counted the copies and nothing more could be done with one but by hand. Listed here,
    // every vault's; one is brought back through the import - a vault of its own, or a folder of the open one
    // with its check list - so nothing is ever written over, and what was withdrawn since is there twice, as
    // after any restore. A bin deletes one copy, asked twice.

    public ObservableCollection<VaultCopyRow> Copies { get; } = [];

    public bool NoCopies => Copies.Count == 0;

    [ObservableProperty] private bool _isCopiesPresent;

    [ObservableProperty] private bool _isCopiesOpen;

    partial void OnIsCopiesOpenChanged(bool value) =>
        Presence.Keep(value, () => IsCopiesOpen, on => IsCopiesPresent = on);

    private void ListCopies()
    {
        Copies.Clear();
        try
        {
            foreach (var copy in PD2SPCompanion.Core.Collection.Vault.Copies())
                Copies.Add(new VaultCopyRow(copy));
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            Say(L($"Backups not listed: {problem.Message}"));
        }

        OnPropertyChanged(nameof(NoCopies));
    }

    /// <summary>The backups' list, in place of the vaults' list it was opened from.</summary>
    [RelayCommand]
    private void OpenCopies()
    {
        _sfx?.Method_ButtonSFX_Press();
        ListCopies();
        ClosePicker();
        IsCopiesOpen = true;
    }

    [RelayCommand]
    private void CloseCopies() => IsCopiesOpen = false;

    /// <summary>
    /// Back, a layer up: from the backups' list to the vaults' list it was opened from. The list lacked a way to go
    /// back one layer: its cross closed everything.
    /// </summary>
    [RelayCommand]
    private void BackToVaults()
    {
        IsCopiesOpen = false;
        OpenPicker();
    }

    /// <summary>The import's panel is up for a backup: its Back goes to the backups' list again.</summary>
    [ObservableProperty] private bool _importIsOfCopy;

    /// <summary>Back from the import of a backup: nothing brought in, the backups' list again.</summary>
    [RelayCommand]
    private void BackToCopies()
    {
        CloseImport();
        OpenCopies();
    }

    /// <summary>A copy brought back: staged under its vault's name, and the import asks where it goes.</summary>
    [RelayCommand]
    private void BringBack(VaultCopyRow? row)
    {
        if (row is null)
            return;

        _sfx?.Method_ButtonSFX_Press();
        if (Stage(row.Copy.Path, row.Copy.Vault))
        {
            ImportIsOfCopy = true;
            IsCopiesOpen = false;
        }
    }

    /// <summary>One copy deleted, on its bin's second press.</summary>
    [RelayCommand]
    private void DeleteCopy(VaultCopyRow? row)
    {
        if (row is null || AskTwice.First(row, Copies))
            return;

        _sfx?.Method_ButtonSFX_Press();
        try
        {
            System.IO.File.Delete(row.Copy.Path);
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            Say(L($"Not deleted: {problem.Message}"));
            return;
        }

        ListCopies();
        Say(L($"Deleted a backup of {row.Name}."));
    }

    // ---- the import ---------------------------------------------------------------------------

    /// <summary>The file chosen, staged to be read, while the panel asks what to make of it.</summary>
    private VaultLibrary.Incoming? _incoming;

    [ObservableProperty] private bool _isImportPresent;

    [ObservableProperty] private bool _isImportOpen;

    partial void OnIsImportOpenChanged(bool value) =>
        Presence.Keep(value, () => IsImportOpen, on => IsImportPresent = on);

    [ObservableProperty] private string _importTitle = "";

    /// <summary>
    /// The way in: as a folder of the open vault, or - the default - as a vault of its own, which is opened
    /// (dismounting the old and mounting the new).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AsVault))]
    private bool _asFolder;

    public bool AsVault => !AsFolder;

    /// <summary>The choice of a folder, named for where it would go: "Folder in S13 Main".</summary>
    [ObservableProperty] private string _folderChoice = "";

    /// <summary>The check list, a row a line: the file, its folders, and the items lying in a folder itself.</summary>
    public ObservableCollection<ImportNode> Nodes { get; } = [];

    /// <summary>What the ticks come to: "34 / 120 items".</summary>
    [ObservableProperty] private string _importCount = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmImportCommand))]
    private bool _canImport;

    [RelayCommand]
    private void ChooseVault() => AsFolder = false;

    [RelayCommand]
    private void ChooseFolder() => AsFolder = true;

    partial void OnAsFolderChanged(bool value) => Count();

    /// <summary>A vault file from anywhere: staged, and the panel asks where it goes.</summary>
    [RelayCommand]
    private async Task Import()
    {
        _sfx?.Method_ButtonSFX_Press();

        if (await FilePicker.Open(L("Import vault"), _vaults.ExportsFolder) is not { } source)
            return;

        Stage(source, null);
    }

    /// <summary>
    /// A vault file staged and the import's panel put up for it - a file chosen, or a backup brought back, which
    /// comes under the name of the vault it is a copy of. False, and said, where it cannot be read: another
    /// season's, or no vault file.
    /// </summary>
    private bool Stage(string source, string? name)
    {
        try
        {
            LetGo();
            _incoming = _vaults.Stage(source, name);
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            Say(L($"Not imported: {problem.Message}"));
            return false;
        }

        ImportTitle = L($"Import {_incoming.Name}");
        FolderChoice = L($"Folder in {_vaults.VaultName}");
        List(_incoming);
        AsFolder = false;
        Count();
        IsPickerOpen = false;
        ImportIsOfCopy = false;
        IsImportOpen = true;
        return true;
    }

    /// <summary>The check list of a staged file, everything ticked.</summary>
    private void List(VaultLibrary.Incoming incoming)
    {
        Nodes.Clear();
        var beneath = incoming.Folders.ToLookup(folder => folder.ParentId);
        int Own(long id) => incoming.ItemsPerFolder.GetValueOrDefault(id);

        void Add(ImportNode node)
        {
            node.Ticked = Tick;
            Nodes.Add(node);
        }

        void Walk(ImportNode parent, long? id, int depth)
        {
            foreach (var folder in beneath[id].OrderBy(each => each.Name, StringComparer.OrdinalIgnoreCase))
            {
                bool holds = beneath[folder.Id].Any();
                var row = new ImportNode(folder.Id, false, folder.Name, depth, holds ? 0 : Own(folder.Id), parent);
                Add(row);
                if (!holds)
                    continue;

                Walk(row, folder.Id, depth + 1);
                if (Own(folder.Id) > 0)
                    Add(new ImportNode(folder.Id, true, L("Items in this folder"), depth + 1, Own(folder.Id), row));
            }
        }

        bool any = incoming.Folders.Count > 0;
        var root = new ImportNode(null, false, incoming.Name, 0, any ? 0 : incoming.TopItems, null);
        Add(root);
        if (!any)
            return;

        Walk(root, null, 1);
        if (incoming.TopItems > 0)
            Add(new ImportNode(null, true, L("Items in this folder"), 1, incoming.TopItems, root));
    }

    /// <summary>A row ticked or unticked: what is beneath it follows, and the folders above say what is left.</summary>
    private void Tick(ImportNode row)
    {
        void Set(ImportNode node, bool ticked)
        {
            node.Following = true;
            node.IsTicked = ticked;
            node.Following = false;
        }

        void Down(ImportNode node)
        {
            foreach (var child in node.Children)
            {
                Set(child, row.IsTicked);
                Down(child);
            }
        }

        Down(row);
        for (var above = row.Parent; above is not null; above = above.Parent)
            Set(above, above.Leaves().All(leaf => leaf.IsTicked));

        Count();
    }

    /// <summary>Each row's count, and the sum: every item as a vault, the ticked ones as a folder.</summary>
    private void Count()
    {
        if (_incoming is not { } incoming)
            return;

        foreach (var node in Nodes)
        {
            int all = node.Leaves().Sum(leaf => leaf.Own), taken = node.Leaves().Where(leaf => leaf.IsTicked).Sum(leaf => leaf.Own);
            node.Count = taken == all || taken == 0 ? Plural(all, "1 item", $"{all} items") : N($"{taken} / {all}");
        }

        int coming = AsFolder ? Nodes.Where(node => !node.HoldsRows && node.IsTicked).Sum(node => node.Own) : incoming.Items;
        ImportCount = L($"{coming} / {incoming.Items} items");
        CanImport = !AsFolder || coming > 0;
    }

    /// <summary>A folder row's chevron: what is beneath it folds away, and comes back as it was.</summary>
    [RelayCommand]
    private void FoldNode(ImportNode row)
    {
        row.IsOpen = !row.IsOpen;

        void Show(ImportNode node, bool shown)
        {
            foreach (var child in node.Children)
            {
                child.IsShown = shown;
                Show(child, shown && child.IsOpen);
            }
        }

        Show(row, row.IsOpen);
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private void ConfirmImport()
    {
        if (_incoming is not { } incoming)
            return;

        _sfx?.Method_ButtonSFX_Press();

        // As a vault it is opened, and the one open now closed for it.
        if (!AsFolder && Held())
            return;

        try
        {
            if (AsFolder)
            {
                // A folder's own items: its own row where it holds no folders, else its last entry - and a
                // folder with neither is made where anything beneath it comes.
                var leaves = Nodes.Where(node => !node.HoldsRows).ToLookup(node => node.Folder);
                var folders = Nodes.Where(node => node.HoldsRows && node.Folder is not null).ToDictionary(node => node.Folder!.Value);
                bool Takes(long? folder) =>
                    leaves[folder].FirstOrDefault() is { } leaf
                        ? leaf.IsTicked
                        : folder is { } id && folders.TryGetValue(id, out var row) && row.Leaves().Any(each => each.IsTicked);

                var (name, added, failed) = _vaults.AddAsFolder(incoming, Takes);
                Say(failed == 0
                    ? Plural(added, $"Imported as {name}: 1 item.", $"Imported as {name}: {added} items.")
                    : L($"Imported as {name}: {added} items, {failed} not read."));
            }
            else
            {
                Say(L($"Imported and opened {_vaults.AddAsVault(incoming)}."));
            }
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            Say(L($"Not imported: {problem.Message}"));
        }

        CloseImport();
        Refresh();
    }

    [RelayCommand]
    private void CloseImport()
    {
        IsImportOpen = false;
        LetGo();
    }

    /// <summary>The staged copy, removed.</summary>
    private void LetGo()
    {
        _incoming?.Dispose();
        _incoming = null;
    }
}
