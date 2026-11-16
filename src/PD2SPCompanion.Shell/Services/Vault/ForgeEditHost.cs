using CommunityToolkit.Mvvm.ComponentModel;

using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.ViewModels;


using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PD2SPCompanion.Shell.Services.Vault;

/// <summary>
/// The item the Forge is making, under the item panel - the inventory's panel, over this host, as the
/// vault's Edit page hosts it over <see cref="VaultEditHost"/>.
/// <para>
/// The item is in no file until it is forged, so KEEP has nowhere to put it: it writes the item and the
/// holder draws it again. What reaches past the item is refused, as in the vault - there is nowhere for
/// a gem to go. What the panel says of its actions goes on the right page's strip: nothing on the
/// character changed.
/// </para>
/// </summary>
public sealed partial class ForgeEditHost : ObservableObject, IItemEditHost
{
    private readonly CharacterSession _session;

    public ForgeEditHost(CharacterSession session) => _session = session;

    [ObservableProperty] private ItemTileViewModel? _selectedItem;

    /// <summary>One item, the one in the holder; picking never shows here.</summary>
    public bool IsPicking => false;

    public int PickedCount => 0;

    /// <summary>Empty: the holder shows the item itself, and the panel is hosted without its header.</summary>
    public ObservableCollection<string> SelectedFacts { get; } = [];

    private string _status = "";

    /// <summary>Kept, and said nowhere: the left strip is the character's, and nothing on it changed.</summary>
    public string Status
    {
        get => _status;
        set => _status = value;
    }

    public MessageLog RightMessages => _session.RightMessages;

    /// <summary>Made as the character's own are: written at the save's version and read back.</summary>
    public Item? Copy(Item item) => _session.Copy(item);

    /// <summary>After KEEP: the item is in no file, so there is nothing to place - but the Forge is told its item changed.</summary>
    public void MarkEdited() => Edited?.Invoke();

    public event System.Action? Edited;

    public void SelectedItemChanged() => SelectedItem?.Rebuilt();

    public int MaximumSockets(ItemTileViewModel tile) => ItemBases.MaximumSockets(tile.Item);

    /// <summary>There is nowhere for a gem to go from an item that is nowhere itself.</summary>
    public IReadOnlyList<Item> WithoutRoom(IEnumerable<Item> gems) => [.. gems];

    public string? Unsocket(ItemTileViewModel tile, Item gem)
    {
        string refused = L("Require character to empty sockets.");
        RightMessages.Post(refused);
        return refused;
    }

    /// <summary>Banned rather than refused at KEEP, as on the vault's Edit page.</summary>
    public bool CanEmptySockets => false;

    /// <summary>Nothing waits on KEEP here: the holder shows the item as it is being changed.</summary>
    public bool KeepsAtOnce => true;

    /// <summary>The Forge's boxes say what the item is: the panel makes it nothing else.</summary>
    public bool ChoosesItsItem => true;

    /// <summary>But takes its base a tier up: the boxes cannot name a unique on a higher base.</summary>
    public bool UpgradesBase => true;

    /// <summary>Its item is nobody's yet: the reroll makes it as a new drop.</summary>
    public bool RollsAsDrop => true;
}
