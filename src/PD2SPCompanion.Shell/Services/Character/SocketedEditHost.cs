using CommunityToolkit.Mvvm.ComponentModel;

using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.ViewModels;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PD2SPCompanion.Shell.Services.Character;

/// <summary>
/// A jewel in an item's socket, under the item panel - a second panel over this host, opened from the first
/// one's Sockets section: a click opens a second layer that edits the jewel in
/// situ, with the same keep and undo at that level. Until then a socketed jewel was edited by taking it out, editing
/// it and putting it back - and the editor could not put one back.
/// <para>
/// The jewel is in no list of its own: it lies inside the item that holds it, which is wherever that item's
/// host has it - the character, a shared page, the vault, the Forge's holder. So everything that reaches the
/// file is the parent's to do, handed over when the jewel is opened: what KEEP calls once it has written the
/// jewel (<see cref="Marked"/>: the parent's file is the one that changed), and what draws the parent again
/// (<see cref="Redrawn"/>: its tile shows the jewel in its socket, its tooltip the jewel's lines).
/// </para>
/// <para>
/// A jewel has no sockets and is no base for anything else, so what reaches past it is refused, and the panel
/// offers nothing that would make it another kind of item.
/// </para>
/// </summary>
public sealed partial class SocketedEditHost(IItemEditHost parent) : ObservableObject, IItemEditHost
{
    /// <summary>The jewel, as a tile of its own; null while the layer is shut.</summary>
    [ObservableProperty] private ItemTileViewModel? _selectedItem;

    partial void OnSelectedItemChanged(ItemTileViewModel? value) => TellFacts();

    /// <summary>The parent's file marked as changed: called after KEEP wrote the jewel.</summary>
    public Action? Marked { get; set; }

    /// <summary>The parent drawn again off its item.</summary>
    public Action? Redrawn { get; set; }

    public bool IsPicking => false;

    public int PickedCount => 0;

    public ObservableCollection<string> SelectedFacts { get; } = [];

    private void TellFacts()
    {
        SelectedFacts.Clear();
        if (SelectedItem is { } tile)
        {
            foreach (string fact in ItemFacts.Brief(tile, withId: true))
                SelectedFacts.Add(fact);
        }
    }

    public string Status
    {
        get => parent.Status;
        set => parent.Status = value;
    }

    public MessageLog RightMessages => parent.RightMessages;

    public Item? Copy(Item item) => parent.Copy(item);

    public void MarkEdited() => Marked?.Invoke();

    public void SelectedItemChanged()
    {
        SelectedItem?.Rebuilt();
        TellFacts();
        Redrawn?.Invoke();
    }

    public int MaximumSockets(ItemTileViewModel tile) => 0;

    public IReadOnlyList<Item> WithoutRoom(IEnumerable<Item> gems) => [.. gems];

    public string? Unsocket(ItemTileViewModel tile, Item gem) => L("Not in current item.");

    public bool CanEmptySockets => false;

    public bool KeepsAtOnce => parent.KeepsAtOnce;

    /// <summary>The parent's: a run of changes to a jewel is a run of changes to the file its item lies in.</summary>
    public void EditStreak(ItemTileViewModel tile) => parent.EditStreak(tile);

    /// <summary>A jewel in a socket stays a jewel: nothing here makes it another kind of item.</summary>
    public bool ChoosesItsItem => true;
}
