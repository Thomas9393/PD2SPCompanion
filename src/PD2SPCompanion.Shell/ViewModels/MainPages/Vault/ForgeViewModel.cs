using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using D2SLib.Model.Save;

using PD2SPCompanion.Core.Collection;
using PD2SPCompanion.Core.Trade;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Vault;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// One thing the Forge can name, as the page lists it: the entry (ForgeSearch.Entry) and the colour its
/// name wears, so the lists read as items do.
/// </summary>
public sealed record ForgeChoice(ForgeSearch.Entry Entry, IBrush Brush)
{
    public string Name => Entry.Name;

    /// <summary>The base it is on, the kinds of base a runeword takes, or a base's tier.</summary>
    public string Detail => Entry.Detail;

    public bool HasDetail => Entry.HasDetail;

    /// <summary>A runeword: its kinds run long, and go on a line of their own.</summary>
    public bool IsWord => Entry.IsWord;

    /// <summary>Anything else with something to say beside its name: said on the name's line, at the right.</summary>
    public bool HasAside => Entry.HasDetail && !Entry.IsWord;

    /// <summary>
    /// After the name, for one of several rows of a name: "- Variant: Battle Cry on striking".
    /// Dim, the base still at the right.
    /// </summary>
    public string VariantShown => Entry.HasVariant ? L($"- Variant: {Entry.Variant}") : "";

    public bool HasVariant => Entry.HasVariant;

    public override string ToString() => Name;
}

/// <summary>
/// The Forge: an item made from nothing. The item
/// being made stands in a holder at the top; under it what it is - a type, a base, a rarity, and for a
/// unique, a set piece or a runeword which one, which typing in the search box fills in at once - and under
/// that the item panel itself (<see cref="Editor"/>), so what is made is changed here and not forged first
/// and opened on the Edit page after. FORGE puts it on the left page: the inventory, or the folder the
/// vault shows.
/// <para>
/// What the four boxes offer and how they narrow each other is <see cref="ForgeSearch"/>, in the core,
/// where every order of choosing is run against the tables; this is the page over it - the boxes' bindings,
/// the holder's item, and where it is sent.
/// </para>
/// </summary>
public sealed partial class ForgeViewModel : ObservableObject
{
    private readonly VaultService _vaults;
    private readonly CharacterSession _session;
    private readonly ISFXManager? _sfxManager;

    /// <summary>The item panel under the search, over the holder's item.</summary>
    public VP_ViewModel_Inv_R Editor { get; }

    private readonly ForgeEditHost _host;

    /// <summary>Where a forged item lands on the character's side: the page a pane shows, then the first room.</summary>
    private readonly ItemLanding _landing;

    private readonly ForgeSearch _boxes = new();

    public ForgeViewModel(VaultService vaults, CharacterSession session, ISFXManager? sfxManager, PlayRules rules,
                          ItemLanding landing)
    {
        _vaults = vaults;
        _session = session;
        _sfxManager = sfxManager;
        _landing = landing;

        _host = new ForgeEditHost(session);
        Editor = new VP_ViewModel_Inv_R(_host, sfxManager!, stash: null, rules);

        // FORGE waits while something typed has not been written: a value out of range.
        Editor.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName is nameof(VP_ViewModel_Inv_R.HasChanges) or nameof(VP_ViewModel_Inv_R.CanApply) or null or "")
                ForgeCommand.NotifyCanExecuteChanged();
        };

        // The page wrote a change into the holder's item: it is no longer as the boxes make it.
        _host.Edited += () =>
        {
            _edited = true;
            TellOfItem();
        };

        // Another character, another era: what was made for the last one is not this one's.
        session.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(CharacterSession.IsLoaded))
                Clear();
        };
    }

    /// <summary>The tables the lists were read under: read again under another install.</summary>
    private int _madeAt = -1;

    /// <summary>
    /// The lists made ahead of the page's first showing - behind the splash (MainView.PrepareAllPages): reading
    /// every base, unique, set piece and runeword off the tables was 60 ms of the Forge's first open, the one
    /// page whose first press was still slow once every page was laid out ahead. Nothing is asked
    /// of the clipboard here, as showing the page does.
    /// </summary>
    public void Warm()
    {
        // No game's tables yet - a first start, before the game folder is chosen: nothing to read the lists from,
        // and reading them threw. They are made once tables are in, and the generation has moved.
        if (PD2SPCompanion.Core.GameFiles.InstalledTables.Generation == 0
            || _madeAt == PD2SPCompanion.Core.GameFiles.InstalledTables.Generation)
            return;

        _madeAt = PD2SPCompanion.Core.GameFiles.InstalledTables.Generation;
        _rows.Clear();
        _boxes.Load();
        Tell();
    }

    /// <summary>The page shown: the lists are made the first time, as the Grail's are.</summary>
    public void Load()
    {
        _ = Peek();
        if (_madeAt == PD2SPCompanion.Core.GameFiles.InstalledTables.Generation)
        {
            // Another character since: the boxes still say what to make.
            Make();
            return;
        }

        _madeAt = PD2SPCompanion.Core.GameFiles.InstalledTables.Generation;
        _rows.Clear();
        _boxes.Load();
        Tell();
    }

    // ---- the search section: type, base, rarity, item ----------------------------------------------

    private readonly Dictionary<ForgeSearch.Entry, ForgeChoice> _rows = new(ReferenceEqualityComparer.Instance);

    /// <summary>An entry as the page lists it, made once: a box's choice is found again by being the same row.</summary>
    private ForgeChoice Row(ForgeSearch.Entry entry) =>
        _rows.TryGetValue(entry, out var row)
            ? row
            : _rows[entry] = new ForgeChoice(entry, entry.Sample is { } sample ? RarityPalette.Of(sample) : Brushes.White);

    /// <summary>The search panel's own types, less those the Forge makes no base of.</summary>
    public IReadOnlyList<ForgeSearch.Choice> TypeChoices { get; private set; } = [ForgeSearch.Any];

    /// <summary>The bases of the type chosen, by tier and then by name - or, a runeword chosen, those that take it.</summary>
    public IReadOnlyList<ForgeSearch.Choice> BaseChoices { get; private set; } = [ForgeSearch.Any];

    /// <summary>What the base chosen can be: a ring is never Normal, and only some bases carry a unique.</summary>
    public IReadOnlyList<ForgeSearch.Choice> RarityChoices { get; private set; } = ForgeSearch.AllRarities;

    /// <summary>The uniques, set pieces or runewords the type and the base leave: a base can carry several.</summary>
    public IReadOnlyList<ForgeChoice> NameChoices { get; private set; } = [];

    /// <summary>True while the boxes are being set from here: a list replaced under a box hands back nothing.</summary>
    private bool _settling;

    public ForgeSearch.Choice Type
    {
        get => _shown.Type!;
        set
        {
            if (_settling || value is null || value == _boxes.Type)
                return;

            _boxes.SetType(value);
            Tell();
        }
    }

    public ForgeSearch.Choice Base
    {
        get => _shown.Base!;
        set
        {
            if (_settling || value is null || value == _boxes.Base)
                return;

            _boxes.SetBase(value);
            Tell();
        }
    }

    public ForgeSearch.Choice Rarity
    {
        get => _shown.Rarity!;
        set
        {
            if (_settling || value is null || value == _boxes.Rarity)
                return;

            _boxes.SetRarity(value);
            Tell();
        }
    }

    public ForgeChoice? Named
    {
        get => _shown.Named;
        set
        {
            if (_settling || value is null || ReferenceEquals(value.Entry, _boxes.Named))
                return;

            _boxes.SetNamed(value.Entry);
            Tell();
        }
    }

    /// <summary>
    /// What each box shows. Its own, apart from what the search holds: a box is shown nothing chosen while
    /// its list is replaced under it (see <see cref="Tell"/>).
    /// </summary>
    private (ForgeSearch.Choice? Type, ForgeSearch.Choice? Base, ForgeSearch.Choice? Rarity, ForgeChoice? Named) _shown =
        (ForgeSearch.Any, ForgeSearch.Any, ForgeSearch.AllRarities[ForgeSearch.Unique], null);

    public bool CanPickBase => _boxes.CanPickBase;

    /// <summary>An item is picked for a unique, a set piece and a runeword; the rest are their base.</summary>
    public bool CanPickName => _boxes.CanPickName;

    /// <summary>What the Item box says while several are offered and none is chosen.</summary>
    public string NameHint => _boxes.Rarity.From switch
    {
        ForgeSearch.Unique => L("Choose unique"),
        ForgeSearch.Set => L("Choose set item"),
        ForgeSearch.Runeword => L("Choose runeword"),
        _ => "",
    };

    /// <summary>
    /// The page told what the search now holds, and the item made. Only a list that changed is replaced -
    /// a list replaced closes its box - and its box is shown nothing chosen first: a box whose list goes
    /// from under what it shows lets go of it late, and stood empty over the next choice.
    /// </summary>
    private void Tell()
    {
        _settling = true;
        try
        {
            if (!TypeChoices.SequenceEqual(_boxes.Types))
            {
                _shown.Type = null;
                OnPropertyChanged(nameof(Type));
                TypeChoices = _boxes.Types;
                OnPropertyChanged(nameof(TypeChoices));
            }

            if (!BaseChoices.SequenceEqual(_boxes.Bases))
            {
                _shown.Base = null;
                OnPropertyChanged(nameof(Base));
                BaseChoices = _boxes.Bases;
                OnPropertyChanged(nameof(BaseChoices));
            }

            if (!RarityChoices.SequenceEqual(_boxes.Rarities))
            {
                _shown.Rarity = null;
                OnPropertyChanged(nameof(Rarity));
                RarityChoices = _boxes.Rarities;
                OnPropertyChanged(nameof(RarityChoices));
            }

            if (NameChoices.Count != _boxes.Names.Count
                || !NameChoices.Select(row => row.Entry).SequenceEqual(_boxes.Names, ReferenceEqualityComparer.Instance))
            {
                _shown.Named = null;
                OnPropertyChanged(nameof(Named));
                NameChoices = [.. _boxes.Names.Select(Row)];
                OnPropertyChanged(nameof(NameChoices));
            }

            _shown = (_boxes.Type, _boxes.Base, _boxes.Rarity, _boxes.Named is { } named ? Row(named) : null);

            OnPropertyChanged(nameof(CanPickBase));
            OnPropertyChanged(nameof(CanPickName));
            OnPropertyChanged(nameof(NameHint));
            OnPropertyChanged(nameof(Type));
            OnPropertyChanged(nameof(Base));
            OnPropertyChanged(nameof(Rarity));
            OnPropertyChanged(nameof(Named));
        }
        finally
        {
            _settling = false;
        }

        Make();
    }

    /// <summary>Whether the search section is unfolded: folded, the sections under it have the page.</summary>
    [ObservableProperty] private bool _searchOpen = PD2SPCompanion.Shell.Services.Settings.FoldMemory.IsOpen(nameof(ForgeViewModel) + nameof(SearchOpen));

    partial void OnSearchOpenChanged(bool value) => PD2SPCompanion.Shell.Services.Settings.FoldMemory.Keep(nameof(ForgeViewModel) + nameof(SearchOpen), value);

    [RelayCommand]
    private void ToggleSearch()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        SearchOpen = !SearchOpen;
    }

    // ---- the search box --------------------------------------------------------------------------

    /// <summary>A name, or a base: every word typed, in any order. What it finds fills the four boxes.</summary>
    [ObservableProperty] private string _search = "";

    partial void OnSearchChanged(string value)
    {
        // A new search starts at the first rows again.
        _limit = Shown;
        Find();
    }

    /// <summary>What the search finds, the first of them: named things, then bases.</summary>
    public ObservableCollection<ForgeChoice> Matches { get; } = [];

    private const int Shown = 40;

    /// <summary>How many of the finds are listed: the first <see cref="Shown"/>, and <see cref="Shown"/> more each time the list is scrolled to its end.</summary>
    private int _limit = Shown;

    [ObservableProperty] private bool _hasMatches;

    [ObservableProperty] private string _countText = "";

    /// <summary>Whether the list stops short of what was found - more come as it is scrolled.</summary>
    [ObservableProperty] private bool _isCapped;

    /// <summary>
    /// The next forty finds, added under the ones listed: the list scrolled to its end (a Show all was bad design -
    /// the list loads on as it is read, as a modern one does).
    /// The rows already there stay, so the place in the list is kept.
    /// </summary>
    public void LoadMore()
    {
        if (!IsCapped)
            return;

        _limit += Shown;
        var found = _boxes.Find(Search, _limit, out int total);
        foreach (var entry in found.Skip(Matches.Count))
            Matches.Add(Row(entry));

        IsCapped = total > Matches.Count;
        CountText = IsCapped ? L($"{Matches.Count} of {total}") : total.ToString();
    }

    private void Find()
    {
        Matches.Clear();
        if (!_session.IsLoaded || Search.Trim().Length == 0)
        {
            HasMatches = false;
            IsCapped = false;
            CountText = "";
            return;
        }

        var found = _boxes.Find(Search, _limit, out int total);
        foreach (var entry in found)
            Matches.Add(Row(entry));

        HasMatches = true;
        IsCapped = total > Matches.Count;
        CountText = IsCapped ? L($"{Matches.Count} of {total}") : total.ToString();
    }

    /// <summary>
    /// One of the search's finds chosen: its type, its base and its rarity go in the boxes, and its name
    /// where it has one (ForgeSearch.Pick). A find chosen is made, even the one already in the holder: with
    /// nothing to undo on this page, it is the way back to the item as it is made.
    /// </summary>
    [RelayCommand]
    private void Pick(ForgeChoice? choice)
    {
        if (choice is null)
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        _boxes.Pick(choice.Entry);
        _madeAs = null;

        Search = "";
        Tell();
    }

    // ---- from the game ------------------------------------------------------------------------------

    /// <summary>The clipboard's text, as the page reads it: the view's to give, a window being what has one.</summary>
    public Func<Task<string?>>? Clipboard { get; set; }

    /// <summary>
    /// The name of the item the game copied, while the clipboard holds one: the paste button is lit and says it.
    /// Null, and the button greyed, while it holds anything else.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPaste), nameof(PasteTip))]
    [NotifyCanExecuteChangedFor(nameof(PasteCopiedCommand))]
    private string? _copied;

    public bool CanPaste => Copied is not null;

    public string PasteTip => Copied is { } name ? L($"Paste {name}") : L("Paste from game");

    /// <summary>The text last looked at: the same text is not read twice.</summary>
    private string? _peeked;

    private async Task<string?> Read()
    {
        try
        {
            return Clipboard is { } read ? await read() : null;
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            return null;
        }
    }

    /// <summary>
    /// A look at the clipboard, for the button's sake: whether it holds an item the game copied, and which.
    /// <b>Never on a timer</b> (especially no polling) - when the editor's window comes to
    /// the front, which is the way back from the game, and when the Forge is shown. Nothing is made: a look
    /// that made the item would put it over what the holder has.
    /// </summary>
    public async Task Peek()
    {
        string? text = await Read();
        if (text == _peeked)
            return;

        _peeked = text;
        Copied = _session.IsLoaded ? GameClipboard.NameOf(text) : null;
    }

    /// <summary>The paste button: the clipboard as it is now - not as it was last looked at - made in the holder.</summary>
    [RelayCommand(CanExecute = nameof(CanPaste))]
    private async Task PasteCopied()
    {
        string? text = await Read();
        Paste(text);

        // Gone from the clipboard since the look: the button says so from here on.
        _peeked = text;
        Copied = _session.IsLoaded ? GameClipboard.NameOf(text) : null;
    }

    /// <summary>
    /// The item the game copies on Ctrl+C over one, made again and put in the holder (<see cref="GameClipboard"/>). The
    /// boxes say what it is where the Forge lists it, and are left where it does not - an upgraded unique. It is
    /// the holder's item from there on, changed and forged as any other. The text has no line for some of what an
    /// item holds; where the import had to fill one in, the message says to check.
    /// </summary>
    /// <returns>Whether an item was made.</returns>
    public bool Paste(string? text)
    {
        if (!GameClipboard.LooksLike(text))
        {
            _session.RightMessages.Post(L("No item copied from the game."));
            return false;
        }

        if (!_session.IsLoaded)
            return false;

        if (_session.ItemEra is not { } era)
        {
            _session.RightMessages.Post(L("Require an item on the character."));
            return false;
        }

        try
        {
            _session.UseMyLayout();
            if (GameClipboard.Read(text!, PD2SPCompanion.Core.Saves.ItemSeason.ForNew(era)) is not { } pasted || !PD2SPCompanion.Core.Items.ItemForge.Makes(pasted.Item.Code.Trim()))
            {
                _session.RightMessages.Post(L("Copied item not made."));
                return false;
            }

            // No row this season: written, it would be another unique. Left out, as the armory import leaves one.
            if (PD2SPCompanion.Core.Trade.TradeItems.NoRow(pasted) is { } missing)
            {
                Log.Info("Forge", $"Paste refused: {missing}");
                _session.RightMessages.Post(GameClipboard.NameOf(text!) is { Length: > 0 } named
                    ? L($"{named} not made: not in this season.")
                    : L("Copied item not made."));
                return false;
            }

            var item = pasted.Item;
            item.Id = NewId();

            // The boxes first, then the holder: what they name is counted as made, so they make nothing over it.
            _boxes.Show(item);
            _madeAs = _boxes.What;
            Tell();

            // Not as the boxes would make it - its own rolls, perhaps a higher base: RESET makes that one.
            _edited = true;

            var tile = new ItemTileViewModel(item, 0);
            Draft = tile;

            foreach (string note in pasted.Notes)
                Log.Info("Forge", $"Pasted {tile.Name}: {note}");

            _sfxManager?.Method_ButtonSFX_Press();
            _session.RightMessages.Post(pasted.Sound && !pasted.Notes.Any(note => note.Contains("not in the game's text", StringComparison.Ordinal))
                ? L($"{tile.Name} pasted.")
                : L($"{tile.Name} pasted. Check its lines: the game's text leaves some out."));
            return true;
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            _session.RightMessages.Post(L("Copied item not made."));
            return false;
        }
    }

    // ---- the item ---------------------------------------------------------------------------------

    /// <summary>The item made, as a tile: what the holder shows, and what the panel edits. Null until something is chosen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDraft))]
    [NotifyCanExecuteChangedFor(nameof(ForgeCommand))]
    private ItemTileViewModel? _draft;

    public bool HasDraft => Draft is not null;

    partial void OnDraftChanged(ItemTileViewModel? value)
    {
        _host.SelectedItem = value;
        TellOfItem();
    }

    /// <summary>
    /// Whether the holder's item has been changed since the boxes made it - a number typed, a corruption, an
    /// upgrade - or came from elsewhere (pasted). Kept through FORGE: the holder then holds a copy of what was
    /// forged, changes and all.
    /// </summary>
    private bool _edited;

    private void TellOfItem()
    {
        OnPropertyChanged(nameof(UpgradeNote));
        OnPropertyChanged(nameof(HasUpgradeNote));
        ResetCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Said beside the Base box while the holder's item is on a higher base than the boxes name (the upgrade reads
    /// like a pending change): the Horadric Cube's upgrade was pressed in the holder,
    /// or an upgraded unique was pasted. The boxes keep saying what the item is made as - a unique is offered
    /// on its own base alone - and any of them changed makes the item afresh, the upgrade gone with every
    /// other change.
    /// </summary>
    public string UpgradeNote =>
        Draft is { } tile && _boxes.What?.Code is { } made && ForgeSearch.Upgraded(made, tile.Item.Code)
            ? L($"Upgraded to {ItemPresenter.BaseName(tile.Item.Code.Trim())}")
            : "";

    public bool HasUpgradeNote => UpgradeNote.Length > 0;

    private bool CanReset => HasDraft && _edited && _boxes.What is not null;

    /// <summary>
    /// The holder's item made afresh from the boxes, which stay as they are. Someone who forged a corrupted item and
    /// wants a clean one of the same had no clear way: the Item box picked again does nothing, being what is already
    /// chosen. Every change goes - numbers
    /// typed, a corruption, an upgrade, for which there is no way down. Not a button that empties the page:
    /// that one was skipped and stays skipped. Dim while the item is still as made.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanReset))]
    private void Reset()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        string name = Draft?.Name ?? "";
        Make(again: true);
        if (Draft is not null)
            _session.RightMessages.Post(L($"{name} reset."));
    }

    /// <summary>What the holder's item was made as: the same boxes make nothing new, and an edit is kept.</summary>
    private ForgeSearch.Order? _madeAs;

    /// <summary>What the four boxes name made afresh, in the open character's item era, and shown.</summary>
    private void Make(bool again = false)
    {
        var what = _boxes.What;
        if (what == _madeAs && !again)
            return;

        _madeAs = what;
        _edited = false;
        if (what is null || !_session.IsLoaded)
        {
            Draft = null;
            return;
        }

        // Stamped as this season stamps, where the character may hold it beside its own (ItemSeason.ForNew): a new
        // item is never a legacy one, and a save of an older era than the season before's holds that era alone.
        if (_session.ItemEra is not { } era)
        {
            Draft = null;
            _session.RightMessages.Post(L("Require an item on the character."));
            return;
        }

        string name = what.Named?.Name ?? _boxes.Base.Shown;
        try
        {
            _session.UseMyLayout();
            var item = ForgeSearch.Make(what, PD2SPCompanion.Core.Saves.ItemSeason.ForNew(era));
            item.Id = NewId();
            Draft = new ItemTileViewModel(item, 0);
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            Draft = null;
            _session.RightMessages.Post(L($"{name} not made."));
        }
    }

    private static uint NewId() => (uint)Random.Shared.NextInt64(1, uint.MaxValue);

    private void Clear()
    {
        // Another character: what the clipboard holds is looked at again when the page is next shown.
        _peeked = null;
        Copied = null;
        _madeAs = null;
        Draft = null;
        Search = "";
    }

    /// <summary>Whether FORGE goes to the vault's open folder: the left page shows the vault. Else the inventory.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ForgeTip))]
    private bool _keepsToVault;

    public string ForgeTip => KeepsToVault ? L("Forge to vault") : L("Forge to character");

    /// <summary>
    /// Something to forge, and nothing typed that the item would not take: every change is written as it is
    /// made (the host keeps at once), so what is still a change is a value out of range.
    /// </summary>
    private bool CanForge => HasDraft && !Editor.HasChanges;

    /// <summary>
    /// The item in the holder put on the left page - the inventory (then the cube, then the stash), or the
    /// folder the vault shows. The holder keeps a copy
    /// under a new id, so the same thing can be forged again or changed further. Said where it lands: the left
    /// strip.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanForge))]
    private void Forge()
    {
        if (Draft is not { } tile || !_session.IsLoaded)
            return;

        _sfxManager?.Method_ButtonSFX_Press();

        var item = tile.Item;
        string name = tile.Name;

        // Taken before the item goes anywhere: what lands is given a place, and the holder's is in none.
        _session.UseMyLayout();
        var next = _session.Copy(item);

        if (KeepsToVault)
        {
            if (_vaults.Store is not { } store)
            {
                _session.Messages.Post(L("No vault open."));
                return;
            }

            long? key;
            string? refused;
            try
            {
                key = store.Add(item, _vaults.CurrentFolder, out refused);
            }
            catch (Exception problem)
            {
                Log.Caught(problem);
                key = null;
                refused = problem.Message;
            }

            if (key is null)
            {
                _session.Messages.Post(L($"{name} not kept: {refused}"));
                return;
            }

            _vaults.SayChanged();
            _session.Messages.Post(L($"{name} forged to vault."));
        }
        else
        {
            // Onto the page a pane shows - a shared one too - before the first room
            // (ItemLanding). A refusal says itself, through the character's status.
            if (_landing.Receive(item, L("No free space on the character."), out var landed) is not null)
                return;

            _session.Messages.Post(landed is { Shared: true } shared
                ? L($"{name} forged to shared page {shared.Page}.")
                : item.Page switch
                {
                    InventoryLayout.InventoryPage => L($"{name} forged to inventory."),
                    InventoryLayout.CubePage => L($"{name} forged to cube."),
                    _ => L($"{name} forged to stash."),
                });
        }

        // The one forged is the character's or the vault's now: the holder takes its copy.
        if (next is null)
        {
            Make(again: true);
            return;
        }

        next.Id = NewId();
        Draft = new ItemTileViewModel(next, 0);
    }
}
