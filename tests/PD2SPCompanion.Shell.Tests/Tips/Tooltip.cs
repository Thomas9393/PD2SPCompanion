using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Tips;

/// <summary>
/// An item's tooltip, as the window's own (ItemActionTip) deals with <see cref="TipActions"/>: up over an item,
/// asked what its strip says and what it is compared with, asked again each time the comparison changes, and
/// away. The comparison is shut while one of these is made, and left as it was found when it goes. Used on the
/// program's thread.
/// </summary>
public sealed class Tooltip : IDisposable
{
    private readonly object _tip = new();
    private readonly bool _found = TipActions.Compares;
    private ItemTileViewModel? _tile;
    private string _origin = TipActions.Grid;

    public Tooltip()
    {
        if (TipActions.Compares)
            TipActions.Toggle();

        TipActions.Changed += Draw;
    }

    /// <summary>What the tooltip was last told: its strip, whether it offers the comparison, and what is compared.</summary>
    public TipInfo? Info { get; private set; }

    /// <summary>The pointer over an item: its tooltip up, and drawn.</summary>
    public Tooltip Over(ItemTileViewModel tile, string origin = TipActions.Grid)
    {
        (_tile, _origin) = (tile, origin);
        TipActions.Opened(_tip, tile);
        Draw();
        return this;
    }

    private void Draw()
    {
        if (_tile is null)
            return;

        Info = TipActions.Describe(_tile, _tile.IsVaults ? TipActions.Vault : _origin);
        TipActions.Offers(_tip, Info is { Offers: true });
    }

    /// <summary>The strip's lines as "keys - what", joined as the old script read them off the screen.</summary>
    public string Strip => string.Join("; ", (Info?.Hints ?? []).Select(hint => $"{hint.Keys} - {hint.Does}"));

    /// <summary>The comparison's caption; "" while it is shut.</summary>
    public string Caption => Info?.Caption ?? "";

    /// <summary>What the comparison shows, by name.</summary>
    public IReadOnlyList<string> Compared => [.. (Info?.Compared ?? []).Select(tile => tile.Name)];

    /// <summary>The pointer away: no tooltip up.</summary>
    public void Away()
    {
        TipActions.Closed(_tip);
        _tile = null;
        Info = null;
    }

    public void Dispose()
    {
        TipActions.Changed -= Draw;
        TipActions.Closed(_tip);
        if (TipActions.Compares != _found)
            TipActions.Toggle();
    }
}
