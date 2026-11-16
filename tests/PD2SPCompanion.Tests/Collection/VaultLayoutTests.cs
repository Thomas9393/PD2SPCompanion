using PD2SPCompanion.Core.Collection;

namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// A vault folder laid out by a rule, where every item had a place of its own:
/// shelves as tall as their tallest lane head, lanes two wide filled by the
/// nearest later items, the columns kept even. The rule was chosen on pictures of six candidates over a library of
/// 704 items; these hold it to what was chosen.
/// </summary>
public sealed class VaultLayoutTests
{
    private static VaultFootprint[] Items(params (int W, int H)[] sizes) =>
        [.. sizes.Select((size, at) => new VaultFootprint(at, size.W, size.H))];

    private static VaultSpot Spot(VaultSheet sheet, long key) => sheet.Spots.Single(spot => spot.Key == key);

    /// <summary>Every item once, inside the sheet, over no other.</summary>
    private static void Sound(VaultSheet sheet, IReadOnlyList<VaultFootprint> items, int columns)
    {
        Assert.Equal(items.Select(item => item.Key).Order(), sheet.Spots.Select(spot => spot.Key).Order());
        var cells = new HashSet<(int, int)>();
        foreach (var spot in sheet.Spots)
        {
            Assert.True(spot.X >= 0 && spot.X + spot.Width <= columns, $"item {spot.Key} at column {spot.X} is off the sheet");
            Assert.True(spot.Y >= 0 && spot.Y + spot.Height <= sheet.Rows, $"item {spot.Key} at row {spot.Y} is below the sheet");
            for (int i = 0; i < spot.Width; i++)
            {
                for (int j = 0; j < spot.Height; j++)
                    Assert.True(cells.Add((spot.X + i, spot.Y + j)), $"item {spot.Key} lies over another at {spot.X + i},{spot.Y + j}");
            }
        }
    }

    [Theory]
    [InlineData(560, 20)]   // twenty cells of 28
    [InlineData(587, 20)]   // twenty and most of another: still twenty
    [InlineData(616, 20)]   // twenty-two would fit: in fours, a folder's tile wide
    [InlineData(671, 20)]   // and twenty-three
    [InlineData(672, 24)]
    [InlineData(448, 16)]
    [InlineData(447, 12)]   // fifteen fit: twelve
    [InlineData(335, 8)]    // eleven fit: eight
    [InlineData(223, 4)]    // seven fit: four
    [InlineData(100, 4)]    // three fit: four all the same, the fewest a sheet has - one of a folder's tiles
    public void The_columns_are_as_many_as_fit_in_fours_and_never_fewer_than_four(double width, int columns) =>
        Assert.Equal(columns, VaultLayout.Columns(width, 28));

    [Theory]
    [InlineData(3, 4)]
    [InlineData(7, 4)]
    [InlineData(11, 8)]
    [InlineData(12, 12)]
    [InlineData(15, 12)]
    [InlineData(31, 28)]
    public void A_width_asked_for_is_the_widest_sheet_no_wider(int asked, int columns) =>
        Assert.Equal(columns, VaultLayout.Fit(asked));

    [Fact]
    public void A_folder_of_one_shape_fills_its_shelves_with_no_row_lost()
    {
        // Twenty-five body armours, 2 x 3, ten columns: five a shelf, the shelves three tall.
        var items = Items([.. Enumerable.Repeat((2, 3), 25)]);
        var sheet = VaultLayout.Lay(items, 10);

        Sound(sheet, items, 10);
        Assert.Equal(15, sheet.Rows);
        Assert.Equal(new VaultSpot(0, 0, 0, 2, 3), Spot(sheet, 0));
        Assert.Equal(new VaultSpot(5, 0, 3, 2, 3), Spot(sheet, 5));
    }

    [Fact]
    public void The_room_under_an_item_takes_the_nearest_later_item_that_fits()
    {
        // A polearm opens the shelf four tall; the armour beside it leaves a row, which the belt further on takes.
        var items = Items((2, 4), (2, 3), (2, 2), (2, 1));
        var sheet = VaultLayout.Lay(items, 10);

        Sound(sheet, items, 10);
        Assert.Equal(4, sheet.Rows);
        Assert.Equal((2, 0), (Spot(sheet, 1).X, Spot(sheet, 1).Y));
        Assert.Equal((2, 3), (Spot(sheet, 3).X, Spot(sheet, 3).Y));    // the belt, under the armour
        Assert.Equal((4, 0), (Spot(sheet, 2).X, Spot(sheet, 2).Y));    // the helm opens the next lane
    }

    [Fact]
    public void A_one_wide_item_takes_later_ones_beside_and_under_it()
    {
        // A wand, then a ring and an armour: the lane is two wide, the ring beside the wand, nothing fits under.
        var items = Items((1, 2), (1, 1), (1, 1), (2, 3));
        var sheet = VaultLayout.Lay(items, 10);

        Sound(sheet, items, 10);
        Assert.Equal((0, 0), (Spot(sheet, 0).X, Spot(sheet, 0).Y));
        Assert.Equal((1, 0), (Spot(sheet, 1).X, Spot(sheet, 1).Y));
        Assert.Equal((1, 1), (Spot(sheet, 2).X, Spot(sheet, 2).Y));
        Assert.Equal(2, Spot(sheet, 3).X);
    }

    [Fact]
    public void A_lane_whose_second_column_stayed_empty_is_one_wide()
    {
        // A dagger with nothing one wide after it: the armour opens its lane right beside it, no column lost.
        var items = Items((1, 3), (2, 3), (2, 3));
        var sheet = VaultLayout.Lay(items, 10);

        Sound(sheet, items, 10);
        Assert.Equal(1, Spot(sheet, 1).X);
        Assert.Equal(3, Spot(sheet, 2).X);
    }

    [Fact]
    public void One_column_left_at_a_shelfs_end_takes_the_nearest_one_wide_item()
    {
        // A dagger and four armours fill nine of ten columns; the fifth armour cannot follow. The wand is too far on
        // for the dagger's lane to have seen it (25 armours between), and near enough for the shelf's last column.
        (int, int)[] sizes = [(1, 3), .. Enumerable.Repeat((2, 3), 25), (1, 2)];
        var items = Items(sizes);
        var sheet = VaultLayout.Lay(items, 10);

        Sound(sheet, items, 10);
        Assert.Equal(1, Spot(sheet, 1).X);
        Assert.Equal((9, 0), (Spot(sheet, 26).X, Spot(sheet, 26).Y));
        Assert.Equal((0, 3), (Spot(sheet, 5).X, Spot(sheet, 5).Y));
    }

    [Fact]
    public void A_shelf_is_as_tall_as_the_tallest_item_that_opens_a_lane_in_it()
    {
        // Helms two tall, then a spear: the shelf the spear opens a lane in is four tall - laid again from its
        // start, so the second helm is under the first - and the shelf after is two again.
        var items = Items((2, 2), (2, 2), (2, 4), (2, 2), (2, 2), (2, 2), (2, 2), (2, 2));
        var sheet = VaultLayout.Lay(items, 6);

        Sound(sheet, items, 6);
        Assert.Equal((0, 2), (Spot(sheet, 1).X, Spot(sheet, 1).Y));
        Assert.Equal((2, 0), (Spot(sheet, 2).X, Spot(sheet, 2).Y));
        Assert.Equal((4, 0), (Spot(sheet, 3).X, Spot(sheet, 3).Y));
        Assert.Equal((4, 2), (Spot(sheet, 4).X, Spot(sheet, 4).Y));
        Assert.Equal((0, 4), (Spot(sheet, 5).X, Spot(sheet, 5).Y));
        Assert.Equal(6, sheet.Rows);
    }

    [Fact]
    public void A_lane_looks_no_further_ahead_than_the_look()
    {
        // A ring that would fit under the first armour, thirty items on: out of a look of 24, within one of 40.
        (int, int)[] sizes = [(2, 4), (2, 3), .. Enumerable.Repeat((2, 4), 30), (2, 1)];
        var items = Items(sizes);

        var near = VaultLayout.Lay(items, 10, look: 24);
        var far = VaultLayout.Lay(items, 10, look: 40);
        Sound(near, items, 10);
        Sound(far, items, 10);
        Assert.NotEqual((2, 3), (Spot(near, 32).X, Spot(near, 32).Y));
        Assert.Equal((2, 3), (Spot(far, 32).X, Spot(far, 32).Y));
    }

    /// <summary>
    /// The rule as it was measured, to the cell: three hundred items of made-up sizes laid as the script that drew the
    /// previews laid them - the rows, and a sum over every item's spot.
    /// </summary>
    [Theory]
    [InlineData(10, 114, 8989486)]
    [InlineData(20, 60, 14382822)]
    [InlineData(40, 32, 27820670)]
    public void Three_hundred_items_lie_where_the_measured_rule_laid_them(int columns, int rows, long sum)
    {
        var items = new List<VaultFootprint>();
        long x = 12345;
        for (int i = 0; i < 300; i++)
        {
            x = (x * 1103515245 + 12345) & 0x7fffffff;
            items.Add(new VaultFootprint(i, 1 + (int)((x >> 8) % 2), 1 + (int)((x >> 12) % 4)));
        }

        var sheet = VaultLayout.Lay(items, columns);

        Sound(sheet, items, columns);
        Assert.Equal(rows, sheet.Rows);
        long check = 0;
        foreach (var spot in sheet.Spots)
            check += (spot.Key + 1) * (spot.X * 31 + spot.Y);
        Assert.Equal(sum, check % 1000000007);
    }

    [Fact]
    public void The_room_beside_the_tiles_last_row_is_laid_first()
    {
        // A folder's tiles fill part of their last row; the items begin beside them, not under.
        // Twelve columns, one tile (four wide): eight columns by four rows beside it, then the full width under.
        var items = Items((1, 3), (1, 1), (2, 4), (1, 1), (2, 2), (1, 1));
        var sheet = VaultLayout.Lay(items, 12, beside: (4, 4));

        Assert.All(sheet.Spots, spot => Assert.True(spot.Y < 0 && spot.Y >= -4 && spot.X >= 4,
            $"item {spot.Key} at {spot.X},{spot.Y} is not beside the tiles"));
        Assert.Equal(0, sheet.Rows);

        // Beside and under together: no two overlap, and none is in the tiles' columns while in their rows.
        var many = Items([.. Enumerable.Repeat((1, 3), 30)]);
        var laid = VaultLayout.Lay(many, 12, beside: (4, 4));
        Assert.Equal(30, laid.Spots.Count);
        var cells = new HashSet<(int, int)>();
        foreach (var spot in laid.Spots)
        {
            if (spot.Y < 0)
                Assert.True(spot.X >= 4 && spot.Y + spot.Height <= 0, $"item {spot.Key} beside the tiles spills out of their row");
            for (int i = 0; i < spot.Width; i++)
                for (int j = 0; j < spot.Height; j++)
                    Assert.True(cells.Add((spot.X + i, spot.Y + j)), $"item {spot.Key} lies over another");
        }

        // No room beside (the row full, or no tiles): laid as ever.
        Assert.Equal(VaultLayout.Lay(many, 12).Spots, VaultLayout.Lay(many, 12, beside: null).Spots);
    }

    [Fact]
    public void Nothing_and_the_odd_shape_are_laid_without_complaint()
    {
        Assert.Equal(0, VaultLayout.Lay([], 20).Rows);

        // Wider than the sheet, and of no height: each gets a spot, the sheet goes on.
        var items = Items((30, 2), (1, 0), (2, 2));
        var sheet = VaultLayout.Lay(items, 10);
        Assert.Equal(3, sheet.Spots.Count);
    }
}
