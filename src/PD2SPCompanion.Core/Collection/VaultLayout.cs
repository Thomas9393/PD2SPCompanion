namespace PD2SPCompanion.Core.Collection;

/// <summary>An item as the layout needs it: its key and the cells it takes.</summary>
public readonly record struct VaultFootprint(long Key, int Width, int Height);

/// <summary>Where the layout put an item: the column and the row of its top left cell, and the room it takes.</summary>
public readonly record struct VaultSpot(long Key, int X, int Y, int Width, int Height);

/// <summary>A folder laid out: every item's spot, in the order they were laid, and how many rows it took.</summary>
public sealed record VaultSheet(IReadOnlyList<VaultSpot> Spots, int Rows);

/// <summary>
/// A vault folder laid out by a rule: a vault item has no place of its own any more - nobody cares about it so long
/// as the content is readable immediately - so a folder is laid out from its order, the items keeping their shapes, with as few gaps as the
/// order allows.
/// <list type="number">
/// <item>A <b>shelf</b> runs the sheet's width and is as tall as the tallest item that opens a lane in it.</item>
/// <item>A <b>lane</b> is two columns wide. The next item in the order opens it, top left; the room it leaves -
/// beside it, under it - takes the nearest later items that fit, looking no further ahead than
/// <see cref="Look"/>. A lane whose second column stayed empty is one column wide.</item>
/// <item>One column left at a shelf's end takes the nearest later item one wide, else stays empty.</item>
/// </list>
/// So the order is read along the shelves, left to right, and an item pulled forward stays in the lane it filled.
/// Measured against five other rules on a library of 704 items at 10, 20 and 40 columns: as tight as the best of
/// them on every folder. What it cannot cure is a folder of big items alone - weapons three tall with nothing one
/// tall among them - which keeps the holes any stash of those has.
/// </summary>
public static class VaultLayout
{
    /// <summary>How far ahead in the order a lane looks for what fits its room: the bend the order may take.</summary>
    public const int Look = 24;

    /// <summary>
    /// The fewest columns a sheet has: four - one of a folder's tiles, growing by fours from there. Not
    /// ten, a stash page's width - the one width that is no whole number of
    /// fours.
    /// </summary>
    public const int LeastColumns = 4;

    /// <summary>
    /// The widest an item's row of the tables is taken at: ten cells, a stash page's - a guard on a silly
    /// <c>invwidth</c>, which stood as "the fewest columns" while that was ten and "two under it" while it was
    /// twelve. One wider than the sheet is laid at a shelf's start, cut to it (<see cref="Lay"/>).
    /// </summary>
    public const int WidestItem = 10;

    /// <summary>The columns a sheet has before a page has said its room: twenty.</summary>
    public const int DefaultColumns = 20;

    /// <summary>A lane's width.</summary>
    private const int Lane = 2;

    /// <summary>
    /// How wide a folder's tile is at the head of its folder's sheet, in columns (the Shell's FolderTile), and so
    /// the step a sheet's width goes by: a row of tiles fills the sheet to its end.
    /// </summary>
    public const int Step = 4;

    /// <summary>
    /// The columns a sheet has in a width: as many cells as fit, in whole steps of <see cref="Step"/> - and never
    /// fewer than <see cref="LeastColumns"/> (<see cref="Fit"/>).
    /// </summary>
    public static int Columns(double width, double cell) => Fit(cell > 0 ? (int)Math.Floor(width / cell) : 0);

    /// <summary>
    /// The widest sheet of so many columns or fewer: a whole number of <see cref="Step"/>s, and
    /// <see cref="LeastColumns"/> at the least. Kept even - an odd last column would stand empty
    /// down a folder of items two wide - and in fours, as a folder's own folders are tiles
    /// four wide at its sheet's head: at 14, 18 or 22 columns two stood empty at the end of every row of them.
    /// </summary>
    public static int Fit(int columns) => Math.Max(LeastColumns, columns - columns % Step);

    /// <summary>Lays the items out in their order, <paramref name="columns"/> wide.</summary>
    /// <param name="beside">
    /// Room beside the folder's tiles, where their last row does not fill the sheet: from column <c>Left</c> to the
    /// end, <c>Rows</c> tall, over the sheet's first row - the first shelf, laid there before the rest: with a folder in
    /// the first row and space behind it, the items jumped to the next row. Its spots' rows are negative, from <c>-Rows</c>: the caller puts them in the tiles' last row.
    /// </param>
    public static VaultSheet Lay(IReadOnlyList<VaultFootprint> items, int columns, int look = Look, (int Left, int Rows)? beside = null)
    {
        columns = Math.Max(Lane, columns);
        var spots = new List<VaultSpot>(items.Count);
        var taken = new bool[items.Count];
        int first = 0, top = 0;

        // The room beside the tiles: one shelf as tall as they are, only of what fits it; nothing laid there where
        // the first item to open a lane is taller than it.
        if (beside is { Left: > 0, Rows: > 0 } band && band.Left < columns && items.Count > 0
            && TryShelf(items, taken, 0, columns - band.Left, band.Rows, look, out var laidBeside, out _))
        {
            foreach (var (index, x, y) in laidBeside)
            {
                taken[index] = true;
                spots.Add(new VaultSpot(items[index].Key, band.Left + x, y - band.Rows, items[index].Width, items[index].Height));
            }
        }

        while (true)
        {
            while (first < items.Count && taken[first])
                first++;
            if (first >= items.Count)
                break;

            // The shelf, laid again taller when an item that opens a lane does not fit it.
            int height = Math.Max(1, items[first].Height);
            List<(int Index, int X, int Y)> laid;
            while (!TryShelf(items, taken, first, columns, height, look, out laid, out int taller))
                height = taller;

            foreach (var (index, x, y) in laid)
            {
                taken[index] = true;
                spots.Add(new VaultSpot(items[index].Key, x, top + y, items[index].Width, items[index].Height));
            }

            top += height;
        }

        return new VaultSheet(spots, top);
    }

    /// <summary>
    /// One shelf of a height, from the first item not yet laid: what it holds, or false with the height it needs
    /// where an item opening a lane is taller.
    /// </summary>
    private static bool TryShelf(IReadOnlyList<VaultFootprint> items, bool[] laidBefore, int first, int columns, int height,
        int look, out List<(int Index, int X, int Y)> laid, out int taller)
    {
        laid = [];
        taller = height;
        var here = new HashSet<int>();
        bool Free(int index) => !laidBefore[index] && !here.Contains(index);

        int x = 0;
        while (x < columns)
        {
            int next = first;
            while (next < items.Count && !Free(next))
                next++;
            if (next >= items.Count)
                break;

            int head = next;
            int room = Math.Min(Math.Max(Lane, Math.Min(items[head].Width, columns)), columns - x);
            if (items[head].Width > room && x > 0)
            {
                // Too wide for what the shelf has left: the nearest later item that fits it, within the look.
                // (At the shelf's start an item wider than the whole sheet is laid all the same, cut to it.)
                head = -1;
                int seen = 0;
                for (int at = next + 1; at < items.Count && seen < look; at++)
                {
                    if (!Free(at))
                        continue;
                    seen++;
                    if (items[at].Width <= room && items[at].Height <= height)
                    {
                        head = at;
                        break;
                    }
                }

                if (head < 0)
                    break;
            }

            if (items[head].Height > height)
            {
                taller = items[head].Height;
                return false;
            }

            x += FillLane(items, Free, here, laid, next, head, x, room, height, look);
        }

        return true;
    }

    /// <summary>
    /// A lane from its head: the head top left, then the nearest later items that fit the room left, each in the
    /// first free spot reading the lane row by row. Returns the columns it used.
    /// </summary>
    private static int FillLane(IReadOnlyList<VaultFootprint> items, Func<int, bool> free, HashSet<int> here,
        List<(int Index, int X, int Y)> laid, int from, int head, int left, int room, int height, int look)
    {
        var cells = new bool[room, height];
        int used = 0;

        void Put(int index, int cx, int cy)
        {
            var item = items[index];
            for (int i = 0; i < item.Width && cx + i < room; i++)
            {
                for (int j = 0; j < item.Height && cy + j < height; j++)
                    cells[cx + i, cy + j] = true;
            }

            here.Add(index);
            laid.Add((index, left + cx, cy));
            used = Math.Max(used, Math.Min(room, cx + item.Width));
        }

        bool Fits(VaultFootprint item, int cx, int cy)
        {
            if (cx + item.Width > room || cy + item.Height > height)
                return false;

            for (int i = 0; i < item.Width; i++)
            {
                for (int j = 0; j < item.Height; j++)
                {
                    if (cells[cx + i, cy + j])
                        return false;
                }
            }

            return true;
        }

        Put(head, 0, 0);

        bool placed = true;
        while (placed)
        {
            placed = false;
            int seen = 0;
            for (int at = from; at < items.Count && seen < look && !placed; at++)
            {
                if (!free(at))
                    continue;
                seen++;

                for (int cy = 0; cy < height && !placed; cy++)
                {
                    for (int cx = 0; cx < room && !placed; cx++)
                    {
                        if (Fits(items[at], cx, cy))
                        {
                            Put(at, cx, cy);
                            placed = true;
                        }
                    }
                }
            }
        }

        return Math.Max(1, used);
    }
}
