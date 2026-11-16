using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Vault;


using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// A vault page: one folder of the vault as a sheet of cells, its items lying where they were put, with
/// the path and the tree that lead to its other folders. A vault is one file for one season and mode.
/// <para>
/// Two of these stand side by side while the vault is on both pages (the <c>Twin</c> part). An item is
/// its tile, an <see cref="ItemTileViewModel"/> read from its row - "card" is its name here still, from
/// when the vault was a list of cards - and the folder shown, for its tools, a <see cref="FolderCard"/>.
/// </para>
/// </summary>
public sealed partial class VP_ViewModel_Vault_L : ViewModelBase, IDisposable, IDragPicker
{
    public CharacterSession Session { get; }

    /// <summary>The shared stash, which a card can be withdrawn onto.</summary>
    private readonly SharedStashSession _stash;

    /// <summary>The play mode's vault rule: move or copy.</summary>
    public PlayRules Rules { get; }

    /// <summary>What leaves the vault with the next save, under the Move rule.</summary>
    private readonly VaultMoves _moves;

    public EditorSettings Settings { get; }

    private readonly ISFXManager _sfxManager = null!;

    private readonly ConfirmService _confirm;

    /// <summary>Used by the designer, which gets no services.</summary>
    public VP_ViewModel_Vault_L() : this(new CharacterSession(), new EditorSettings(), null!, new ConfirmService(), null!,
                                         null!, new PlayRules(new EditorSettings()), null!, null!, null) { }

    public VP_ViewModel_Vault_L(CharacterSession session, EditorSettings settings,
                                ISFXManager sfxManager, ConfirmService confirm, VaultService vaults,
                                SharedStashSession stash, PlayRules rules, VaultMoves moves, ItemLanding landing,
                                GameSync? sync, SearchPanelViewModel? search = null)
    {
        _sync = sync;
        _landing = landing;
        Search = search;

        // A running game turns Withdraw off: it writes a character at the next save - see GameSync.
        if (sync is not null)
        {
            sync.PropertyChanged += (_, changed) =>
            {
                if (changed.PropertyName is not (nameof(GameSync.IsGameRunning) or nameof(GameSync.IsWriteBlocked)))
                    return;

                OnPropertyChanged(nameof(GameBlocked));
                OnPropertyChanged(nameof(CanWithdraw));
                OnPropertyChanged(nameof(WithdrawHint));
            };
        }

        Session = session;
        Settings = settings;
        _sfxManager = sfxManager;
        _confirm = confirm;
        _vaults = vaults;
        _stash = stash;
        Rules = rules;
        FollowSocketRules();
        _moves = moves;

        // The cards' arrows follow what is staged; the withdraw button's words follow the mode.
        if (moves is not null)
            moves.Changed += () => { ShowMoves(); SayGold(); };
        rules.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(WithdrawHint));
            OnPropertyChanged(nameof(CanWithdraw));
        };

        // Something cut, copied, pasted or let go: Paste says what it holds, and a folder that is cut is faint.
        ItemClipboard.Shared.Changed += SayClipboard;

        // A character opened or closed changes whether there is anywhere to withdraw to. And what the
        // inventory held is put down or put back: the cells and the folder lit under it here go out.
        session.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(CharacterSession.IsLoaded))
                OnPropertyChanged(nameof(CanWithdraw));

            if (changed.PropertyName is nameof(CharacterSession.Held) or nameof(CharacterSession.IsHolding) && !session.IsHolding)
            {
                if (!IsCarrying)
                {
                    HoverSheet(false);
                    HoverFolder(null);
                }
            }
        };

        // Stored from the inventory page: read again, so the card is there when this page is.
        if (vaults is not null)
        {
            // Another vault: its folder ids and rows are its own, so the page starts again at
            // its top rather than at an id that may name some other folder there. Before the
            // reload, which the switch raises straight after.
            vaults.Switched += () =>
            {
                // What may be withdrawn is asked again of the vault now open.
                OnPropertyChanged(nameof(CanWithdraw));
                OnPropertyChanged(nameof(WithdrawHint));
                _here = null;
                ForgetWays();

                // Another vault's folders, under the same ids: what was unfolded this session was this one's.
                _unfoldedTargets.Clear();
                IsPicking = false;
                SelectedEntry = null;
                Notice = "";
            };
            vaults.Changed += Reload;
            vaults.Rewritten += key =>
            {
                // The Edit page wrote this item in place: the card's row is the vault's again.
                if (Store is { } store && Cards.FirstOrDefault(card => card.Key == key) is { } card)
                {
                    try
                    {
                        if (store.RowsOf([key]) is [{ } row])
                            card.Reread(row);
                    }
                    catch (Exception problem)
                    {
                        Log.Caught(problem);
                    }
                }
            };
            if (DuplicateIndex.Current is { } duplicates)
                duplicates.Changed += Reload;
            vaults.Filter.Changed += WhenFilterChanged;
            vaults.Filter.Cleared += () =>
            {
                if (OwnsSearch)
                    ClearSelection();
            };
        }

        Order = new SortChoice(settings, "Vault", PD2SPCompanion.Core.Collection.Vault.Orders);

        Picks = new PickMenu(
            all: SelectAll,
            none: () => PickOver(_ => false),
            invert: () => PickOver(entry => IsPicked(entry) == false));

        // An order picked lays the folder's pages out in it (SortFolder) - the same order picked again too. A way
        // of looking: a locked folder is shown in it as any other (it refused, while a sort wrote places).
        Order.Picked += SortFolder;

        // Not loaded here. This view model is a singleton and is built while the app starts,
        // before any game folder has been opened - and the season comes from the installed
        // tables, which are read lazily on first use of the game data. Opening the vault now
        // stamped it "current" and made a second, empty file beside the real one. The page
        // asks for its contents when it is shown instead.
    }

    /// <summary>The vault, shared with the inventory page, which stores into it.</summary>
    private readonly VaultService _vaults;

    /// <summary>The open store, or null with the reason put where the page shows it.</summary>
    private Vault? Store
    {
        get
        {
            var store = _vaults.Store;
            if (store is null)
                Trouble = _vaults.Trouble;
            return store;
        }
    }

    /// <summary>
    /// The cards of the folder's pages read so far - what picking, deleting and withdrawing act on.
    /// </summary>
    public ObservableCollection<ItemTileViewModel> Cards { get; } = [];

    /// <summary>Why the list is empty, where it is empty for a reason worth saying.</summary>
    [ObservableProperty] private string _trouble = "";

    public bool HasTrouble => Trouble.Length > 0;

    partial void OnTroubleChanged(string value)
    {
        OnPropertyChanged(nameof(HasTrouble));
        OnPropertyChanged(nameof(IsPlainlyEmpty));
    }

    /// <summary>
    /// What the last action did, or why it could not - said on the message strip under this page: the left
    /// strip for the left vault page, the right strip for the right one (it was the
    /// left strip from either). What reaches the other page is said there instead (<see cref="SayAcross"/>).
    /// Posted on every assignment; clearing it says nothing.
    /// </summary>
    public string Notice
    {
        get => _notice;
        set
        {
            _notice = value;
            OnPropertyChanged();
            Strip.Post(value);
        }
    }

    /// <summary>The strip under this page.</summary>
    private MessageLog Strip => IsPrimary ? Session.Messages : Session.RightMessages;

    /// <summary>
    /// What an action on this page came to, as the session's status where this is the left page - the strip
    /// under it, and what the status line keeps - and on the right strip where it is the right one.
    /// </summary>
    private void Say(string text)
    {
        if (IsPrimary)
            Session.Status = text;
        else
            Session.RightMessages.Post(text);
    }

    /// <summary>
    /// What reached the character from this page - withdrawn, put on: said on the left strip, where it landed,
    /// whichever page it left.
    /// </summary>
    private void SayAcross(string text)
    {
        _notice = text;
        OnPropertyChanged(nameof(Notice));
        Session.Messages.Post(text);
    }

    private string _notice = "";

    /// <summary>
    /// How the list is ordered. Shared machinery - see <see cref="SortChoice"/> - so the drop
    /// box, the direction button and remembering the choice are not written again here.
    /// </summary>
    public SortChoice Order { get; }

    /// <summary>
    /// Several cards selected - or a rectangle or a Ctrl-click under way, which may yet leave one
    /// or none (see <see cref="Normalize"/>). There is no mode to turn on: one selected is <see cref="SelectedEntry"/>,
    /// several are the picked cards, and never both.
    /// </summary>
    [ObservableProperty] private bool _isPicking;

    partial void OnIsPickingChanged(bool value)
    {
        SaySelectionChanged();

        if (value)
            return;

        // Back to one or none: every pick goes, and the selection with it - whatever one is
        // left is made the selection by the caller (Normalize).
        ClearPicks();

        SelectedEntry = null;

        OnPropertyChanged(nameof(PickedCount));
    }

    /// <summary>The classic three, all, none and the others, over this page's cards.</summary>
    public PickMenu Picks { get; }

    /// <summary>
    /// The menu's three and Ctrl+A: picking comes on as a Ctrl-click's does, keeping what was
    /// selected, and whatever it leaves is the selection.
    /// </summary>
    private void PickOver(Func<object, bool> wanted)
    {
        if (!BeginDragPick(keepSelected: true))
            return;

        // A folder's pages hold the cards and the list of entries none; and "all" is the folder, the pages
        // not yet scrolled to with the rest.
        if (ShowsPages)
            LoadEveryPage();

        TickMany(() =>
        {
            foreach (var card in Cards.ToList())
                card.IsPicked = wanted(card);

            // The folder's folders with its items, as Explorer's "all" takes both.
            foreach (var tile in SheetFolders)
                tile.IsPicked = wanted(tile);
        });

        Normalize();
    }

    /// <summary>Ctrl+A: every item of the folder shown - or, while a search stands, what it found there.</summary>
    public void SelectAll()
    {
        // A locked folder stays out of Select all, as a locked stash page does.
        if (IsFolderLocked)
        {
            _sfxManager?.Method_ButtonSFX_Press();
            Notice = L("Folder locked.");
            return;
        }

        PickOver(IsWanted);
    }

    // ---- an item out (PD2SPCompanion.Core.Trade.GameClipboard.Write) -------------------------

    /// <summary>
    /// Ctrl + C, the game's own key over an item: what is selected - or every one of several - as the text the
    /// game copies, each with the editor's key that holds the item whole, one object after another as the
    /// game's copies are when pasted together. The Forge's Paste from game makes the first again. Null, and
    /// said, where nothing is selected or an item cannot be written. Nothing is changed.
    /// </summary>
    public string? CopyText()
    {
        var going = Acting.ToList();
        if (going.Count == 0)
        {
            Notice = L("No item selected.");
            return null;
        }

        if (!PD2SPCompanion.Core.Collection.Vault.TryInstalledSeason(out string season, out string why))
        {
            Notice = why;
            return null;
        }

        var texts = new List<string>();
        string? refused = null;
        foreach (var tile in going)
        {
            if (PD2SPCompanion.Core.Trade.GameClipboard.Write(tile.Item, PD2SPCompanion.Shell.Services.Vault.VaultService.StatIdBits, season, out string? no) is { } text)
                texts.Add(text);
            else
                refused ??= L($"{tile.Name} not copied: {no}");
        }

        if (texts.Count == 0)
        {
            Notice = refused ?? L("Nothing copied.");
            return null;
        }

        _sfxManager?.Method_ButtonSFX_Press();
        Notice = (texts.Count == 1 ? L($"{going[0].Name} copied.") : L($"{texts.Count} items copied."))
                 + (refused is null ? "" : " " + refused);
        return string.Join(Environment.NewLine, texts);
    }

    /// <summary>What "all" takes: everything, or the finds while a search stands (as the inventory's, VP_ViewModel_Inv_L.IsWanted).</summary>
    private bool IsWanted(object entry) => _hitKeys is null || (entry is ItemTileViewModel card && _hitKeys.Contains(card.Key));

    /// <summary>
    /// The search list's Select found: the finds in the folder shown become the selection. Answers how many
    /// there are; none where the finds lie in other folders, or the search is the other page's.
    /// </summary>
    public int SelectFound()
    {
        if (_hitKeys is not { Count: > 0 } hits || !IsSearchingHere)
            return 0;

        // One lock, one rule: a locked folder's items cannot be picked, as Select all says. None selected: the list says so.
        if (IsFolderLocked)
        {
            Notice = L("Folder locked.");
            return 0;
        }

        PickOver(IsWanted);
        return hits.Count;
    }

    /// <summary>
    /// Whether there are items to act on - the picked cards while picking, the selected one
    /// otherwise. Withdraw and move need items; nothing selected, they are off.
    /// </summary>
    public bool HasActing => Acting.Count > 0;

    private readonly GameSync? _sync;

    /// <summary>
    /// The game is running. Withdraw writes a character at the next save, so it is off and red;
    /// the vault's own work - folders, moving or deleting a card - goes on. See GameSync.
    /// </summary>
    public bool GameBlocked => _sync?.IsWriteBlocked ?? false;

    /// <summary>
    /// Withdraw needs items to copy, a character to copy them onto, and the game closed. (A forged
    /// collection's items came out only where the play mode forged, until collections were folded into the
    /// vault - Legit is the player's own word, kept by the player.)
    /// </summary>
    public bool CanWithdraw => HasActing && Session.IsLoaded && !GameBlocked && !AllLeaving;

    /// <summary>
    /// Everything acted on is already on its way out: on the character, its row leaving with the next save. The
    /// button is off and says so, where the inventory's Store takes a mark back on a second press - a withdrawal
    /// is already on the character, so taking it back is Undo's.
    /// </summary>
    private bool AllLeaving => _moves is { } moves && Acting is { Count: > 0 } acting && acting.All(card => moves.IsWithdrawing(card.Key));

    /// <summary>
    /// Withdraw's tooltip, worded as the inventory's Store is (one wording on both pages) -
    /// and with no character open, why it is dimmed, as its menu row says it.
    /// </summary>
    public string WithdrawHint => GameBlocked ? (_sync?.BlockedFor(L("withdraw")) ?? GameSync.Blocked(L("withdraw")))
        : AllLeaving ? L("Already leaving vault") + Environment.NewLine + L("Undo takes it back")
        : ShortcutText.Dimmed(
            Acting.Count > 1 ? L($"Withdraw {Acting.Count} selected items to character") : L("Withdraw selected item to character"),
            Session.IsLoaded ? null : L("Require open character."));

    /// <summary>Why Withdraw is off, for a menu's row: the reason alone, the row being its name.</summary>
    private string WithdrawWhy => !Session.IsLoaded ? L("Require open character.") : WithdrawHint;

    /// <summary>The up arrows on the cards whose rows leave the vault with the next save.</summary>
    private void ShowMoves()
    {
        if (_moves is null)
            return;

        foreach (var card in Cards)
            card.IsLeaving = _moves.IsWithdrawing(card.Key);

        OnPropertyChanged(nameof(CanWithdraw));
        OnPropertyChanged(nameof(WithdrawHint));
    }

    /// <summary>Delete and Move to folder take what is selected - items, and the folder's folders on its sheet; the folder shown has its own, on its row.</summary>
    public bool CanDeleteSelection => HasSelection;

    public bool CanMove => HasSelection;

    public string MoveHint => ShortcutText.Dimmed(L("Move to folder"), CanMove ? null : L("Nothing selected."));

    private void SaySelectionChanged()
    {
        SayEquipTools();

        // A page's tile wears the ring its card's frame wore.
        foreach (var card in Cards)
            card.IsSelected = !IsPicking && ReferenceEquals(card, SelectedEntry);
        foreach (var tile in SheetFolders)
            tile.IsSelected = !IsPicking && ReferenceEquals(tile, SelectedEntry);

        // One card, not a pick of several: the Edit page edits a single item, as the inventory does. The left
        // page's: the right page is never beside the Edit page, which is the right page's own.
        if (IsPrimary)
        {
            if (_reloading == 0)
                _vaults?.Choose(IsPicking ? null : SelectedEntry as ItemTileViewModel);
            _vaults?.SetPicked(IsPicking ? PickedCount : 0);
        }

        OnPropertyChanged(nameof(HasActing));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedFolder));
        OnPropertyChanged(nameof(CanRenameFolder));
        OnPropertyChanged(nameof(RenameFolderHint));
        OnPropertyChanged(nameof(CanMove));
        OnPropertyChanged(nameof(MoveHint));
        OnPropertyChanged(nameof(CanWithdraw));
        OnPropertyChanged(nameof(CanCut));
        OnPropertyChanged(nameof(CanCopy));
        OnPropertyChanged(nameof(CutHint));
        OnPropertyChanged(nameof(CopyHint));
        OnPropertyChanged(nameof(WithdrawHint));
        OnPropertyChanged(nameof(DeleteHint));
        OnPropertyChanged(nameof(CanDeleteSelection));
        OnPropertyChanged(nameof(PagesText));

        // What the bin was armed for has changed, so the arming goes - the inventory's rule.
        DisarmDelete();
    }

    partial void OnSelectedEntryChanged(object? value)
    {
        // The list chose something while several were selected - a plain click on a card or a
        // folder: that one alone is the selection now, as Explorer's click makes it.
        if (IsPicking && value is not null)
        {
            IsPicking = false;
            SelectedEntry = value;
            return;
        }

        SaySelectionChanged();
    }

    /// <summary>
    /// Whether the bin has been clicked once and is waiting to be meant - the inventory page's
    /// two-click delete: a bin that turns into a red tick, and a second click on it to confirm.
    /// Any other click takes the arming back (see the page's pressed handler).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteHint))]
    private bool _deleteArmed;

    public void DisarmDelete() => DeleteArmed = false;

    public string DeleteHint => DeleteArmed
        ? L("Confirm deletion") + Environment.NewLine + L("Click elsewhere to cancel")
        : ShortcutText.With(
            ActingFolders.Count == 0 ? Acting.Count > 1 ? L($"Delete {Acting.Count} selected items") : L("Delete selected item")
            : ActingFolders.Count == 1 && Acting.Count == 0 ? L("Delete folder")
            : L($"Delete {ActingFolders.Count + Acting.Count} selected"),
            L("Del"));

    /// <summary>How many are picked: items, and the folder's folders on its sheet.</summary>
    public int PickedCount => Cards.Count(card => card.IsPicked) + SheetFolders.Count(tile => tile.IsPicked);

    /// <summary>What the item commands act on: the ticked cards, or the one selected. Items only - a folder is never withdrawn or copied.</summary>
    private IReadOnlyList<ItemTileViewModel> Acting =>
        IsPicking
            ? [.. Cards.Where(card => card.IsPicked)]
            : Selected is { } one ? [one] : [];

    /// <summary>
    /// The folders that are not inside another of them: a folder picked with its own parent goes where the parent
    /// goes, in it, as Explorer moves and deletes a tree.
    /// </summary>
    private static IReadOnlyList<FolderCard> Outermost(Vault store, IReadOnlyList<FolderCard> folders)
    {
        var inner = folders.SelectMany(folder => store.Subtree(folder.Id).Where(id => id != folder.Id)).ToHashSet();
        return [.. folders.Where(folder => !inner.Contains(folder.Id))];
    }

    /// <summary>The card selected on the pages: an item's - an object, as the picking passes what it picks.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Selected))]
    private object? _selectedEntry;

    public ItemTileViewModel? Selected => SelectedEntry as ItemTileViewModel;

    // ---------------------------------------------------------------------------------------
    // Where in the vault the grid is.
    // ---------------------------------------------------------------------------------------

    /// <summary>The folder being shown. Null is the vault's top level.</summary>
    private long? _here;

    /// <summary>Every folder, read whole with each reload - see <see cref="Vault.Folders"/>.</summary>
    private IReadOnlyList<VaultFolder> _folders = [];

    /// <summary>The page's title: back to the vault's top level from anywhere.</summary>
    [RelayCommand]
    private void GoHome() => GoInto(null);

    /// <summary>One folder up, as Explorer's up arrow.</summary>
    [RelayCommand]
    private void GoUp()
    {
        if (_here is { } id)
            GoInto(_folders.FirstOrDefault(folder => folder.Id == id)?.ParentId);
    }

    public bool CanGoUp => _here is not null;

    /// <summary>A folder gone to, from the tree, a crumb or a list of folders.</summary>
    public void GoTo(long? folder) => GoInto(folder);

    /// <param name="byWay">Gone to by Back or Forward: the ways are theirs to keep, not this step's.</param>
    private void GoInto(long? folder, bool byWay = false)
    {
        _sfxManager?.Method_ButtonSFX_Press();

        if (folder == _here)
            return;

        if (!byWay)
        {
            _wayBack.Add(_here);
            _wayForward.Clear();
        }

        _here = folder;
        SayHere();
        IsPicking = false;

        SelectedEntry = null;
        Notice = "";
        Reload();
        ScrolledHome?.Invoke();
    }

    /// <summary>
    /// The top of the path: "Vault" - "Vault / Charms / Skillers".
    /// </summary>
    private string TopName => L("Vault");

    /// <summary>A folder's place, as the path line writes it: "Softcore\Charms\Skillers".</summary>
    private string PathOf(long? folder)
    {
        var steps = new List<string>();
        for (long? at = folder; at is { } id && _folders.FirstOrDefault(f => f.Id == id) is { } step; at = step.ParentId)
            steps.Insert(0, step.Name);

        steps.Insert(0, TopName);
        return string.Join(FolderPlace.Step, steps);
    }

    /// <summary>
    /// Every folder of the vault, the vault itself first, in the tree's order: the rows of the folder tree and
    /// of the list of folders under the path; see <see cref="FolderPlace"/>.
    /// </summary>
    public ObservableCollection<FolderPlace> Places { get; } = [];

    /// <summary>The folder shown, among them: its path is what the path's tooltip and its last button say.</summary>
    [ObservableProperty] private FolderPlace? _place;

    // ---- quick access, as Windows Explorer has --------

    /// <summary>The folders pinned to quick access, in the order pinned: rows of their own over the folder tree.</summary>
    public ObservableCollection<FolderPlace> QuickPlaces { get; } = [];

    /// <summary>Something is pinned: the section is there.</summary>
    public bool HasQuick => QuickPlaces.Count > 0;

    /// <summary>The folder shown is pinned: the tools' button is lit.</summary>
    public bool IsQuick => _here is { } id && QuickPlaces.Any(place => place.Id == id);

    /// <summary>A folder is shown, which can be pinned: the vault's top is the tree's first row already.</summary>
    public bool CanQuick => _here is not null;

    public string QuickHint => IsQuick ? L("Unpin from quick access")
        : ShortcutText.Dimmed(L("Pin to quick access"), CanQuick ? null : L("Top level cannot be pinned."));

    /// <summary>The tools' button: the folder shown pinned to quick access, or taken off.</summary>
    [RelayCommand]
    private void ToggleQuick()
    {
        if (_here is { } folder)
            PinFolder(folder, !IsQuick);
    }

    private void RebuildPlaces(IReadOnlyDictionary<long, int> counts, int atTop)
    {
        // Where the folder shown has gone - deleted, say - the one it was in is where to be, or the top; and
        // where the inventory stores to: left pointing at the folder, it was storing into nothing.
        if (_here is { } shown && _folders.All(folder => folder.Id != shown))
        {
            _here = _hereParent is { } above && _folders.Any(folder => folder.Id == above) ? above : null;
            SayHere();
        }

        var showing = _here is { } id ? _folders.FirstOrDefault(folder => folder.Id == id) : null;
        _hereParent = showing?.ParentId;

        // A folder that is no more keeps no fold: the next to come by its id would begin unfolded.
        var ids = _folders.Select(folder => folder.Id).ToHashSet();
        _unfoldedTargets.IntersectWith(ids);

        // Which are locked: a mark on each one's row and tile, and the tools' button lit on the one shown.
        IReadOnlySet<long> locked = new HashSet<long>();
        bool topLocked = false;
        try
        {
            if (Store is { } asked)
                (locked, topLocked) = (asked.LockedFolders(), asked.IsLocked(null));
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
        }

        var places = new List<FolderPlace>
        {
            new(null, TopName, 0, TopName, atTop) { IsHere = _here is null, IsLocked = topLocked, Folders = _folders.Count(each => each.ParentId is null) },
        };

        // The folders the one shown lies under: the folded one of them is lit in its place (FolderPlace.IsTreeHere).
        // They are not unfolded for it: the tree is folded and unfolded by hand alone.
        var holding = new HashSet<long>();
        for (long? at = _hereParent; at is { } step; at = _folders.FirstOrDefault(each => each.Id == step)?.ParentId)
        {
            if (!holding.Add(step))
                break;
        }

        void Add(long? parent, int depth)
        {
            foreach (var folder in _folders.Where(each => each.ParentId == parent))
            {
                places.Add(new FolderPlace(folder.Id, folder.Name, depth, PathOf(folder.Id), counts.GetValueOrDefault(folder.Id),
                                           parent, _folders.Any(each => each.ParentId == folder.Id))
                {
                    IsOpen = IsFolderOpen(folder.Id),
                    HoldsHere = holding.Contains(folder.Id),
                    IsHere = folder.Id == _here,
                    IsLocked = locked.Contains(folder.Id),
                    Folders = _folders.Count(each => each.ParentId == folder.Id),
                });
                Add(folder.Id, depth + 1);
            }
        }

        Add(null, 1);

        // The same folders, the same rows: only where the page is said again, so a list left open is not shut
        // and the tree is not built again under the pointer.
        if (Places.Count != places.Count || !Places.Zip(places).All(pair => pair.First.Same(pair.Second)))
        {
            Places.Clear();
            foreach (var place in places)
                Places.Add(place);
        }
        else
        {
            // The same folders: what the move picker folded since is folded here, and which of them is the
            // folder shown, and which it lies under, is said again.
            foreach (var (kept, made) in Places.Zip(places))
                (kept.IsOpen, kept.HoldsHere, kept.IsHere, kept.IsLocked) = (made.IsOpen, made.HoldsHere, made.IsHere, made.IsLocked);
        }

        OnPropertyChanged(nameof(IsFolderLocked));
        OnPropertyChanged(nameof(FolderLockHint));

        // Quick access: the folders pinned, as rows of their own over the tree.
        IReadOnlyList<long> pinned = [];
        try
        {
            if (Store is { } asked)
                pinned = asked.QuickAccess();
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
        }

        var quick = pinned.Select(id => places.FirstOrDefault(place => place.Id == id)).OfType<FolderPlace>()
            .Select(place => new FolderPlace(place.Id, place.Name, 1, place.Path, place.Items, place.Parent)
            {
                IsFlat = true,
                Folders = place.Folders,
                IsHere = place.IsHere,
                IsLocked = place.IsLocked,
            }).ToList();
        if (QuickPlaces.Count != quick.Count || !QuickPlaces.Zip(quick).All(pair => pair.First.Same(pair.Second)))
        {
            QuickPlaces.Clear();
            foreach (var place in quick)
                QuickPlaces.Add(place);
        }
        else
        {
            foreach (var (kept, made) in QuickPlaces.Zip(quick))
                (kept.IsHere, kept.IsLocked) = (made.IsHere, made.IsLocked);
        }

        OnPropertyChanged(nameof(HasQuick));
        OnPropertyChanged(nameof(IsQuick));
        OnPropertyChanged(nameof(CanQuick));
        OnPropertyChanged(nameof(QuickHint));

        ShowPlaces();
        Place = Places.FirstOrDefault(place => place.Id == _here);
        ShowWays();
        OnPropertyChanged(nameof(CanFoldTree));
    }

    /// <summary>A folder's chevron, in the tree or a list of folders: its folders put away, or brought back - as in the move picker.</summary>
    [RelayCommand]
    private void FoldPlace(FolderPlace place)
    {
        if (place.Id is not { } id || !place.CanFold)
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        place.IsOpen = !place.IsOpen;
        KeepFolderOpen(id, place.IsOpen);
        ShowPlaces();
    }

    /// <summary>Each place shown where every folder above it is open.</summary>
    private void ShowPlaces()
    {
        var listed = Places.Where(each => each.Id is not null).ToDictionary(each => each.Id!.Value);
        foreach (var place in Places)
        {
            bool shown = true;
            for (long? at = place.Parent; shown && at is { } id && listed.TryGetValue(id, out var above); at = above.Parent)
                shown = above.IsOpen;

            place.IsShown = shown;
        }
    }

    /// <summary>
    /// Whether a folder's own folders are listed under it - in the folder tree and in the move picker alike,
    /// one fold for both: as it was left while the program runs, and folded when it is next started - the tree
    /// then shows the vault's own folders and no more, as Windows Explorer's does: no fold kept across sessions, so
    /// nothing needs cleaning up by hand. Each of the
    /// two vault pages has its own tree. Folds an older settings file kept, by the
    /// vault, the page and the folder, are let go as the settings are read (EditorSettings).
    /// </summary>
    private bool IsFolderOpen(long folder) => _unfoldedTargets.Contains(folder);

    private void KeepFolderOpen(long folder, bool open)
    {
        if (open)
            _unfoldedTargets.Add(folder);
        else
            _unfoldedTargets.Remove(folder);
    }

    /// <summary>The items of the folder shown.</summary>
    private int _itemsHere;

    /// <summary>Items in the whole vault, for the header.</summary>
    private int _everything;

    /// <summary>
    /// Asks the page to put the pages back at the top: another folder's begin at its first, and a folder
    /// sorted or searched is read from its start.
    /// </summary>
    public event Action? ScrolledHome;


    /// <summary>
    /// Empty and nothing to explain. Kept apart from <see cref="HasTrouble"/> so the two
    /// messages never print on top of one another - "The vault is empty" under a line saying
    /// it could not be opened is worse than either alone.
    /// </summary>
    public bool IsPlainlyEmpty => !HasTrouble && !ShowsPages;

    public string EmptyText =>
        IsSearchingHere ? L("No items match.") : _here is null ? L("Vault is empty.") : L("Folder is empty.");

    /// <summary>
    /// The search the right panel is holding. It reaches <b>every folder</b>, deliberately,
    /// replacing the folder-only scope settled when folders went in - so while it is
    /// on, the folder cards go and each item card says where it is kept.
    /// </summary>
    private VaultFilter? Filter => _vaults?.Filter;

    public bool IsSearching => Filter?.IsFiltering == true;

    // ---- carrying cards by drag -----------------

    /// <summary>Cards on their way somewhere by drag: a cell of a page here, or of the inventory on the other page.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GhostParts))]
    [NotifyPropertyChangedFor(nameof(GhostFolders))]
    private bool _isCarrying;

    private List<ItemTileViewModel> _carried = [];

    /// <summary>What the window draws under the pointer while carrying: the cards' items, fanned.</summary>
    public IReadOnlyList<GhostPart> GhostParts =>
        IsCarrying ? [.. _carried.Select((card, n) => new GhostPart(card, n * GhostPart.Fan, n * GhostPart.Fan))] : [];

    /// <summary>
    /// A drag has left a card (DragPick): carry it - with every pick, where it is one - as Explorer's drag
    /// of a file does. One not picked is selected alone.
    /// </summary>
    public bool BeginCarry(object item)
    {
        // A folder's tile: carried as a card is, to a folder it is let go of on.
        if (item is FolderTile tile && SheetFolders.Contains(tile))
        {
            if (IsPicking && tile.IsPicked)
            {
                _carried = [.. Cards.Where(each => each.IsPicked)];
                _carriedFolders = [.. SheetFolders.Where(each => each.IsPicked)];
            }
            else
            {
                if (IsPicking)
                    ClearSelection();

                SelectedEntry = tile;
                _carried = [];
                _carriedFolders = [tile];
            }

            _carriedItem = null;
            _grabbed = null;
            IsCarrying = true;
            return true;
        }

        if (CardOf(item) is not { } card)
            return false;

        if (IsPicking && card.IsPicked)
        {
            _carried = [.. Cards.Where(each => each.IsPicked)];
            _carriedFolders = [.. SheetFolders.Where(each => each.IsPicked)];
        }
        else
        {
            if (IsPicking)
                ClearSelection();

            SelectedEntry = card;
            _carried = [card];
            _carriedFolders = [];
        }

        _carriedItem = null;
        _grabbed = card;
        IsCarrying = true;
        return true;
    }

    /// <summary>The drag let go of with nothing taking it: nothing moved, so nothing to undo.</summary>
    public void EndCarry()
    {
        _carried = [];
        _carriedFolders = [];
        _carriedItem = null;
        _grabbed = null;
        HoverCard(null);
        Twin?.HoverCard(null);
        HoverSheet(false);
        HoverFolder(null);
        Twin?.HoverSheet(false);
        Twin?.HoverFolder(null);
        IsCarrying = false;
    }

    /// <summary>
    /// The carried cards let go of on an inventory cell (the other page): withdrawn there, as
    /// Withdraw withdraws them - the play mode's rule, the game's lock and all. A shared stash
    /// page takes them too (an item stored from there by drag could not be
    /// dragged back); the Withdraw button still fills the character only.
    /// </summary>
    public void WithdrawCarried(GridCell cell)
    {
        var going = _carried;

        // Folders alone: the character takes none.
        if (going.Count == 0)
        {
            EndCarry();
            return;
        }

        // As they lie about the one held, on the sheet: a group keeps its shape where the cells take it,
        // as it does carried from one vault page to the other (MovesTo).
        var held = Held;
        Dictionary<ItemTileViewModel, (int Dx, int Dy)>? shape = going.Count > 1 && held is not null
            ? going.ToDictionary(card => card, card => (
                card.CellX - held.CellX,
                (RowOf(card.Key) ?? 0) - (RowOf(held.Key) ?? 0)))
            : null;

        EndCarry();

        WithdrawCards(going, (cell.Page, cell.Column, cell.Row), shared: cell.Shared, shape);
    }

    /// <summary>The carried card the drag took hold of - the one that goes down under the pointer; the first, where it is not among them.</summary>
    private ItemTileViewModel? Held => _grabbed is not null && _carried.Contains(_grabbed) ? _grabbed : _carried.FirstOrDefault();

    /// <summary>
    /// Items and folders into a folder, or the top level: Move to folder's way and a drop on a folder card's. A
    /// folder lands under its own name, or "Name (2)" where that is taken; one that would go inside itself stays,
    /// and is said.
    /// </summary>
    private void MoveInto(IReadOnlyList<long> items, IReadOnlyList<FolderCard> folders, long? target, string name)
    {
        if (Store is not { } store || items.Count + folders.Count == 0)
            return;

        int moved = 0;
        string? refused = null, trouble = null, renamed = null;

        try
        {
            if (items.Count > 0)
                moved = store.MoveItems(items, target);

            foreach (var folder in Outermost(store, folders))
            {
                // Already there - let go of on its own sheet, pasted where it was cut: it stays, under its own name.
                if (_folders.FirstOrDefault(each => each.Id == folder.Id)?.ParentId == target)
                {
                    refused ??= L("Already in this folder.");
                    continue;
                }

                try
                {
                    // Under another name where its own is taken there: said, the first of them (it was not).
                    string landed = store.MoveFolder(folder.Id, target);
                    if (landed != folder.Name)
                        renamed ??= L($"{folder.Name} is now {landed}.");
                    moved++;
                }
                catch (InvalidOperationException refusal)
                {
                    refused ??= refusal.Message;
                }
            }
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            trouble = problem.Message;
        }

        if (moved == 0)
        {
            Notice = trouble is not null ? L($"Nothing moved: {trouble}") : refused ?? "";
            return;
        }

        Notice = (folders.Count == 0
                ? Plural(moved, $"Moved to {name}.", $"{moved} items moved to {name}.")
                : Plural(moved, $"Moved to {name}.", $"{moved} moved to {name}."))
            + (renamed is null ? "" : " " + renamed)
            + ((trouble ?? refused) is { } said ? " " + said : "");
        SelectedEntry = null;
        ReloadBoth();
    }

    /// <summary>
    /// What the inventory carries, dropped on this list: the folder dropped on, or null for the one shown - stored as it is. Sorted and as a
    /// crafting base are the Store buttons' alone: a drag held Ctrl and Shift for them too, told
    /// nowhere; they are left to the dedicated buttons. The inventory
    /// stores it; the main view model joins the two.
    /// </summary>
    public event Action<long?, VP_ViewModel_Inv_L.StoreAs>? InventoryDropped;

    public void DropFromInventory(long? folder)
    {
        HoverSheet(false);
        InventoryDropped?.Invoke(folder, VP_ViewModel_Inv_L.StoreAs.Open);
    }

    /// <summary>
    /// The one search's form and its list of finds, which this page marks. The form is the
    /// right page's, under its Search bookmark (SearchWindow), and the search
    /// is always the left vault page's (OwnsSearch). Nothing here opens or closes it.
    /// </summary>
    public SearchPanelViewModel? Search { get; }

    /// <summary>
    /// Read again a moment after the search stops changing rather than on every key: a page of
    /// cards takes a few hundred milliseconds to build.
    /// </summary>
    private DispatcherTimer? _searchPause;

    private void WhenFilterChanged()
    {
        _searchPause ??= new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background,
            (_, _) =>
            {
                _searchPause?.Stop();

                // The other page's search: this one only stands back for it, or comes forward again.
                if (!OwnsSearch)
                {
                    Refresh();
                    return;
                }

                Research();
                OnPropertyChanged(nameof(IsSearching));
                ScrolledHome?.Invoke();
            });

        _searchPause.Stop();
        _searchPause.Start();
    }

    /// <summary>
    /// The search changed and nothing else: what it finds worked out again, and the cards and folder tiles the page
    /// holds told which they are - the folder's layout does not follow the search. Every card was made again from its
    /// row for it, some 18 ms a change on a 704-item folder. A page not read yet
    /// marks its cards as it is read (LoadPages). With no cards, the folder is read whole, as before.
    /// </summary>
    private void Research()
    {
        if (Store is not { } store || Cards.Count == 0)
        {
            Reload();
            return;
        }

        try
        {
            var query = Searched();
            _hitKeys = null;
            _findsUnder = null;
            _found = 0;

            if (IsSearchingHere)
            {
                var perFolder = store.HitsPerFolder(query, out int hitsAtTop);
                _findsUnder = FoldersHolding(perFolder);
                _hitKeys = [.. store.HitsIn(_here, query)];
                _found = hitsAtTop + perFolder.Values.Sum();
                Search?.Results.SetVault(_found);
            }
            else if (OwnsSearch)
            {
                Search?.Results.SetVault(0);
            }
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            Reload();
            return;
        }

        foreach (var card in Cards)
            card.IsSearchMiss = _hitKeys is not null && !_hitKeys.Contains(card.Key);
        foreach (var tile in SheetFolders)
            tile.IsSearchMiss = _findsUnder is { } finds && !finds.Contains(tile.Place.Id!.Value);

        Refresh();
        MapChanged?.Invoke();
    }

    public string CountText => Plural(_everything, "1 item", $"{_everything} items");

    /// <summary>
    /// The vault's gold, beside what it holds - as it will be once saved,
    /// so a move staged on the inventory shows here at once.
    /// </summary>
    public string GoldText => L($"{(_moves?.GoldBalance ?? _vaults.Store?.GoldBalance() ?? 0):N0} gold");

    // ---- the vault's gold, set here -----------------------------------------

    /// <summary>A plain press of the stepper, and with control held: the purses' own pacing (GoldViewModel).</summary>
    public const long GoldStep = GoldViewModel.Step, GoldLeap = GoldViewModel.Leap;

    private long Gold => _moves?.GoldBalance ?? _vaults.Store?.GoldBalance() ?? 0;

    /// <summary>
    /// The vault's gold in the stepper's box. Typed, it is written on leaving the box - each keystroke would be a
    /// line in the gold log.
    /// </summary>
    public string GoldTyped
    {
        get => Gold.ToString();
        set
        {
            if (long.TryParse(value, out long typed))
                ChangeGold(Math.Max(0, typed) - Gold);
        }
    }

    public bool CanLowerGold => Gold > 0;

    public string RaiseGoldHint =>
        L($"Add gold{Environment.NewLine}")
        + L($"LMB: {GoldStep:N0}{Environment.NewLine}")
        + L($"Ctrl + LMB: {GoldLeap:N0}");

    public string LowerGoldHint =>
        L($"Take gold{Environment.NewLine}")
        + L($"LMB: {GoldStep:N0}{Environment.NewLine}")
        + L($"Ctrl + LMB: {GoldLeap:N0}{Environment.NewLine}")
        + L("Shift + LMB: all of it");

    [RelayCommand]
    private void RaiseGold() => ChangeGold(+GoldStep);

    [RelayCommand]
    private void LowerGold() => ChangeGold(-GoldStep);

    /// <summary>
    /// Control and shift on the stepper, from the page's modified press. Shift goes to the end, as on every
    /// stepper: taking, that is all of it. Adding has no end - the vault has no "full" -
    /// so Shift there is no more than Ctrl's handful (it added five million, an end that
    /// was none).
    /// </summary>
    public void StepGold(bool up, bool all) =>
        ChangeGold(up ? GoldLeap : -(all ? Gold : GoldLeap));

    private void ChangeGold(long by)
    {
        if (_moves is null || by == 0)
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        if (_moves.EditGold(by) is { } refused)
            Say(refused);

        SayGold();
    }

    private void SayGold()
    {
        OnPropertyChanged(nameof(GoldText));
        OnPropertyChanged(nameof(GoldTyped));
        OnPropertyChanged(nameof(CanLowerGold));
    }

    /// <summary>What the search asks for, of every folder: where each find is, the list says.</summary>
    private VaultQuery Searched() => Filter?.Query() ?? VaultQuery.Everything;

    /// <summary>
    /// Reads the vault again. Called when the page is shown, because that is the first moment
    /// the game's tables are certain to be loaded and the season therefore known.
    /// </summary>
    public void Show()
    {
        WasShown = true;
        Reload();
    }

    /// <summary>
    /// Reads of the list under way. A reload lets go of the card selected and selects its new card again: told of each,
    /// whoever follows the choice saw "nothing selected" in between, and Trade's sale beside the page closed and threw
    /// its search away at every reload. What is chosen is said once, when it is over.
    /// </summary>
    private int _reloading;

    private void Reload()
    {
        _reloading++;
        try
        {
            ReadTheList();
        }
        finally
        {
            _reloading--;
            if (_reloading == 0 && IsPrimary)
                _vaults?.Choose(IsPicking ? null : SelectedEntry as ItemTileViewModel);
        }
    }

    private void ReadTheList()
    {
        // Found again by key after the reload: KEEP on the Edit page writes the item and reads the
        // vault again, and the page must go on showing the item it was editing.
        long? keptKey = (SelectedEntry as ItemTileViewModel)?.Key;
        long? keptFolder = SelectedFolder?.Place.Id;

        // And the picks, by key: a reload made new cards and their ticks went with the old ones - after a duplicate,
        // a move, a sort, a search's change, the inventory storing - where the inventory's picks stayed.
        // What has left the folder is not found, and goes.
        _keptPicks = IsPicking ? [.. Cards.Where(card => card.IsPicked).Select(card => card.Key)] : null;
        _keptFolderPicks = IsPicking ? [.. SheetFolders.Where(tile => tile.IsPicked).Select(tile => tile.Place.Id!.Value)] : null;

        Cards.Clear();

        if (Store is not { } store)
        {
            SheetFolders.Clear();
            Refresh();
            return;
        }

        try
        {
            _folders = store.Folders();

            var counts = store.ItemsPerFolder();
            int atTop = store.Count(null);
            RebuildPlaces(counts, atTop);
            var query = Searched();

            // What the search found, where one stands: in the folder shown - its sheet lights those and steps
            // the rest back - and in every other, which the Search page's list says.
            _hitKeys = null;
            _findsUnder = null;
            _found = 0;

            if (IsSearchingHere)
            {
                var perFolder = store.HitsPerFolder(query, out int hitsAtTop);

                // A folder's tile is faint where nothing in it, or under it, is a find.
                _findsUnder = FoldersHolding(perFolder);

                var inHere = store.HitsIn(_here, query);
                _hitKeys = [.. inHere];
                _found = hitsAtTop + perFolder.Values.Sum();

                // Where the finds are is the list's to say: the folders' rows go on
                // counting items, in no green. The page says how many; the Search page lists them.
                Search?.Results.SetVault(_found);
            }
            else if (OwnsSearch)
            {
                Search?.Results.SetVault(0);
            }

            // The folder as its pages (VP_ViewModel_Vault_L.Pages.cs), each read as it comes into sight;
            // the one holding the item selected, now, so the selection is back before anything is drawn.
            _everything = store.Count();
            _itemsHere = store.Count(_here);
            Trouble = "";

            if (SelectedEntry is not null)
                SelectedEntry = null;

            _keptKey = keptKey;
            ReloadPages(store);

            if (keptKey is { } kept && _spots.TryGetValue(kept, out var selected))
                LoadPages([PageAt(selected.Page)]);

            // The folder's tile that was selected, selected again where the folder is still here.
            if (keptFolder is { } folderKept && SheetFolders.FirstOrDefault(tile => tile.Place.Id == folderKept) is { } again)
                SelectedEntry = again;

            // The pages the picks lie on, read now: a pick on a page not yet in sight is a pick all the same.
            if (_keptPicks is { Count: > 0 } picks)
            {
                var pages = picks.Where(_spots.ContainsKey).Select(key => _spots[key].Page).Distinct().Select(PageAt).ToList();
                if (pages.Count > 0)
                    LoadPages(pages);
            }
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            Trouble = L($"Vault not read: {problem.Message}");
            PageRows.Clear();
            SheetFolders.Clear();
            Refresh();
            return;
        }

        _keptPicks = null;
        _keptFolderPicks = null;
        Refresh();
        MapChanged?.Invoke();

        // The cards are new: the picks kept are on them again, and what is left of them is several, one or none.
        Normalize();
        SayTicksChanged();
    }

    /// <summary>The keys picked before a reload, ticked again on the new cards as their pages are read.</summary>
    private HashSet<long>? _keptPicks;

    private void WhenCardChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ItemTileViewModel.IsPicked))
        {
            if (_tickingMany > 0)
            {
                _ticksChanged = true;
                return;
            }

            SayTicksChanged();
        }
    }

    private void SayTicksChanged()
    {
        OnPropertyChanged(nameof(PickedCount));
        SaySelectionChanged();
    }

    private int _tickingMany;

    private bool _ticksChanged;

    /// <summary>
    /// Many cards ticked or let go at once - Ctrl+A, Esc, the menu's three: said once, after the last. What is
    /// said counts the ticked cards over, a dozen times, so said a card it was the folder squared: 3 s for
    /// Ctrl+A over 3,500 items, and over 35,000 the window never answered again (a stress test).
    /// </summary>
    private void TickMany(Action ticks)
    {
        _tickingMany++;
        try
        {
            ticks();
        }
        finally
        {
            if (--_tickingMany == 0 && _ticksChanged)
            {
                _ticksChanged = false;
                SayTicksChanged();
            }
        }
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(ShowsPages));
        OnPropertyChanged(nameof(ShowsTreeNow));
        OnPropertyChanged(nameof(CanGoUp));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(IsPlainlyEmpty));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(GoldText));
        OnPropertyChanged(nameof(PagesText));
        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(IsSearchingHere));
        OnPropertyChanged(nameof(PickedCount));
    }

    /// <summary>
    /// A drag rectangle, or a Ctrl- or Shift-click, has started on the list (DragPick): picking
    /// comes on by itself, as Explorer's does, and the selection goes,
    /// since picking is the tick - ticked first where it is kept. False while picking is not on
    /// offer - it always is: the Edit page beside says how many are selected.
    /// </summary>
    public bool BeginDragPick(bool keepSelected)
    {
        var kept = keepSelected && !IsPicking ? SelectedEntry : null;

        IsPicking = true;
        SelectedEntry = null;

        if (kept is not null)
            SetPicked(kept, true);

        return true;
    }

    // The rest of the drag rectangle's side (IDragPicker): an item's card takes a tick, by its tile or itself.
    // None in a locked folder: its items cannot be picked, as a locked stash page's cannot - one lock, one rule.
    // Only Select all held back; the rectangle, Ctrl + click and Invert picked there, and Delete,
    // Move and Withdraw then acted on what the lock was to keep.
    // A folder's tile takes one in any folder: the lock is its items'.
    public bool? IsPicked(object item) =>
        item is FolderTile tile ? SheetFolders.Contains(tile) ? tile.IsPicked : null
        : IsFolderLocked ? null : CardOf(item)?.IsPicked;

    /// <summary>
    /// A Shift+click's run across the folder's stretches, one sheet as it is drawn: the cards
    /// of every stretch from the one to the other, read first where they are not yet, row by row, left to right.
    /// It stopped at the edge of the 15-row stretch the click was in.
    /// </summary>
    public IReadOnlyList<object>? ReadingOrder(object from, object to)
    {
        // A folder's tile at either end: the folders first, as they stand, then the items down to the one clicked.
        if (from is FolderTile || to is FolderTile)
        {
            int last = new[] { from, to }.Max(each => CardOf(each) is { } card ? PageOf(card) ?? 0 : -1);
            var upTo = last < 0 ? [] : Enumerable.Range(0, last + 1).Select(PageAt).ToList();
            LoadPages(upTo);

            return [.. SheetFolders,
                .. upTo.SelectMany(page => page.Grid.Items.OrderBy(card => card.Top).ThenBy(card => card.Left))
                    .Where(card => IsPicked(card) is not null)];
        }

        if (CardOf(from) is not { } one || CardOf(to) is not { } other
            || PageOf(one) is not { } a || PageOf(other) is not { } b)
        {
            return null;
        }

        var pages = Enumerable.Range(Math.Min(a, b), Math.Abs(a - b) + 1).Select(PageAt).ToList();
        LoadPages(pages);

        return [.. pages.SelectMany(page => page.Grid.Items.OrderBy(card => card.Top).ThenBy(card => card.Left))
            .Where(card => IsPicked(card) is not null)];
    }

    /// <summary>The stretch a card is on, among those read.</summary>
    private int? PageOf(ItemTileViewModel card) =>
        _pages.Values.FirstOrDefault(page => page.Grid.Items.Contains(card))?.Number;

    public void SetPicked(object item, bool picked)
    {
        if (item is FolderTile tile)
        {
            if (SheetFolders.Contains(tile))
                tile.IsPicked = picked;
            return;
        }

        if (picked && IsFolderLocked)
        {
            Notice = L("Folder locked.");
            return;
        }

        if (CardOf(item) is { } card)
            card.IsPicked = picked;
    }

    public void ClearPicks() => TickMany(() =>
    {
        foreach (var card in Cards)
            card.IsPicked = false;
        foreach (var tile in SheetFolders)
            tile.IsPicked = false;
    });

    /// <summary>A click on the list's empty space: nothing selected, one card or several.</summary>
    public void ClickedEmpty() => ClearSelection();

    /// <summary>Nothing selected - a click on empty space, or Esc.</summary>
    public void ClearSelection()
    {
        if (IsPicking)
            IsPicking = false;

        SelectedEntry = null;
    }

    public void EndPick() => Normalize();

    /// <summary>
    /// Several selected, or one, or none - never several of one. A Ctrl-click, a rectangle, a
    /// menu or a reload that leaves a single card makes it the selection; none leaves nothing
    /// selected.
    /// </summary>
    private void Normalize()
    {
        if (!IsPicking || PickedCount >= 2)
            return;

        object? one = Cards.FirstOrDefault(card => card.IsPicked) ?? (object?)SheetFolders.FirstOrDefault(tile => tile.IsPicked);
        IsPicking = false;
        SelectedEntry = one;
    }

    /// <summary>A plain click on a card or a folder while several are selected: that one alone.</summary>
    public void SelectOnly(object entry)
    {
        if (IsPicking)
            IsPicking = false;

        SelectedEntry = entry;
    }

    /// <summary>
    /// Takes the acted-on cards out of the vault for good - or, with a folder selected, asks
    /// what to do with the folder.
    /// </summary>
    [RelayCommand]
    private void Delete()
    {
        _sfxManager?.Method_ButtonSFX_Press();

        if (Store is not { } store)
            return;

        // Undo would have nothing to put back: the item is already on the character. Saved first,
        // so this only stops when the save could not be made.
        if (_moves?.HasWithdrawals == true)
            _moves.SaveNow?.Invoke();
        if (Acting.FirstOrDefault(card => _moves?.IsWithdrawing(card.Key) == true) is not null)
        {
            DisarmDelete();
            Notice = L("Saving. Try again.");
            return;
        }

        // Folders among them that hold something: asked about in the one question, which has what they hold to
        // settle (DeleteWithFolders). Folders that hold nothing are asked about as items are, by the second press:
        // a bin always asks, and an empty folder went at the first.
        var folders = ActingFolderCards;
        if (AsksAboutContents(store, folders))
        {
            DeleteWithFolders(store, folders, Acting);
            return;
        }

        if (!DeleteArmed)
        {
            DeleteArmed = true;

            // Said as the inventory's bin says it (one way on both pages).
            if (folders is [var empty] && Acting.Count == 0)
                Say(L($"Delete {empty.Name}? Click red tick again to confirm."));
            else if (folders.Count > 0)
                Say(L($"Delete {folders.Count + Acting.Count} selected? Click red tick again to confirm."));
            else if (Acting is [var tile])
                Say(L($"Delete {tile.Name}? Click red tick again to confirm."));
            else if (Acting.Count > 1)
                Say(L($"Delete {SelectedCount(Acting.Count)}? Click red tick again to confirm."));
            return;
        }

        DisarmDelete();

        if (folders.Count > 0)
        {
            DeleteFolders(store, Outermost(store, folders), Acting, everything: false);
            return;
        }

        if (Acting is not { Count: > 0 } going)
            return;

        try
        {
            store.Remove(going.Select(card => card.Key));
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            Notice = L($"Not deleted: {problem.Message}");
            return;
        }

        Notice = "";
        Say(going is [var only]
            ? L($"Deleted {only.Name}.")
            : Plural(going.Count, "Deleted 1 item.", $"Deleted {going.Count} items."));
        SelectedEntry = null;
        ReloadBoth();
    }

    /// <summary>"1 selected item", "3 selected items" - the inventory's words.</summary>
    private static string SelectedCount(int count) =>
        Plural(count, "1 selected item", $"{count} selected items");

    /// <summary>
    /// Whether deleting these opens the question of what they hold - one of them holds an item or a folder. The
    /// bin's second press and a menu's Confirm are then left out: the question is the asking, else a delete is
    /// asked about twice.
    /// </summary>
    private static bool AsksAboutContents(Vault store, IReadOnlyList<FolderCard> folders) =>
        folders.Any(folder => !IsEmpty(store, folder));

    /// <summary>The same, of what the bin would take now: the item menu's Delete asks in the menu only where this is false.</summary>
    private bool DeleteAsksAboutContents => Store is { } store && AsksAboutContents(store, ActingFolderCards);

    /// <summary>Whether a folder holds nothing at all, in it or anywhere under it.</summary>
    private static bool IsEmpty(Vault store, FolderCard folder)
    {
        var below = store.Subtree(folder.Id);
        var counts = store.ItemsPerFolder();
        return below.Count == 1 && below.Sum(id => counts.GetValueOrDefault(id)) == 0;
    }

    /// <summary>
    /// The user's three answers for the folder shown when it holds anything: the folder only, with what it holds
    /// going to the vault's top level; everything beneath it; or nothing. An empty folder has nothing to ask about
    /// and simply goes. One folder at a time.
    /// </summary>
    private void AskToDelete(Vault store, FolderCard folder)
    {
        var below = store.Subtree(folder.Id).ToHashSet();
        var counts = store.ItemsPerFolder();
        int items = below.Sum(id => counts.GetValueOrDefault(id));
        int folders = below.Count - 1;

        if (items == 0 && folders == 0)
        {
            DeleteFolders(store, [folder], [], everything: false);
            return;
        }

        // "3 items in 4 folders", itself among them.
        string holds = (items, folders) switch
        {
            (_, 0) => Plural(items, "1 item", $"{items} items"),
            (0, _) => Plural(folders, "1 folder", $"{folders} folders"),
            _ => Plural(items, $"1 item in {folders + 1} folders", $"{items} items in {folders + 1} folders"),
        };

        _confirm.Ask(
            L("Delete folder"),
            L($"{folder.Name} holds {holds}."),
            L("DELETE FOLDER"),
            () => DeleteFolders(store, [folder], [], everything: false),
            L("DELETE ALL"),
            () =>
            {
                // It may hold a card on its way out, which Undo could then not put back.
                if (_moves?.HasWithdrawals == true)
                    _moves.SaveNow?.Invoke();
                if (_moves?.HasWithdrawals == true)
                {
                    Notice = L("Saving. Try again.");
                    return;
                }

                DeleteFolders(store, [folder], [], everything: true);
            },
            confirmHint: L("Delete folder only.") + Environment.NewLine + L("Contents move to top level."),
            alternativeHint: L("Delete folder and contents."), deletes: true);
    }

    /// <summary>
    /// Deletes the folders - outermost ones only, the rest go inside them - and the items picked with them, as one
    /// step of the history.
    /// </summary>
    private void DeleteFolders(Vault store, IReadOnlyList<FolderCard> folders, IReadOnlyList<ItemTileViewModel> loose, bool everything)
    {
        int held = 0, moved = 0, gone = 0, renamed = 0;
        string? trouble = null;

        try
        {
            if (loose.Count > 0)
                store.Remove(loose.Select(card => card.Key));

            foreach (var folder in folders)
            {
                var done = store.DeleteFolder(folder.Id, everything);
                held += done.Items;
                moved += done.Items + done.Folders;
                renamed += done.Renamed;
                gone++;
            }
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            trouble = problem.Message;
        }

        int count = folders.Count + loose.Count;
        if (trouble is not null)
            Notice = folders is [var failed] && loose.Count == 0 ? L($"Not deleted: {failed.Name}. {trouble}") : L($"Not all deleted: {trouble}");
        else if (folders is [var folder] && loose.Count == 0)
            Notice = everything
                ? Plural(held, $"Deleted {folder.Name} with 1 item.", $"Deleted {folder.Name} with {held} items.")
                : moved == 0
                    ? L($"Deleted {folder.Name}.")
                    : L($"Deleted {folder.Name}. Contents moved to top level.");
        else
            Notice = everything
                ? Plural(held, $"Deleted {count} selected with 1 item inside.", $"Deleted {count} selected with {held} items inside.")
                : moved == 0
                    ? L($"Deleted {count} selected.")
                    : L($"Deleted {count} selected. Contents moved to top level.");

        // A folder lifted to the top where its name was taken is there under another: said (it was not).
        if (trouble is null && renamed > 0)
            Notice += " " + Plural(renamed, "1 folder renamed.", $"{renamed} folders renamed.");

        if (gone == 0 && trouble is not null && loose.Count == 0)
            return;

        SelectedEntry = null;
        ReloadBoth();
    }

    // ---------------------------------------------------------------------------------------
    // Making and naming folders.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Makes a folder here and opens its name box, as Explorer does: <c>New folder</c>, or
    /// <c>New folder (2)</c> where that is taken - a tile of the sheet shown, selected, its name to be typed
    /// where it stands, and the page staying where it is.
    /// </summary>
    [RelayCommand]
    private void NewFolder()
    {
        _sfxManager?.Method_ButtonSFX_Press();

        if (Store is not { } store)
            return;

        long made;

        try
        {
            made = store.CreateFolder(_here, store.FreeName(_here, L("New folder")));
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            Notice = L($"Folder not made: {problem.Message}");
            return;
        }

        // Read again on both pages - the other has it in its tree - and named on its tile here.
        ReloadBoth();
        RenameFolderInPlace(made);
    }

    /// <summary>
    /// Keeps what was typed, from Enter or from leaving the box - Explorer keeps a name on
    /// both. A name that cannot be used is said and the old one stays.
    /// </summary>
    public void CommitRename(FolderRename rename)
    {
        var card = rename.Card;

        // Enter commits and then takes the focus away, which commits again. Once is enough.
        if (!ReferenceEquals(card.Rename, rename))
            return;

        card.Rename = null;

        if (Store is not { } store || rename.Name.Trim() == card.Name)
            return;

        try
        {
            store.RenameFolder(card.Id, rename.Name);
            Notice = "";
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            Notice = problem.Message;
            return;
        }

        ReloadBoth();
    }

    /// <summary>Escape in the name box: the name stays as it was.</summary>
    public void CancelRename(FolderRename rename)
    {
        if (ReferenceEquals(rename.Card.Rename, rename))
            rename.Card.Rename = null;
    }

    // ---------------------------------------------------------------------------------------
    // Moving items between folders. A picker, dressed as every other picker in the app.
    // ---------------------------------------------------------------------------------------

    [ObservableProperty] private bool _isPickerPresent;

    [ObservableProperty] private bool _isPickerOpen;

    partial void OnIsPickerOpenChanged(bool value) =>
        Presence.Keep(value, () => IsPickerOpen, on => IsPickerPresent = on);

    /// <summary>Everywhere the picked items could go: the tree, less the folder they are in.</summary>
    public ObservableCollection<MoveTarget> Targets { get; } = [];

    /// <summary>The items the open picker will move, fixed when it opened.</summary>
    private IReadOnlyList<long> _moving = [];

    /// <summary>The folders it will move with them.</summary>
    private IReadOnlyList<FolderCard> _movingFolders = [];

    [ObservableProperty] private string _moveTitle = "";

    [RelayCommand]
    private void OpenMove()
    {
        _sfxManager?.Method_ButtonSFX_Press();

        var moving = Acting;
        var folders = ActingFolderCards;
        if (moving.Count + folders.Count == 0)
        {
            Notice = L("No item selected.");
            return;
        }

        OpenMoveFor(moving, folders);
    }

    /// <summary>
    /// The search list's Move found to folder: the move picker opened for every item the search found in the
    /// vault, whichever folder it lies in - the list shows five hundred of them at the most, this takes them
    /// all. The folder shown is a place to move them to here: they are not all in it.
    /// </summary>
    public void OpenMoveForFound()
    {
        if (Store is not { } store || !IsSearchingHere)
            return;

        IReadOnlyList<long> keys;
        // Not what lies in a locked folder: that stays where it was laid.
        int kept = 0;
        try
        {
            var locked = store.LockedFolders();
            bool topLocked = store.IsLocked(null);
            var rows = store.Page(Searched(), "Name", false, 0, int.MaxValue);
            keys = [.. rows.Where(row => row.FolderId is { } id ? !locked.Contains(id) : !topLocked).Select(row => row.Key)];
            kept = rows.Count - keys.Count;
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            Notice = L($"Nothing moved: {problem.Message}");
            return;
        }

        if (keys.Count == 0)
        {
            Notice = kept > 0 ? L("Found items are in locked folders.") : L("No item found.");
            return;
        }

        if (kept > 0)
            Notice = Plural(kept, "1 found item stays in a locked folder.", $"{kept} found items stay in locked folders.");

        OpenMoveFor(keys, [], Plural(keys.Count, "Move 1 found item", $"Move {keys.Count} found items"), fromHere: false);
    }

    /// <summary>The move picker opened for these: the selection's, or the folder shown (the folder tools' Move folder).</summary>
    private void OpenMoveFor(IReadOnlyList<ItemTileViewModel> moving, IReadOnlyList<FolderCard> folders)
    {
        string only = moving is [var item] ? item.Name : folders is [var folder] ? folder.Name : "";
        int count = moving.Count + folders.Count;
        OpenMoveFor([.. moving.Select(card => card.Key)], folders,
                    count == 1 ? L($"Move {only}") : folders.Count == 0 ? L($"Move {moving.Count} items") : L($"Move {count} selected"));
    }

    /// <summary>
    /// The picker itself, for items by their keys. <paramref name="fromHere"/>: they all lie in the folder
    /// shown, which is then no place to move them and is not listed.
    /// </summary>
    private void OpenMoveFor(IReadOnlyList<long> moving, IReadOnlyList<FolderCard> folders, string title, bool fromHere = true)
    {
        _moving = moving;
        _movingFolders = folders;
        MoveTitle = title;

        // A folder moved goes nowhere inside itself: it and its folders are not listed.
        var away = folders.Select(each => each.Id).ToHashSet();

        Targets.Clear();

        if (_here is not null || !fromHere)
            Targets.Add(new MoveTarget(null, TopName, 0, null, holdsFolders: false));

        void Add(long? parent, int depth)
        {
            foreach (var folder in _folders.Where(each => each.ParentId == parent))
            {
                if (away.Contains(folder.Id))
                    continue;

                // The folder the items are in is no place to move them, and is not listed: its own
                // folders stand a step in all the same, under no chevron of its own.
                if (folder.Id != _here || !fromHere)
                {
                    Targets.Add(new MoveTarget(folder.Id, folder.Name, depth, parent,
                                               _folders.Any(each => each.ParentId == folder.Id))
                    {
                        IsOpen = IsFolderOpen(folder.Id),
                    });
                }

                Add(folder.Id, depth + 1);
            }
        }

        Add(null, _here is null && fromHere ? 0 : 1);
        ShowTargets();

        if (Targets.Count == 0)
        {
            Notice = L("No other folder.");
            return;
        }

        IsPickerOpen = true;
    }

    /// <summary>
    /// The folders unfolded in the move picker and the folder tree, by id: kept while the program runs, so the
    /// tree opens as it was left - and no longer than that (<see cref="IsFolderOpen"/>).
    /// This vault's: emptied when another is opened.
    /// </summary>
    private readonly HashSet<long> _unfoldedTargets = [];

    /// <summary>A folder's chevron in the move picker: its folders put away, or brought back.</summary>
    [RelayCommand]
    private void FoldTarget(MoveTarget target)
    {
        if (target.Id is not { } id || !target.HoldsFolders)
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        target.IsOpen = !target.IsOpen;
        KeepFolderOpen(id, target.IsOpen);
        ShowTargets();

        // The folder tree is the same fold, and in sight beside the picker: it follows at once - but for a
        // folder the one shown lies under, which the tree keeps open.
        if (Places.FirstOrDefault(place => place.Id == id) is { } row)
        {
            row.IsOpen = target.IsOpen;
            ShowPlaces();
        }
    }

    /// <summary>Each target shown where every folder above it is open. A folder not listed - the one the items are in - hides nothing.</summary>
    private void ShowTargets()
    {
        var listed = Targets.Where(each => each.Id is not null).ToDictionary(each => each.Id!.Value);
        foreach (var target in Targets)
        {
            bool shown = true;
            for (long? at = target.Parent; shown && at is { } id; at = _folders.FirstOrDefault(each => each.Id == id)?.ParentId)
                shown = !listed.TryGetValue(id, out var above) || above.IsOpen;
            target.IsShown = shown;
        }
    }

    [RelayCommand]
    private void ClosePicker() => IsPickerOpen = false;

    [RelayCommand]
    private void MoveTo(MoveTarget target)
    {
        _sfxManager?.Method_ButtonSFX_Press();
        IsPickerOpen = false;

        var items = _moving;
        var folders = _movingFolders;
        _moving = [];
        _movingFolders = [];
        MoveInto(items, folders, target.Id, target.Name);
    }

    /// <summary>
    /// Takes the acted-on items out onto the open character, as the play mode's rule says:
    /// <list type="bullet">
    /// <item><b>Move</b> - the item lands marked and its row leaves the vault when the character
    /// is saved; see <see cref="VaultMoves"/>.</item>
    /// <item><b>Copy</b> - a copy, and a duplicate is allowed and said.</item>
    /// </list>
    /// </summary>
    [RelayCommand]
    private void Withdraw()
    {
        WithdrawCards(Acting, at: null);
    }

    /// <summary>
    /// What a withdrawal will take out, and what stays and why: the items read back with their
    /// own ids, and the cards left - already leaving, quest items, unreadable. <c>Copies</c> counts
    /// those the character already holds.
    /// </summary>
    private sealed record Batch(
        List<(ItemTileViewModel Card, D2SLib.Model.Save.Item Item)> Coming,
        int Pending, int Quest, int Unreadable, int Copies);

    /// <summary>
    /// The checks every withdrawal passes, to a room, to a cell or onto a worn slot: the game's
    /// lock, a character open with nothing in its hand, then item by item the play mode's rule. Null, and said, when nothing may
    /// go at all.
    /// </summary>
    private Batch? Prepare(IReadOnlyList<ItemTileViewModel> going)
    {
        if (going.Count == 0)
            return null;

        // Asked of the process at the click, not only of the last look.
        if (_sync?.RefusedWhileRunning(L("withdraw")) == true)
            return null;

        if (!Session.IsLoaded)
        {
            Notice = L("Require open character.");
            return null;
        }

        if (Session.IsHolding)
        {
            Notice = L("Require held item placed.");
            return null;
        }

        if (Store is not { } store)
            return null;

        // What the character already holds, so a copy can be said as one. Withdrawing the same
        // template twice in one go makes a copy too, so the batch adds to it as it goes.
        var held = new HashSet<uint>(Session.ItemIds);

        var coming = new List<(ItemTileViewModel Card, D2SLib.Model.Save.Item Item)>();
        int quest = 0, copies = 0, unreadable = 0, pending = 0;

        foreach (var card in going)
        {
            // Already on the character, waiting for a save to leave the vault.
            if (_moves?.IsWithdrawing(card.Key) == true)
            {
                pending++;
                continue;
            }

            D2SLib.Model.Save.Item item;
            try
            {
                item = ItemBlob.Restore(card.Row.Blob, store.StatIdBits);
            }
            catch (Exception)
            {
                unreadable++;
                continue;
            }

            // A second quest item can break the quest it belongs to. The vault keeps them, and
            // does not hand them out.
            if (PD2SPCompanion.Core.Items.ItemBases.IsQuestItem(item.Code))
            {
                quest++;
                continue;
            }

            // The item's own id, put back: the same item going in and coming out. Two items
            // sharing one inside a character is how a duplicate shows.
            if (card.Row.OriginId is { } id)
                item.Id = id;

            coming.Add((card, item));
        }

        foreach (var (card, _) in coming)
        {
            if (card.Row.OriginId is { } id && !held.Add(id))
                copies++;
        }

        return new Batch(coming, pending, quest, unreadable, copies);
    }

    /// <summary>Whether withdrawn rows leave with the next save: always, where the page has the moves to record.</summary>
    private bool Moving => _moves is not null;

    /// <summary>
    /// Writes down what of <paramref name="batch"/> landed on the character: under Move the row
    /// leaves with the next save. (What came out was written down too, as never a find, until vault format 20.)
    /// </summary>
    private void Record(Batch batch)
    {
        if (!Moving)
            return;

        foreach (var (card, item) in batch.Coming)
        {
            // On the character, or dropped on a shared stash page.
            if (Session.Holds(item) || _stash.Holds(item))
                _moves!.Withdrew(card.Key, item);
        }
    }

    /// <summary>What the batch left behind, said after what it did - with those that found no room.</summary>
    private static IEnumerable<string> Remarks(Batch batch, IReadOnlyList<ItemTileViewModel> going, int noRoom = 0)
    {
        if (batch.Pending > 0)
        {
            yield return batch.Pending == 1 && going.Count == 1
                ? L($"{going[0].Name} already leaving vault.")
                : L($"{batch.Pending} already leaving vault.");
        }
        if (noRoom > 0) yield return L($"{noRoom} did not fit.");
        if (batch.Copies > 0) yield return Plural(batch.Copies, "1 already held.", $"{batch.Copies} already held.");
        if (batch.Quest > 0) yield return Plural(batch.Quest, "1 quest item stays in vault.", $"{batch.Quest} quest items stay in vault.");
        if (batch.Unreadable > 0) yield return L($"{batch.Unreadable} unreadable.");
    }

    /// <summary>Where the Withdraw button's items land: the page a pane shows, then the first room (ItemLanding).</summary>
    private readonly ItemLanding _landing;

    /// <summary>
    /// Withdraw's work, on <paramref name="going"/>: onto the page the inventory's panes show - a shared one
    /// too - then into the first room on the character (ItemLanding), or dropped on a
    /// cell (<paramref name="at"/>) - there, or packed into that container, all or none. With
    /// <paramref name="shared"/>, the cell is on a shared stash page.
    /// </summary>
    private void WithdrawCards(IReadOnlyList<ItemTileViewModel> going, (byte Page, int X, int Y)? at, bool shared = false,
                               IReadOnlyDictionary<ItemTileViewModel, (int Dx, int Dy)>? shape = null)
    {
        _sfxManager?.Method_ButtonSFX_Press();

        if (Prepare(going) is not { } batch)
            return;

        var coming = batch.Coming;

        // Where each lay about the one held, for those that may come: the cells take them so, or each finds its room.
        IReadOnlyList<(int Dx, int Dy)>? lying = shape is null ? null
            : [.. coming.Select(each => shape.TryGetValue(each.Card, out var from) ? from : (0, 0))];

        var (placed, noRoom, refusedCount, refused) = coming.Count == 0 ? (0, [], 0, null)
            : at is { } cell ? (shared ? _stash.WelcomeAt(coming.Select(each => each.Item).ToList(), cell.Page, cell.X, cell.Y, lying)
                                       : Session.WelcomeAt(coming.Select(each => each.Item).ToList(), cell.Page, cell.X, cell.Y, lying))
            : _landing.Welcome(coming.Select(each => each.Item).ToList());

        // Withdrawn by the button onto a shared page a pane shows: said as a drop there is.
        if (at is null && placed > 0)
        {
            var landed = coming.Select(each => each.Item).Where(item => Session.Holds(item) || _stash.Holds(item)).ToList();
            shared = landed.Count > 0 && landed.All(_stash.Holds);
        }

        // A drop that does not fit where it was aimed moves nothing at all, as a drag's move.
        if (at is not null && placed == 0 && noRoom.Count > 0)
        {
            Notice = refused ?? L("No free space.");
            return;
        }

        Record(batch);

        var remarks = Remarks(batch, going, noRoom.Count).ToList();
        if (refused is not null)
            remarks.Add(refusedCount == 1 ? refused : L($"{refusedCount} refused. {refused}"));

        // What moved, first. Nothing moved: only why, where something says why ("0 items moved" would open a
        // refusal).
        var said = new List<string>();
        if (placed > 0 || remarks.Count == 0)
        {
            string where = shared ? L("shared stash") : L("character");
            said.Add(Moving
                ? Plural(placed, $"1 item moved to {where}.", $"{placed} items moved to {where}.")
                : Plural(placed, $"1 item copied to {where}.", $"{placed} items copied to {where}."));
        }
        said.AddRange(remarks);

        // What arrived is said where it arrived, on the left; nothing arrived, where it was asked for.
        if (placed > 0)
            SayAcross(string.Join(" ", said));
        else
            Notice = string.Join(" ", said);
    }

    /// <summary>
    /// The carried card let go of on a worn slot: withdrawn as Withdraw
    /// withdraws it - the play mode's rule, the game's lock and all - and put on as the
    /// inventory's own drop on a slot puts on: the character's slot, or the mercenary's; what was
    /// worn there comes off to the first room; the requirements judged as the play mode judges
    /// them. One card at a time, as there.
    /// </summary>
    public void WearCarried(D2SLib.Model.Save.ItemLocation at, bool mercenary)
    {
        var going = _carried;
        EndCarry();
        if (going.Count > 0)
            WearCards(going, at, mercenary);
    }

    /// <summary>The inventory page's choice of a worn slot for an item (VP_ViewModel_Inv_L.SlotAt), joined by the main view model.</summary>
    public Func<D2SLib.Model.Save.Item, bool, int, (EquipSlotViewModel? Slot, string? Why)>? SlotAt { get; set; }

    /// <summary>
    /// Whether an item can be put on, the character or the mercenary: how many slots take it, why none does, and
    /// whether it is gear at all (the inventory page's EquipChoice, joined by the main view model).
    /// </summary>
    public Func<D2SLib.Model.Save.Item, bool, (int Slots, string? Why, bool IsGear, string Does)>? EquipChoice { get; set; }

    /// <summary>How many a withdrawal begun on this card takes: the cards acted on, where it is one of them.</summary>
    public int MovedWith(ItemTileViewModel tile) =>
        Acting.Contains(tile) ? Math.Max(1, Acting.Count) : 1;

    /// <summary>
    /// The inventory page's store of one item into a folder here (VP_ViewModel_Inv_L.StoreInto), joined by the main
    /// view model: where the piece a card pushed off goes - the vault, in the card's own folder.
    /// </summary>
    public Func<D2SLib.Model.Save.Item, long?, bool>? StoreDisplaced { get; set; }

    /// <summary>Whether this list is the right page, the inventory beside it (the main view model's to say): a card is put on only then.</summary>
    public Func<bool>? IsOnRight { get; set; }

    /// <summary>
    /// A card withdrawn and put on, the character or the mercenary: the menu's Equip (<see cref="MenuFor(ItemTileViewModel)"/>;
    /// following the inventory's logic). <b>Only while the vault is the right page</b>,
    /// the inventory beside it, everything visible: what is put on,
    /// and what comes off for it, is then in sight. <paramref name="index"/>: which of the slots that take it, 0
    /// the one meant first, 1 the other. Said on the strip.
    /// </summary>
    public void Wear(ItemTileViewModel card, bool mercenary = false, int index = 0)
    {
        if (IsCarrying)
            return;

        if (IsOnRight?.Invoke() != true)
        {
            _sfxManager?.Method_ButtonSFX_Press();
            Notice = L("Require inventory page open.");
            return;
        }

        if (SlotAt?.Invoke(card.Item, mercenary, index) is not { Slot: { } slot })
        {
            _sfxManager?.Method_ButtonSFX_Press();
            Notice = SlotAt?.Invoke(card.Item, mercenary, index).Why ?? L("Not equipped.");
            return;
        }

        WearCards([card], slot.Slot.Location, mercenary);
    }

    private void WearCards(IReadOnlyList<ItemTileViewModel> going, D2SLib.Model.Save.ItemLocation at, bool mercenary)
    {
        _sfxManager?.Method_ButtonSFX_Press();

        if (going.Count > 1)
        {
            Notice = L("Not several at once.");
            return;
        }

        if (Prepare(going) is not { } batch)
            return;

        if (batch.Coming.Count == 0)
        {
            Notice = string.Join(" ", Remarks(batch, going));
            return;
        }

        var (card, item) = batch.Coming[0];

        // What comes off for it, named before anything moves: the piece worn there, and the other hand's for a
        // weapon held in both.
        var displaced = Session.PushedOffBy(item, at, mercenary)
            .Select(pushed => (Item: pushed, Name: ItemPresenter.Describe(pushed).Name)).ToList();

        // The vault's id kept, as a withdrawal to a cell keeps it: a shared id is how a copy shows.
        string? said = Session.Wear(item, at, arriving: true, force: Rules.ForcesEquip, out bool worn, mercenary, keepId: true);
        if (!worn)
        {
            Notice = said ?? L("Not equipped.");
            return;
        }

        // What it pushed off takes the card's place: into the vault, in the card's folder - a swap, one in and one
        // out. Refused there - the play mode's rule, the game's lock - it stays on the
        // character's side, on to the shared page a pane shows as everything that arrives lands (ItemLanding),
        // and the strip says where.
        string? pushed = null;
        foreach (var (off, offName) in displaced)
        {
            if (StoreDisplaced?.Invoke(off, card.Row.FolderId) == true)
                continue;

            _landing.Rehome(off);
            if (_landing.WhereIs(off) is { } spot)
                pushed = (pushed is null ? "" : pushed + " ") + L($"{offName} to {ItemLanding.Named(spot)}.");
        }

        Record(batch);

        string done = said is null
            ? (Moving ? L($"{card.Name} moved and equipped.") : L($"{card.Name} copied and equipped."))
            : (Moving ? L($"{card.Name} moved.") : L($"{card.Name} copied.")) + " " + said;
        if (pushed is not null)
            done += " " + pushed;

        // Put on the character: said on the left, where it is worn.
        SayAcross(string.Join(" ", Remarks(batch, going).Prepend(done)));
    }

    /// <summary>
    /// The carried card the drag took hold of, as its item - the one that lands on the cell it is let go of
    /// on, the rest lying about it as they lay on the sheet, or finding their own room (WithdrawCards): what
    /// the inventory lights the landing cells for. Null with nothing carried.
    /// </summary>
    public D2SLib.Model.Save.Item? CarriedLead => IsCarrying ? Held?.Item : null;

    /// <summary>
    /// The one carried card's item, read once, so a worn slot can light green or red under it -
    /// null when several are carried or it will not read.
    /// </summary>
    public D2SLib.Model.Save.Item? CarriedItem
    {
        get
        {
            if (!IsCarrying || _carried.Count != 1 || Store is not { } store)
                return null;

            try
            {
                return _carriedItem ??= ItemBlob.Restore(_carried[0].Row.Blob, store.StatIdBits);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    private D2SLib.Model.Save.Item? _carriedItem;

    /// <summary>The vault itself is the service's to close.</summary>
    public void Dispose() { }
}
