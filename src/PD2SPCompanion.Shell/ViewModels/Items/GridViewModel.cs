using System;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// One container of items - the inventory, the belt, the cube - sized in grid cells.
/// <para>
/// Kept across rebuilds (<see cref="GridCache"/>): the tiles are filled in again, the object and
/// the controls drawing it stay. A new grid object per rebuild made new controls, which have no
/// size until the next layout pass - a drop released before it found no cell and put the item
/// back.
/// </para>
/// </summary>
public sealed partial class GridViewModel : ObservableObject
{
    public GridViewModel(string name, byte page, int columns, int rows, bool shared = false)
    {
        Name = name;
        Page = page;
        Shared = shared;
        Columns = columns;
        Rows = rows;
        Cells =
        [
            .. Enumerable.Range(0, rows).SelectMany(
                row => Enumerable.Range(0, columns)
                                 .Select(column => new GridCell(page, column, row, shared))),
        ];

        // The same object is filled again: what is drawn from the tiles is told again.
        Items.CollectionChanged += (_, _) =>
        {
            // Told once at the end of a fill (Fill), not at each tile it adds: each told worked the grid's extent
            // out over every tile, so a fill was the square of its tiles.
            if (_filling)
                return;
            OnPropertyChanged(nameof(Count));
            OnPropertyChanged(nameof(Width));
            OnPropertyChanged(nameof(Height));
        };
    }

    public string Name { get; }

    /// <summary>The page the save numbers this container with.</summary>
    public byte Page { get; }

    /// <summary>
    /// Whether this container is a page of the shared stash rather than one of the
    /// character's own.
    /// <para>
    /// A page number alone does not say which file a container is in, and the two number
    /// their pages over the same range: the character's inventory is page 1 and so is the
    /// first shared page, the cube is 4 and so is the fourth. Without this, dropping an
    /// item held from one onto the other put it on a page of the file it was not in.
    /// </para>
    /// </summary>
    public bool Shared { get; }

    /// <summary>How many cells across, as the game's own table gives it.</summary>
    public int Columns { get; }

    /// <summary>How many cells down.</summary>
    public int Rows { get; }

    /// <summary>
    /// Where to rule the charm section off, or null for a container that has none. Only
    /// the character's own inventory is divided.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDivider))]
    [NotifyPropertyChangedFor(nameof(DividerMargin))]
    private double? _dividerTop;

    public bool HasDivider => DividerTop is not null;

    /// <summary>The line is placed by its top margin, since a Panel has no other way to
    /// put a stretched child at a given height.</summary>
    public Avalonia.Thickness DividerMargin => new(0, DividerTop ?? 0, 0, 0);
    public ObservableCollection<ItemTileViewModel> Items { get; } = [];
    public int Count => Items.Count;

    /// <summary>
    /// One entry per cell of the declared grid, so the page can rule it the way the
    /// game does. Generated rather than drawn as a tiled brush: the grid is only ever a
    /// few hundred cells, and this keeps the lines crisp at any position.
    /// </summary>
    public IReadOnlyList<GridCell> Cells { get; }

    /// <summary>
    /// Readied for a rebuild: no divider, no cell lit under a carried item. The tiles stay until
    /// <see cref="Fill"/> swaps in the new set.
    /// </summary>
    public void Reset()
    {
        DividerTop = null;

        foreach (var cell in Cells)
        {
            cell.IsTarget = false;
            cell.IsAllowed = false;
            cell.IsIdle = false;
        }
    }

    /// <summary>
    /// The tiles a rebuild settled on, swapped in by difference: a tile kept from the last rebuild
    /// stays, and so does the control drawing it - only what changed is taken out or put in.
    /// </summary>
    public void Fill(IReadOnlyList<ItemTileViewModel> tiles)
    {
        _filling = true;
        try
        {
            var keep = new HashSet<ItemTileViewModel>(tiles, ReferenceEqualityComparer.Instance);
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                if (!keep.Contains(Items[i]))
                    Items.RemoveAt(i);
            }

            var have = new HashSet<ItemTileViewModel>(Items, ReferenceEqualityComparer.Instance);
            foreach (var tile in tiles)
            {
                if (have.Add(tile))
                    Items.Add(tile);
            }
        }
        finally
        {
            _filling = false;
        }

        // A kept tile may have moved within the grid, which changes no item in the collection.
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
    }

    /// <summary>A fill under way: the size is told at its end.</summary>
    private bool _filling;

    public double Width => Extent(tile => tile.CellX + tile.View.GridWidth, Columns);
    public double Height => RunsOn ? Rows * ItemTileViewModel.CellSize : Extent(tile => tile.CellY + tile.View.GridHeight, Rows);

    /// <summary>
    /// A stretch of a sheet that runs on below it (the vault's): as tall as its rows and no
    /// taller, whatever lies on it - an item whose top is on its last row reaches down over the next
    /// stretch, and the grid grown to hold it pushed that stretch's cells out from under the item.
    /// </summary>
    public bool RunsOn { get; init; }

    /// <summary>
    /// The declared size, or whatever the contents actually need if they run past it.
    /// Equipped items are laid out by the caller and have no declared grid, so they
    /// fall back entirely to their own extent.
    /// </summary>
    private double Extent(Func<ItemTileViewModel, int> edge, int declared)
    {
        int used = Items.Count == 0 ? 0 : Items.Max(edge);
        return Math.Max(declared, used) * ItemTileViewModel.CellSize;
    }
}

/// <summary>One empty cell of a grid, for the ruled lines behind the items.</summary>

/// <summary>
/// One grid object per container, handed back emptied for each rebuild while the container keeps
/// its size - the page, the name and the file say which. See <see cref="GridViewModel"/> for why.
/// </summary>
public sealed class GridCache
{
    private readonly Dictionary<(byte Page, string Name, bool Shared), GridViewModel> _grids = [];

    public GridViewModel For(string name, byte page, int columns, int rows, bool shared = false)
    {
        if (_grids.TryGetValue((page, name, shared), out var grid) && grid.Columns == columns && grid.Rows == rows)
        {
            grid.Reset();
            return grid;
        }

        grid = new GridViewModel(name, page, columns, rows, shared);
        _grids[(page, name, shared)] = grid;
        return grid;
    }

    /// <summary>Forgets them all - another character's containers are not these.</summary>
    public void Clear() => _grids.Clear();
}

public sealed partial class GridCell(byte page, int column, int row, bool shared = false)
    : ObservableObject
{
    public byte Page { get; } = page;

    /// <summary>Which file this cell is in. See <see cref="GridViewModel.Shared"/>.</summary>
    public bool Shared { get; } = shared;
    public int Column { get; } = column;
    public int Row { get; } = row;

    public double Left => Column * ItemTileViewModel.CellSize;
    public double Top => Row * ItemTileViewModel.CellSize;
    public double Size => ItemTileViewModel.CellSize;

    /// <summary>
    /// Part of the space the held item would take up from wherever the pointer is. Lit as
    /// a block rather than a single cell, because what matters is whether the item fits -
    /// a one-cell marker under a four-cell item says nothing useful.
    /// </summary>
    [ObservableProperty] private bool _isTarget;

    /// <summary>Whether it would actually go there. Lit green when it would, red when not.</summary>
    [ObservableProperty] private bool _isAllowed;

    /// <summary>
    /// It would go there, but a charm put there would do nothing. Amber rather than red:
    /// red means the move is refused, and this one is not - the game allows it too.
    /// </summary>
    [ObservableProperty] private bool _isIdle;
}
