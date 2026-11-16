using CommunityToolkit.Mvvm.ComponentModel;

using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.ViewModels;


using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PD2SPCompanion.Shell.Services.Vault;

/// <summary>
/// The vault item the Edit page's item panel is editing - the inventory's panel, over this host
/// rather than the character's session, so the two keep selections of their own.
/// <para>
/// One item at a time, the one card selected on the vault page; while several are picked, how
/// many. Every change is written back into the vault in place, the item under <b>a new id</b> (see
/// <see cref="PD2SPCompanion.Core.Collection.Vault.Replace"/>) from the first change of a run. What reaches
/// past the item is refused: there is no inventory to empty a socket into, and no hand to put a
/// crafted copy in.
/// </para>
/// </summary>
public sealed partial class VaultEditHost : ObservableObject, IItemEditHost
{
    private readonly VaultService _vaults;
    private readonly CharacterSession _status;

    public VaultEditHost(VaultService vaults, CharacterSession status)
    {
        _vaults = vaults;
        _status = status;

        vaults.ChosenChanged += () =>
        {
            // The list read again after this page's own edit: sorted out when that reading is over (Reread).
            if (_rereading)
                return;

            Adopt();
        };

        vaults.PickedChanged += () =>
        {
            OnPropertyChanged(nameof(IsPicking));
            OnPropertyChanged(nameof(PickedCount));
        };
    }

    /// <summary>The card selected on the vault page, as this page's item.</summary>
    private void Adopt()
    {
        // What the last edit owed the list is paid before the item changes: the edited one is read again.
        if (_reread?.IsEnabled == true && _vaults.Chosen?.Key != _key)
        {
            _reread.Stop();
            Avalonia.Threading.Dispatcher.UIThread.Post(Reread, Avalonia.Threading.DispatcherPriority.Background);
        }

        _key = _vaults.Chosen?.Key;
        _editedKey = null;
        SelectedItem = _vaults.Chosen;
    }

    /// <summary>The stored item's key, which every change is written back to.</summary>
    private long? _key;

    // ---- kept at once ---------------------------------------------------------------------------------------
    // A change is written into the vault as it is made, as the character's is into the character: the row is
    // the item as it stands, at every moment - nothing waits on a pause, so a card withdrawn a moment after
    // an edit is the edited item. Two things are spared the typist. The id: an edited item gets a new one
    // (Vault.Replace), once for a run of changes to it, not one a number. And the list: reading the vault
    // again makes a new card and a new tile, which handed to the panel would build its rows again under the
    // caret - so the vault page is told of the one row (VaultService.SayRewritten), and the whole of it is
    // read again a moment after the last change, for what is counted off items - the search's finds, the
    // duplicates - with this page keeping the tile it is editing.

    /// <summary>The key of the item a run of changes is being made to, and the id the first of them gave it.</summary>
    private long? _editedKey;

    private uint _editedId;

    private Avalonia.Threading.DispatcherTimer? _reread;

    private bool _rereading;

    /// <summary>The vault read again after an edit of this page's: the panel keeps its item where it is still the one selected.</summary>
    private void Reread()
    {
        _reread?.Stop();

        _rereading = true;
        try
        {
            _vaults.SayChanged();
        }
        finally
        {
            _rereading = false;
        }

        // Still the one: the tile being edited stays the panel's. Anything else - the selection moved, the
        // card gone - is the list's to say.
        if (_vaults.Chosen is { } chosen && chosen.Key == _key && SelectedItem is not null)
            return;

        Adopt();
    }

    [ObservableProperty] private ItemTileViewModel? _selectedItem;

    /// <summary>Several are selected on the vault page beside: the panel says how many, as beside the inventory.</summary>
    public bool IsPicking => _vaults.Picked > 0;

    public int PickedCount => _vaults.Picked;

    public ObservableCollection<string> SelectedFacts { get; } = [];

    partial void OnSelectedItemChanged(ItemTileViewModel? value) => ShowFacts();

    private void ShowFacts()
    {
        SelectedFacts.Clear();
        if (SelectedItem is { } tile)
        {
            foreach (string fact in ItemFacts.Of(tile))
                SelectedFacts.Add(fact);
        }
    }

    /// <summary>The app's one status line, which the character's session holds.</summary>
    public string Status
    {
        get => _status.Status;
        set => _status.Status = value;
    }

    /// <summary>The right page's strip, which the character's session holds too.</summary>
    public MessageLog RightMessages => _status.RightMessages;

    /// <summary>Made the way a deposit is: written at the vault's version and read back.</summary>
    public Item? Copy(Item item)
    {
        if (_vaults.Store is not { } store)
            return null;

        try
        {
            return ItemBlob.Copy(item, store.StatIdBits);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>A change kept: the edited item goes back into the vault, under a new id - one for a run of changes.</summary>
    public void MarkEdited()
    {
        if (SelectedItem is not { } tile || _key is not { } key || _vaults.Store is not { } store)
            return;

        uint? id;
        string? refused;
        try
        {
            id = store.Replace(key, tile.Item, out refused, _editedKey == key ? _editedId : null);
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            id = null;
            refused = problem.Message;
        }

        if (id is null)
        {
            // The vault item on this page, refused: said here, not under the left page.
            RightMessages.Post(L($"{tile.Name} not written to vault: {refused}"));
            return;
        }

        (_editedKey, _editedId) = (key, id.Value);

        // The card's row at once - what a withdrawal a moment later takes is the edited item - and the whole
        // list a moment after the last change.
        _vaults.SayRewritten(key);
        try
        {
            _reread ??= new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(600), Avalonia.Threading.DispatcherPriority.Background, (_, _) => Reread());
            _reread.Stop();
            _reread.Start();
        }
        catch (Exception)
        {
            // No dispatcher (a test harness): read again now.
            Reread();
        }
    }

    /// <summary>Every change is the vault's at once (IItemEditHost.KeepsAtOnce).</summary>
    public bool KeepsAtOnce => true;

    /// <summary>The vault item by its key: a run of changes to it is one step of the history.</summary>
    public void EditStreak(ItemTileViewModel tile) => _status.SetEditStreak($"vault:{_key}");

    public void SelectedItemChanged()
    {
        SelectedItem?.Rebuilt();
        ShowFacts();
    }

    public int MaximumSockets(ItemTileViewModel tile) => ItemBases.MaximumSockets(tile.Item);

    /// <summary>There is nowhere in the vault for a gem to go, so none of them has room.</summary>
    public IReadOnlyList<Item> WithoutRoom(IEnumerable<Item> gems) => [.. gems];

    public string? Unsocket(ItemTileViewModel tile, Item gem)
    {
        string refused = L("Require character to empty sockets.");
        RightMessages.Post(refused);
        return refused;
    }

    /// <summary>Banned on the Edit page rather than refused at KEEP.</summary>
    public bool CanEmptySockets => false;
}
