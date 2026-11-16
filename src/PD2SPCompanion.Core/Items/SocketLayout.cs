using System;
using System.Collections.Generic;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// Where the game draws each socket over an item. The game keeps this in D2Client, which no table and no reimplementation
/// carries, so it is written out from in-game screenshots: runes El, Eld, Tir, Nef, Eth, Ith put in
/// in that order, so each rune names its hole. A socketed item's x in the save is its hole number,
/// in the order the gems went in (read from a stash), and that number is
/// what picks the place - not an order a reader would guess: four holes on a 2x3 go top left,
/// bottom right, bottom left, top right.
/// <para>
/// The rule the screenshots show: one column down the middle while the item is as tall as the
/// holes are many, else two columns - with a fifth hole in the centre, as a die's five. Two holes
/// on a 2x4 sit apart, on rows 1 and 3; everywhere else a column is packed and centred, the
/// 1x4's two included.
/// </para>
/// <para>
/// Every shape that takes a socket has been seen in game:
/// 1x1 (The Third Eye), 2x1 (Troll Belt [S]), 1x2 with 1-2, 1x3 with 1-3 (a sword and a quiver),
/// 1x4 with 1-4, 2x2 with 1-4, 2x3 with 1-6, 2x4 with 1-6. A 1x2 orb never takes a third,
/// whatever itemtypes.txt allows. The rule below stays for anything a season adds.
/// </para>
/// </summary>
public static class SocketLayout
{
    /// <summary>A hole's centre, in cells from the item's top left corner.</summary>
    public readonly record struct Spot(double X, double Y);

    /// <summary>
    /// Whether the game has been seen to draw this many holes on an item this shape. The unit tests
    /// hold the table to every shape measured in game (ItemRulesTests).
    /// </summary>
    public static bool IsSeen(int width, int height, int count) =>
        Seen.ContainsKey((width, height, count));

    /// <summary>
    /// The centre of each hole, first hole first, for an item <paramref name="width"/> by
    /// <paramref name="height"/> cells with <paramref name="count"/> of them.
    /// </summary>
    public static IReadOnlyList<Spot> Of(int width, int height, int count)
    {
        if (count <= 0)
            return [];

        if (Seen.TryGetValue((width, height, count), out var seen))
            return seen;

        return Ruled(Math.Max(1, width), Math.Max(1, height), count);
    }

    private static Spot S(double x, double y) => new(x, y);

    /// <summary>What the screenshots show.</summary>
    private static readonly Dictionary<(int W, int H, int N), Spot[]> Seen = new()
    {
        // One cell and one socket: The Third Eye (1x1), a Troll Belt [S] (2x1, between its cells).
        [(1, 1, 1)] = [S(0.5, 0.5)],
        [(2, 1, 1)] = [S(1, 0.5)],

        // 1-wide: an orb (Vortex Orb), a sword and a quiver (War Sword, Sharp Arrows), a
        // two-handed sword (Champion Sword) - two on the 1x4 packed, not apart as on the 2x4.
        [(1, 2, 1)] = [S(0.5, 1)],
        [(1, 2, 2)] = [S(0.5, 0.5), S(0.5, 1.5)],
        [(1, 3, 1)] = [S(0.5, 1.5)],
        [(1, 3, 2)] = [S(0.5, 1), S(0.5, 2)],
        [(1, 3, 3)] = [S(0.5, 0.5), S(0.5, 1.5), S(0.5, 2.5)],
        [(1, 4, 1)] = [S(0.5, 2)],
        [(1, 4, 2)] = [S(0.5, 1.5), S(0.5, 2.5)],
        [(1, 4, 3)] = [S(0.5, 1), S(0.5, 2), S(0.5, 3)],
        [(1, 4, 4)] = [S(0.5, 0.5), S(0.5, 1.5), S(0.5, 2.5), S(0.5, 3.5)],

        // 2x2: a helm (Hwanin's Splendor).
        [(2, 2, 1)] = [S(1, 1)],
        [(2, 2, 2)] = [S(1, 0.5), S(1, 1.5)],
        [(2, 2, 3)] = [S(0.5, 0.5), S(1.5, 0.5), S(1, 1.5)],
        [(2, 2, 4)] = [S(0.5, 0.5), S(1.5, 1.5), S(0.5, 1.5), S(1.5, 0.5)],     // Sacred Rondache

        // 2x3: a body armour and an axe alike (Duriel's Shell, Death Cleaver).
        [(2, 3, 1)] = [S(1, 1.5)],
        [(2, 3, 2)] = [S(1, 1), S(1, 2)],
        [(2, 3, 3)] = [S(1, 0.5), S(1, 1.5), S(1, 2.5)],
        [(2, 3, 4)] = [S(0.5, 1), S(1.5, 2), S(0.5, 2), S(1.5, 1)],
        [(2, 3, 5)] = [S(1, 1.5), S(0.5, 0.5), S(1.5, 2.5), S(0.5, 2.5), S(1.5, 0.5)],
        [(2, 3, 6)] = [S(0.5, 0.5), S(0.5, 1.5), S(0.5, 2.5), S(1.5, 0.5), S(1.5, 1.5), S(1.5, 2.5)],

        // 2x4: a staff (Archon Staff).
        [(2, 4, 1)] = [S(1, 2)],
        [(2, 4, 2)] = [S(1, 1), S(1, 3)],
        [(2, 4, 3)] = [S(1, 1), S(1, 2), S(1, 3)],
        [(2, 4, 4)] = [S(1, 0.5), S(1, 1.5), S(1, 2.5), S(1, 3.5)],
        [(2, 4, 5)] = [S(1, 2), S(0.5, 1), S(1.5, 3), S(0.5, 3), S(1.5, 1)],
        [(2, 4, 6)] = [S(0.5, 1), S(0.5, 2), S(0.5, 3), S(1.5, 1), S(1.5, 2), S(1.5, 3)],
    };

    /// <summary>
    /// Any shape a later season adds, by the rule the seen ones follow - the 2-wide pattern for the same
    /// height and count moved to the item's own middle, a 1-wide item's column down its centre.
    /// </summary>
    private static Spot[] Ruled(int width, int height, int count)
    {
        double middle = width / 2.0;

        // One column, while the item is tall enough: packed and centred, as seen on every shape -
        // bar two on a 2x4, which sit apart (and are in the table).
        if (count <= height)
        {
            double top = (height - count) / 2.0 + 0.5;
            var packed = new Spot[count];
            for (int i = 0; i < count; i++)
                packed[i] = S(middle, top + i);
            return packed;
        }

        // A 1-wide item with more holes than cells (a 1x2 orb with three): spread evenly down it,
        // overlapping, since there is no second column to go to.
        if (width == 1)
        {
            var spread = new Spot[count];
            for (int i = 0; i < count; i++)
                spread[i] = S(middle, height * (2 * i + 1) / (2.0 * count));
            return spread;
        }

        // Two columns. Four as on a 2x3 (top left, bottom right, bottom left, top right), on the
        // two rows around the middle - a 2x2 is then its four cells.
        double mid = height / 2.0;
        if (count == 4)
            return [S(0.5, mid - 0.5), S(1.5, mid + 0.5), S(0.5, mid + 0.5), S(1.5, mid - 0.5)];

        // Anything else: left column then right, packed and centred, as six are.
        int rows = (count + 1) / 2;
        double first = (height - rows) / 2.0 + 0.5;
        var two = new Spot[count];
        for (int i = 0; i < count; i++)
            two[i] = S(i < rows ? 0.5 : 1.5, first + (i < rows ? i : i - rows));
        return two;
    }
}
