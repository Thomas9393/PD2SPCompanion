using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// A stash page laid out again (Tidy and Sort for the personal
/// stash and a shared page, never the inventory): the vault's Sort and Tidy for a page of the game's, which
/// has a size. The items in an order, and the cell each takes - the first free room, reading the page across
/// and then down, as the vault lays a folder out (Vault.SortFolder); and a sheet's gaps closed, for a page
/// and for a folder alike (<see cref="CloseGaps"/>, Vault.TidyFolder).
/// <para>
/// A page is full where a folder is not: an order whose first fit runs out of room is no layout at all
/// (<see cref="LayOut"/> answers null), and the caller leaves the page as it lies.
/// </para>
/// </summary>
public static class PageArrange
{
    /// <summary>The orders a page can be sorted by - the vault's (Vault.Orders), less the day an item was stored.</summary>
    public static readonly string[] Orders = [N("Type"), N("Name"), N("Rarity")];

    /// <summary>
    /// The items in an order of <see cref="Orders"/>, as the vault sorts by it: Type by the Type order's rank,
    /// then the base's name, then the item's; Rarity by its rank, then the name; Name alone. Descending is the
    /// same list read backwards. An order not named here is Type.
    /// </summary>
    public static IReadOnlyList<Item> InOrder(IEnumerable<Item> items, string by, bool descending)
    {
        var named = items.Select(item => (Item: item, View: ItemPresenter.Describe(item))).ToList();
        var name = StringComparer.OrdinalIgnoreCase;

        var ordered = by switch
        {
            "Name" => named.OrderBy(each => each.View.Name, name),
            "Rarity" => named.OrderBy(each => ItemOrder.Rarity(each.Item)).ThenBy(each => each.View.Name, name),
            _ => named.OrderBy(each => ItemOrder.TypeRank(each.Item))
                      .ThenBy(each => each.View.BaseName, name)
                      .ThenBy(each => each.View.Name, name),
        };

        var list = ordered.Select(each => each.Item).ToList();
        if (descending)
            list.Reverse();

        return list;
    }

    /// <summary>A piece lying on a sheet of cells: its left column, its top row, and the room it takes.</summary>
    public readonly record struct Piece(int X, int Y, int Width, int Height);

    /// <summary>
    /// The gaps of a sheet closed:
    /// everything stays where it lies, and each hole, read across and then down, takes <b>the last piece on
    /// the sheet that fits it</b>. So a page laid out by hand is disturbed as little as can be - a move a hole,
    /// no more - and the free room gathers at the foot. It cannot fail: a piece only ever moves into room that
    /// is free, towards the top.
    /// <para>
    /// It was every piece lifted and laid back in the order it lay, each into the first free room - which kept
    /// the order, never moved a later small piece into an earlier small hole, and on a page packed by hand
    /// ran out of room and moved nothing at all ("No room in this order"). An order is Sort's to give.
    /// </para>
    /// <para>
    /// A hole is taken by the first free cell of it, the piece's top left corner there. The piece may lie
    /// partly over where it lay - one shifted up a row - so its own cells count as free for it. The sheet is
    /// read until a reading moves nothing: what a move frees lies after the hole it filled and is read in
    /// its turn, but a piece that was not the last of its size may be the last that fits once that one has
    /// gone elsewhere. Returns the pieces that moved, by their place in <paramref name="pieces"/>, and where to.
    /// </para>
    /// <para>
    /// The pieces are kept by size, each size's in the order they lie: the last that fits a hole is then the
    /// last of some size, and a folder of thousands is asked a size a cell and not a piece a cell.
    /// </para>
    /// </summary>
    public static IReadOnlyList<(int Index, int X, int Y)> CloseGaps(IReadOnlyList<Piece> pieces, int columns)
    {
        var moved = new Dictionary<int, (int X, int Y)>();
        if (pieces.Count == 0 || columns < 1)
            return [];

        int rows = pieces.Max(piece => piece.Y + piece.Height);
        var owner = new int[columns, rows];
        for (int x = 0; x < columns; x++)
        {
            for (int y = 0; y < rows; y++)
                owner[x, y] = -1;
        }

        var at = pieces.Select(piece => (piece.X, piece.Y)).ToArray();
        void Mark(int index, int value)
        {
            var piece = pieces[index];
            for (int x = at[index].X; x < at[index].X + piece.Width && x < columns; x++)
            {
                for (int y = at[index].Y; y < at[index].Y + piece.Height; y++)
                    owner[x, y] = value;
            }
        }

        // Each size's pieces in the order they lie, the last at the end.
        var lying = Comparer<(int Y, int X, int Index)>.Default;
        var bySize = new Dictionary<(int Width, int Height), SortedSet<(int Y, int X, int Index)>>();
        for (int index = 0; index < pieces.Count; index++)
        {
            Mark(index, index);
            var size = (pieces[index].Width, pieces[index].Height);
            if (!bySize.TryGetValue(size, out var same))
                bySize[size] = same = new SortedSet<(int, int, int)>(lying);
            same.Add((at[index].Y, at[index].X, index));
        }

        for (bool closing = true; closing;)
        {
            closing = false;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    if (owner[x, y] != -1)
                        continue;

                    // The last piece lying after this cell that fits with its corner here, of whatever size.
                    (int Y, int X, int Index)? best = null;
                    foreach (var (size, same) in bySize)
                    {
                        if (same.Count == 0 || x + size.Width > columns || y + size.Height > rows)
                            continue;

                        var last = same.Max;
                        if (lying.Compare(last, (y, x, int.MaxValue)) <= 0)
                            continue;

                        // What stands in the room, if anything: nothing, and the size's last piece fits; one
                        // piece of this very size lying after the cell, and that one does, moved over itself.
                        int inTheWay = -1;
                        bool fits = true;
                        for (int cx = x; fits && cx < x + size.Width; cx++)
                        {
                            for (int cy = y; fits && cy < y + size.Height; cy++)
                            {
                                int there = owner[cx, cy];
                                if (there == -1 || there == inTheWay)
                                    continue;

                                fits = inTheWay == -1 && (pieces[there].Width, pieces[there].Height) == size
                                       && lying.Compare((at[there].Y, at[there].X, there), (y, x, int.MaxValue)) > 0;
                                inTheWay = there;
                            }
                        }

                        if (!fits)
                            continue;

                        var fitting = inTheWay == -1 ? last : (at[inTheWay].Y, at[inTheWay].X, inTheWay);
                        if (best is not { } found || lying.Compare(fitting, found) > 0)
                            best = fitting;
                    }

                    if (best is not { } going)
                        continue;

                    var sizeOf = (pieces[going.Index].Width, pieces[going.Index].Height);
                    bySize[sizeOf].Remove(going);
                    Mark(going.Index, -1);
                    at[going.Index] = (x, y);
                    Mark(going.Index, going.Index);
                    bySize[sizeOf].Add((y, x, going.Index));
                    moved[going.Index] = (x, y);
                    closing = true;
                }
            }
        }

        return [.. moved.Where(each => (each.Value.X, each.Value.Y) != (pieces[each.Key].X, pieces[each.Key].Y))
                        .OrderBy(each => each.Key).Select(each => (each.Key, each.Value.X, each.Value.Y))];
    }

    /// <summary>
    /// The cell each of these takes, in turn, on a page of <paramref name="columns"/> by <paramref name="rows"/>:
    /// the first free room, reading across and then down. Null where one of them finds none - nothing is laid
    /// out by halves.
    /// </summary>
    public static IReadOnlyList<(int X, int Y)>? LayOut(IReadOnlyList<(int Width, int Height)> sizes, int columns, int rows)
    {
        var taken = new bool[columns, rows];
        var cells = new List<(int X, int Y)>(sizes.Count);

        foreach (var (width, height) in sizes)
        {
            if (FirstFree(taken, width, height, columns, rows) is not { } at)
                return null;

            for (int x = at.X; x < at.X + width; x++)
            {
                for (int y = at.Y; y < at.Y + height; y++)
                    taken[x, y] = true;
            }

            cells.Add(at);
        }

        return cells;
    }

    private static (int X, int Y)? FirstFree(bool[,] taken, int width, int height, int columns, int rows)
    {
        for (int y = 0; y + height <= rows; y++)
        {
            for (int x = 0; x + width <= columns; x++)
            {
                if (IsFree(taken, x, y, width, height))
                    return (x, y);
            }
        }

        return null;
    }

    private static bool IsFree(bool[,] taken, int left, int top, int width, int height)
    {
        for (int x = left; x < left + width; x++)
        {
            for (int y = top; y < top + height; y++)
            {
                if (taken[x, y])
                    return false;
            }
        }

        return true;
    }
}
