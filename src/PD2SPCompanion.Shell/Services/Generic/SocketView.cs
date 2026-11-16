using System;
using System.Collections.Generic;

using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Services.Generic;

/// <summary>
/// Which way a socketed item is read, everywhere an item's lines are shown - a tooltip, the trade frame, the
/// Edit page's summary: as the game reads it, what its sockets give summed into its own
/// lines; or the item's own lines and each socket's beneath "Socketed (n)"
/// (<see cref="PD2SPCompanion.Core.Items.ItemLines"/>). <b>Shift tapped turns it over, and it stays</b> until the
/// next tap or the app closes (held, it was extra effort). One state for the window, as
/// one window has the keys.
/// <para>
/// The tiles whose lines someone has read are told when it changes, so whatever shows them draws them again -
/// held weakly: a tile is kept by its page, not by having been looked at.
/// </para>
/// </summary>
public static class SocketView
{
    /// <summary>Whether the sockets are read apart just now.</summary>
    public static bool Apart { get; private set; }

    /// <summary>The key's name on a cap, and what it does: the line a tooltip's strip and the trade frame show.</summary>
    public static IReadOnlyList<TipHint> Hints { get; } = [new(L("Shift"), L("Socket details, on or off"))];

    private static readonly List<WeakReference<ItemTileViewModel>> _read = [];

    /// <summary>A socketed item's lines were read: it is told of a change from now on.</summary>
    public static void Watch(ItemTileViewModel tile)
    {
        if (_read.Count > 0 && _read[^1].TryGetTarget(out var last) && ReferenceEquals(last, tile))
            return;

        // The dead let go before the list is looked through, so it stays as long as what is alive.
        _read.RemoveAll(each => !each.TryGetTarget(out _));
        if (!_read.Exists(each => each.TryGetTarget(out var known) && ReferenceEquals(known, tile)))
            _read.Add(new WeakReference<ItemTileViewModel>(tile));
    }

    /// <summary>Shift tapped: apart if summed, summed if apart.</summary>
    public static void Toggle() => Set(!Apart);

    /// <summary>The reading set.</summary>
    public static void Set(bool apart)
    {
        if (Apart == apart)
            return;

        Apart = apart;
        foreach (var each in _read.ToArray())
        {
            if (each.TryGetTarget(out var tile))
                tile.SocketViewChanged();
        }
    }
}
