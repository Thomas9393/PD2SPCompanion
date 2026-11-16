using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Characters;

/// <summary>Whether an item may go somewhere, and why not when it may not.</summary>
public sealed record Placement(bool Allowed, string? Reason)
{
    public static readonly Placement Yes = new(true, null);

    public static Placement No(string reason) => new(false, reason);
}

/// <summary>
/// Moving an item between the containers a character carries.
/// <para>
/// A move is nothing more than changing where an item says it is: its mode, page and cell.
/// The item list keeps its order and every other field is untouched, and gems socketed into
/// an item are stored inside it, so they travel with it without being mentioned here.
/// </para>
/// <para>
/// The rules are only about space - does it fit, and is the space free. Nothing here can
/// produce an item the game would refuse, because nothing here decides what an item is
/// allowed to be. Equipping, which does, is a separate question with rules of its own.
/// </para>
/// </summary>
public static class ItemMover
{
    /// <summary>The cells an item covers if its top-left corner is at (x, y).</summary>
    public static (int Width, int Height) Footprint(Item item)
    {
        var (width, height) = ItemPresenter.FootprintOf(item);
        return (Math.Max(1, width), Math.Max(1, height));
    }

    /// <summary>
    /// Which cells of a page are taken.
    /// <para>
    /// The point is to look at each item once. Asking "does this overlap anything" cell by
    /// cell means every item is measured again at every cell, and measuring an item is a
    /// table lookup - a page of forty items was doing six thousand of them to answer one
    /// question, and a move of forty items did that forty times over.
    /// </para>
    /// <para>
    /// Here the page is read once into a grid of flags, and a cell is then a lookup. It is
    /// also kept <b>across</b> a batch: each item that lands blocks its own cells, so the
    /// next one is asked against the page as it now stands without rebuilding anything.
    /// </para>
    /// </summary>
    public sealed class Occupancy
    {
        private readonly bool[] _taken;

        public Occupancy(GridSize grid, IEnumerable<Item> contents, Item? ignoring = null)
        {
            Grid = grid;
            _taken = new bool[grid.Columns * grid.Rows];

            foreach (var item in contents)
            {
                // An item never blocks itself: moving something two cells left is a move,
                // not a clash with where it used to be.
                if (!ReferenceEquals(item, ignoring))
                    Block(item);
            }
        }

        public GridSize Grid { get; }

        /// <summary>Marks the cells an item sits on, where it sits now.</summary>
        public void Block(Item item)
        {
            var (width, height) = Footprint(item);
            Block(item.X, item.Y, width, height);
        }

        /// <summary>Marks the cells an item would sit on, put down at (x, y).</summary>
        public void Block(int x, int y, int width, int height)
        {
            for (int row = y; row < y + height; row++)
            {
                if (row < 0 || row >= Grid.Rows)
                    continue;

                for (int column = x; column < x + width; column++)
                {
                    if (column >= 0 && column < Grid.Columns)
                        _taken[row * Grid.Columns + column] = true;
                }
            }
        }

        /// <summary>Whether a thing of that size fits at (x, y) with nothing under it.</summary>
        public bool Fits(int x, int y, int width, int height)
        {
            if (x < 0 || y < 0 || x + width > Grid.Columns || y + height > Grid.Rows)
                return false;

            for (int row = y; row < y + height; row++)
            {
                for (int column = x; column < x + width; column++)
                {
                    if (_taken[row * Grid.Columns + column])
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The first corner a thing of that size fits at, scanning the way the game does -
        /// across each row, top to bottom. Null where it will not fit anywhere.
        /// </summary>
        public (int X, int Y)? FirstFree(int width, int height)
        {
            for (int y = 0; y + height <= Grid.Rows; y++)
            {
                for (int x = 0; x + width <= Grid.Columns; x++)
                {
                    if (Fits(x, y, width, height))
                        return (x, y);
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Whether <paramref name="moving"/> may sit with its top-left corner at (x, y) on a
    /// page of the given size, given everything else already on that page.
    /// </summary>
    public static Placement CanPlace(
        Item moving, IEnumerable<Item> pageContents, GridSize grid, int x, int y)
    {
        var (width, height) = Footprint(moving);

        if (x < 0 || y < 0)
            return Placement.No(L("Off the grid."));

        if (x + width > grid.Columns || y + height > grid.Rows)
        {
            return Placement.No(width > grid.Columns || height > grid.Rows
                ? L($"Too large: {width} by {height}, container {grid.Columns} by {grid.Rows}.")
                : L("Hangs off the edge."));
        }

        foreach (var other in pageContents)
        {
            // An item never collides with itself: moving something two cells left is a
            // move, not a clash with where it used to be.
            if (ReferenceEquals(other, moving))
                continue;

            var (otherWidth, otherHeight) = Footprint(other);


            bool overlaps = x < other.X + otherWidth
                            && x + width > other.X
                            && y < other.Y + otherHeight
                            && y + height > other.Y;

            if (overlaps)
                return Placement.No(L($"{ItemPresenter.Describe(other).Name} is in the way."));
        }

        return Placement.Yes;
    }

    /// <summary>
    /// The first free corner an item would fit at, scanning the way the game does - across
    /// each row, top to bottom. Null when it will not fit anywhere.
    /// </summary>
    public static (int X, int Y)? FirstFreeCell(
        Item moving, IEnumerable<Item> pageContents, GridSize grid) =>
        FirstFreeCell(moving, new Occupancy(grid, pageContents, moving));

    /// <summary>
    /// Where each of a group lands with the one held on (x, y) and the rest where they lay about it -
    /// <paramref name="shape"/>, an item's columns and rows from the held one, in <paramref name="items"/>'
    /// order. All of them or null: one that would lie off the page or over something, and the group has no
    /// place there as it is - the caller then finds each its own room.
    /// </summary>
    public static List<(Item Item, int X, int Y)>? ShapedSpots(
        IReadOnlyList<Item> items, IReadOnlyList<(int Dx, int Dy)> shape, IEnumerable<Item> pageContents, GridSize grid, int x, int y)
    {
        if (shape.Count != items.Count)
            return null;

        var page = new Occupancy(grid, pageContents);
        var spots = new List<(Item Item, int X, int Y)>();

        for (int i = 0; i < items.Count; i++)
        {
            var (width, height) = Footprint(items[i]);
            int atX = x + shape[i].Dx, atY = y + shape[i].Dy;
            if (!page.Fits(atX, atY, width, height))
                return null;

            page.Block(atX, atY, width, height);
            spots.Add((items[i], atX, atY));
        }

        return spots;
    }

    /// <summary>
    /// The same, against a page already read. What a batch uses: the map is built once and
    /// each item that lands blocks its own cells, so nothing is measured twice.
    /// </summary>
    public static (int X, int Y)? FirstFreeCell(Item moving, Occupancy page)
    {
        var (width, height) = Footprint(moving);
        return page.FirstFree(width, height);
    }

    /// <summary>
    /// The free corner nearest <paramref name="beside"/>, on its page, for a thing of
    /// <paramref name="moving"/>'s size - a duplicate put down next to its original.
    /// Nearest is the smallest gap between the two, touching first; then the same
    /// rows; then to the right before the left; then the game's own reading order. Null where it
    /// fits nowhere on the page.
    /// </summary>
    public static (int X, int Y)? FreeCellNear(Item moving, IEnumerable<Item> pageContents, GridSize grid, Item beside) =>
        FreeCellNear(moving, new Occupancy(grid, pageContents), beside);

    /// <summary>The same, against a page already read - and perhaps promised in part already (a batch).</summary>
    public static (int X, int Y)? FreeCellNear(Item moving, Occupancy page, Item beside)
    {
        var grid = page.Grid;
        var (width, height) = Footprint(moving);
        var (besideWidth, besideHeight) = Footprint(beside);

        (int X, int Y)? best = null;
        (int, int, int, int, int) bestRank = default;
        for (int y = 0; y + height <= grid.Rows; y++)
        {
            for (int x = 0; x + width <= grid.Columns; x++)
            {
                if (!page.Fits(x, y, width, height))
                    continue;

                int gapX = Math.Max(0, Math.Max(beside.X - (x + width), x - (beside.X + besideWidth)));
                int gapY = Math.Max(0, Math.Max(beside.Y - (y + height), y - (beside.Y + besideHeight)));
                var rank = (Math.Max(gapX, gapY), gapX + gapY, Math.Abs(y - beside.Y), x < beside.X ? 1 : 0, y * grid.Columns + x);
                if (best is null || rank.CompareTo(bestRank) < 0)
                {
                    best = (x, y);
                    bestRank = rank;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Puts an item on a page at a cell. The caller is expected to have asked
    /// <see cref="CanPlace"/> first; this does not check again, so that a refusal and the
    /// move itself cannot disagree about the rules.
    /// </summary>
    public static void Place(Item item, byte page, int x, int y)
    {
        item.Mode = ItemMode.Stored;

        // Stored items carry no body slot. Leaving a stale one behind would describe an
        // item that is both in the inventory and worn.
        item.Location = ItemLocation.None;

        item.Page = page;
        item.X = (byte)x;
        item.Y = (byte)y;
    }
}
