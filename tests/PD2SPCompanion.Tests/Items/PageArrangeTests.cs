using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// A stash page laid out again (PageArrange: Tidy and Sort for the personal stash and a
/// shared page): the cells a run of items takes, and the orders they are put in.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class PageArrangeTests(Game game)
{
    private const string Stamp = "103";

    [GameFact]
    public void Each_item_takes_the_first_free_room_across_and_then_down()
    {
        // Four across, four down: a helm, a ring, a body armor, a ring.
        var cells = PageArrange.LayOut([(2, 2), (1, 1), (2, 3), (1, 1)], columns: 4, rows: 4);

        // The armor does not fit beside the helm in the top row's rest, so it goes under the ring; the
        // second ring then takes the cell the armor left free above it.
        Assert.Equal([(0, 0), (2, 0), (2, 1), (3, 0)], cells);
    }

    [GameFact]
    public void A_run_that_does_not_fit_is_no_layout_at_all()
    {
        Assert.Null(PageArrange.LayOut([(2, 2), (2, 2), (1, 1)], columns: 2, rows: 4));
        Assert.Null(PageArrange.LayOut([(3, 1)], columns: 2, rows: 4));
        Assert.Equal([], PageArrange.LayOut([], columns: 2, rows: 4));
    }

    [GameFact]
    public void Nothing_laid_out_lies_over_anything_else_or_off_the_page()
    {
        var random = new Random(7);
        for (int round = 0; round < 200; round++)
        {
            var sizes = Enumerable.Range(0, random.Next(1, 30))
                                  .Select(_ => (Width: random.Next(1, 3), Height: random.Next(1, 5)))
                                  .ToList();
            if (PageArrange.LayOut(sizes, columns: 10, rows: 10) is not { } cells)
                continue;

            var taken = new HashSet<(int, int)>();
            for (int at = 0; at < sizes.Count; at++)
            {
                for (int x = cells[at].X; x < cells[at].X + sizes[at].Width; x++)
                {
                    for (int y = cells[at].Y; y < cells[at].Y + sizes[at].Height; y++)
                    {
                        Assert.InRange(x, 0, 9);
                        Assert.InRange(y, 0, 9);
                        Assert.True(taken.Add((x, y)), $"round {round}: cell {x},{y} taken twice");
                    }
                }
            }
        }
    }

    private static PageArrange.Piece P(int x, int y, int width = 1, int height = 1) => new(x, y, width, height);

    /// <summary>The pieces as they lie once the moves are made, and that none lies over another or off the sheet.</summary>
    private static List<PageArrange.Piece> After(IReadOnlyList<PageArrange.Piece> pieces, IReadOnlyList<(int Index, int X, int Y)> moves, int columns)
    {
        var lying = pieces.ToList();
        foreach (var (index, x, y) in moves)
            lying[index] = lying[index] with { X = x, Y = y };

        var taken = new HashSet<(int, int)>();
        foreach (var piece in lying)
        {
            Assert.True(piece.X >= 0 && piece.Y >= 0 && piece.X + piece.Width <= columns, $"off the sheet: {piece}");
            for (int x = piece.X; x < piece.X + piece.Width; x++)
            {
                for (int y = piece.Y; y < piece.Y + piece.Height; y++)
                    Assert.True(taken.Add((x, y)), $"two pieces on {x},{y}");
            }
        }

        return lying;
    }

    [GameFact]
    public void Close_gaps_fills_a_hole_with_the_last_piece_that_fits_and_leaves_the_rest_where_it_lies()
    {
        // A row of four with a hole at its second cell; a tall piece and two small ones under it.
        PageArrange.Piece[] pieces = [P(0, 0), P(2, 0), P(3, 0), P(0, 1, 2, 2), P(2, 1), P(3, 2)];

        var moves = PageArrange.CloseGaps(pieces, columns: 4);

        // The last small piece, from the foot, into the hole - and nothing else.
        Assert.Equal([(5, 1, 0)], moves);
        After(pieces, moves, 4);
    }

    [GameFact]
    public void A_hole_too_small_for_what_follows_it_waits_for_a_piece_its_size()
    {
        // The page that showed it: one free cell in the first row, a 2 by 2 after it - which does not fit - and
        // a ring far below, which does.
        PageArrange.Piece[] pieces = [P(0, 0), P(2, 0, 2, 2), P(0, 1), P(1, 1), P(0, 2, 4, 1), P(3, 3)];

        var moves = PageArrange.CloseGaps(pieces, columns: 4);

        Assert.Equal([(5, 1, 0)], moves);
    }

    [GameFact]
    public void A_piece_moves_up_over_part_of_where_it_lay()
    {
        // A tall piece with one free row over it: up a row, its own cells no bar to it.
        PageArrange.Piece[] pieces = [P(1, 0), P(0, 1, 1, 3)];

        var moves = PageArrange.CloseGaps(pieces, columns: 2);

        Assert.Equal([(1, 0, 0)], moves);
        After(pieces, moves, 2);
    }

    [GameFact]
    public void With_no_gap_before_a_piece_that_fits_it_nothing_moves()
    {
        Assert.Empty(PageArrange.CloseGaps([P(0, 0), P(1, 0), P(0, 1, 2, 2)], columns: 2));

        // A hole at the very end is no gap: nothing lies after it.
        Assert.Empty(PageArrange.CloseGaps([P(0, 0), P(1, 0), P(0, 1)], columns: 2));

        // And a hole nothing after it fits stays a hole.
        Assert.Empty(PageArrange.CloseGaps([P(0, 0), P(0, 1, 2, 2)], columns: 2));
        Assert.Empty(PageArrange.CloseGaps([], columns: 10));
    }

    [GameFact]
    public void Any_sheet_is_left_without_a_piece_over_another_none_moved_down_and_nothing_more_to_close()
    {
        var random = new Random(20261009);
        (int, int)[] sizes = [(1, 1), (1, 1), (1, 2), (2, 2), (1, 3), (2, 3), (2, 4), (2, 1)];

        for (int round = 0; round < 200; round++)
        {
            int columns = random.Next(4, 21);
            var pieces = new List<PageArrange.Piece>();
            var taken = new HashSet<(int, int)>();
            for (int tries = 0; tries < 120; tries++)
            {
                var (width, height) = sizes[random.Next(sizes.Length)];
                int x = random.Next(0, columns - width + 1), y = random.Next(0, 30);
                var cells = Enumerable.Range(x, width).SelectMany(cx => Enumerable.Range(y, height).Select(cy => (cx, cy))).ToList();
                if (cells.Any(taken.Contains))
                    continue;

                taken.UnionWith(cells);
                pieces.Add(P(x, y, width, height));
            }

            var moves = PageArrange.CloseGaps(pieces, columns);
            var lying = After(pieces, moves, columns);

            // Towards the top only: a piece never lies later than it lay.
            foreach (var (index, x, y) in moves)
                Assert.True((y, x).CompareTo((pieces[index].Y, pieces[index].X)) < 0, $"round {round}: piece {index} moved down");

            // And done once is done: the same sheet has nothing more to close.
            Assert.Empty(PageArrange.CloseGaps(lying, columns));
        }
    }

    [GameFact]
    public void The_orders_are_the_vault_s()
    {
        if (!game.Available)
            return;

        var crest = ItemForge.Unique("Harlequin Crest", Stamp).Item;
        var ring = ItemForge.Unique("Nagelring", Stamp).Item;
        var bow = ItemForge.Unique("Windforce", Stamp).Item;
        var stone = ItemForge.Unique("The Stone of Jordan", Stamp).Item;
        Item[] items = [stone, bow, crest, ring];

        string[] Names(IEnumerable<Item> some) => [.. some.Select(item => ItemPresenter.Describe(item).Name)];

        Assert.Equal(["Harlequin Crest", "Nagelring", "The Stone of Jordan", "Windforce"],
                     Names(PageArrange.InOrder(items, "Name", descending: false)));
        Assert.Equal(["Windforce", "The Stone of Jordan", "Nagelring", "Harlequin Crest"],
                     Names(PageArrange.InOrder(items, "Name", descending: true)));

        // By Type: never a later group before an earlier one, and the two rings together, by name.
        var typed = PageArrange.InOrder(items, "Type", descending: false);
        var ranks = typed.Select(ItemOrder.TypeRank).ToList();
        Assert.Equal(ranks.Order(), ranks);
        int first = typed.ToList().IndexOf(ring);
        Assert.Same(stone, typed[first + 1]);

        // All four are uniques: by Rarity they fall back on the name.
        Assert.Equal(Names(PageArrange.InOrder(items, "Name", descending: false)),
                     Names(PageArrange.InOrder(items, "Rarity", descending: false)));
    }
}
