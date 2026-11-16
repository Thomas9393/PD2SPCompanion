using System;
using System.Collections.Generic;
using System.Linq;

using CommunityToolkit.Mvvm.ComponentModel;

using D2SLib.Model.Save;

using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Services.Generic;

/// <summary>
/// A gem, a rune or a jewel dragged out of the Sockets section's row (the drag alone
/// ejects it), while it is carried: what the window draws under the pointer, as it draws what the inventory
/// and the vault carry, and how the carry is called off where the pointer leaves the book. Nothing has left its
/// socket until the button is let go - the section's own editor does that (SocketsEditor.EndCarry).
/// <para>
/// The compendium's item dragged off its Grail card is carried the same way: from no item's sockets (<see cref="FromRow"/> false), so it lands as an item and is never
/// socketed. Nothing leaves the compendium until it lands (GrailPanel).
/// </para>
/// </summary>
public sealed partial class SocketCarry : ObservableObject
{
    public static SocketCarry Shared { get; } = new();

    /// <summary>What is carried, as the window's ghost draws it: one tile, at the pointer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCarrying))]
    private IReadOnlyList<GhostPart> _parts = [];

    public bool IsCarrying => Parts.Count > 0;

    /// <summary>Calls the carry off: the section's, which began it.</summary>
    public Action? Cancel { get; private set; }

    /// <summary>Whether what is carried comes out of the Edit page's Sockets row - else out of the compendium.</summary>
    public bool FromRow { get; private set; }

    public void Begin(Item gem, Action cancel) => Begin([gem], cancel);

    /// <summary>
    /// Several, where several sockets were selected and one of them dragged: fanned, the one dragged first - as the
    /// inventory draws a group from several places and the vault its cards (they lay one on
    /// another, a pixel apart).
    /// </summary>
    public void Begin(IReadOnlyList<Item> gems, Action cancel, bool fromRow = true)
    {
        Cancel = cancel;
        FromRow = fromRow;
        Parts = [.. gems.Select((gem, n) => new GhostPart(new ItemTileViewModel(gem, 0), n * GhostPart.Fan, n * GhostPart.Fan))];
    }

    public void End()
    {
        Cancel = null;
        FromRow = false;
        Parts = [];
    }

    /// <summary>
    /// The Del key's way to a socket row with a selection: the row last selected in offers its
    /// delete here, and the Items page asks it first - so Del with a socket selected deletes what is
    /// in it, never the item it is in. True where it took the key.
    /// </summary>
    public Func<bool>? DeleteSelection { get; set; }
}
