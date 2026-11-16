using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.VisualTree;

using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// What UI Automation is told of a grid's items, now that no item is a control (<see cref="TileLayer"/>): a
/// peer a tile, with what a tile's button said of itself - the id "Tile", the item's name, where it is drawn,
/// whether it is in sight, and Invoke for a click. A screen reader's, and the window tests', which find an item
/// by its name and click where it is.
/// </summary>
internal sealed class TileLayerPeer : ControlAutomationPeer
{
    private readonly Dictionary<ItemTileViewModel, TilePeer> _tiles = new(ReferenceEqualityComparer.Instance);

    public TileLayerPeer(TileLayer owner) : base(owner)
    {
        owner.TilesChanged += InvalidateChildren;
    }

    public new TileLayer Owner => (TileLayer)base.Owner;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

    protected override bool IsContentElementCore() => false;

    protected override bool IsControlElementCore() => true;

    /// <summary>The controls over tiles - the duplicate badge - then a peer for each tile, kept from one asking to the next.</summary>
    protected override IReadOnlyList<AutomationPeer>? GetChildrenCore()
    {
        var children = new List<AutomationPeer>(base.GetChildrenCore() ?? []);
        var shown = new HashSet<ItemTileViewModel>(Owner.Tiles, ReferenceEqualityComparer.Instance);

        foreach (var gone in _tiles.Keys.Where(tile => !shown.Contains(tile)).ToList())
            _tiles.Remove(gone);

        foreach (var tile in Owner.Tiles)
        {
            if (!_tiles.TryGetValue(tile, out var peer))
                _tiles[tile] = peer = new TilePeer(this, tile);

            children.Add(peer);
        }

        return children;
    }

    /// <summary>A place of the layer in the window's coordinates, and whether any of it is in sight.</summary>
    internal (Rect Bounds, bool InSight) Seen(Rect place)
    {
        var layer = Owner;
        if (TopLevel.GetTopLevel(layer) is not { } root || layer.TransformToVisual(root) is not { } toRoot)
            return (default, false);

        var bounds = place.TransformToAABB(toRoot);
        bool inSight = layer.IsEffectivelyVisible
                       && layer.GetTransformedBounds() is { } seen
                       && place.TransformToAABB(seen.Transform).Intersects(seen.Clip);
        return (bounds, inSight);
    }
}

/// <summary>
/// One tile of a <see cref="TileLayer"/>, as a button of its own was to UI Automation. A peer of the layer's
/// control - many of them of the one control - because only a control's peer can say where it is on the screen
/// (the framework turns a place into the screen's through the peer's control, and keeps that to itself: a peer
/// of no control answered with no place at all, and nothing could be clicked). What it says of
/// itself is the tile's: its name, its place in the layer, whether that is in sight.
/// </summary>
internal sealed class TilePeer(TileLayerPeer layer, ItemTileViewModel tile) : ControlAutomationPeer(layer.Owner), IInvokeProvider
{
    private TextPeer? _count;

    public void Invoke() => layer.Owner.Click(tile);

    protected override void BringIntoViewCore() => layer.Owner.BringIntoView(TileLayer.PlaceOf(tile));

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

    protected override string? GetAutomationIdCore() => "Tile";

    protected override Rect GetBoundingRectangleCore() => layer.Seen(TileLayer.PlaceOf(tile)).Bounds;

    /// <summary>A stack's count, as the text a tile's button held: a test reads how many there are off it.</summary>
    protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore() =>
        tile.HasQuantity ? [_count ??= new TextPeer(this, layer, tile)] : [];

    protected override string GetClassNameCore() => "Button";

    protected override AutomationPeer? GetLabeledByCore() => null;

    protected override string? GetNameCore() => tile.Name;

    protected override string? GetHelpTextCore() => null;

    protected override AutomationPeer? GetParentCore() => layer;

    protected override bool HasKeyboardFocusCore() => false;

    protected override bool IsContentElementCore() => true;

    protected override bool IsControlElementCore() => true;

    protected override bool IsKeyboardFocusableCore() => false;

    protected override bool IsOffscreenCore() => !layer.Seen(TileLayer.PlaceOf(tile)).InSight;

    protected override void SetFocusCore()
    {
    }

    protected override bool TrySetParent(AutomationPeer? parent) => false;

    /// <summary>The count drawn on a tile.</summary>
    private sealed class TextPeer(TilePeer parent, TileLayerPeer layer, ItemTileViewModel tile) : ControlAutomationPeer(layer.Owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override string? GetAutomationIdCore() => null;

        protected override Rect GetBoundingRectangleCore() => layer.Seen(TileLayer.PlaceOf(tile)).Bounds;

        protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore() => [];

        protected override string GetClassNameCore() => "TextBlock";

        protected override AutomationPeer? GetLabeledByCore() => null;

        protected override string? GetNameCore() => tile.QuantityText;

        protected override string? GetHelpTextCore() => null;

        protected override AutomationPeer? GetParentCore() => parent;

        protected override bool HasKeyboardFocusCore() => false;

        protected override bool IsContentElementCore() => true;

        protected override bool IsControlElementCore() => true;

        protected override bool IsKeyboardFocusableCore() => false;

        protected override bool IsOffscreenCore() => !layer.Seen(TileLayer.PlaceOf(tile)).InSight;

        protected override void SetFocusCore()
        {
        }

        protected override bool TrySetParent(AutomationPeer? parent) => false;
    }
}
