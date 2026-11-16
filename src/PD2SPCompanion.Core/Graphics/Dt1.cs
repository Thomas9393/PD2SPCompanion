namespace PD2SPCompanion.Core.Graphics;

/// <summary>
/// One tile of a DT1 file: where it goes in a map cell (<see cref="Orientation"/>, <see cref="MainIndex"/>,
/// <see cref="SubIndex"/> - what a DS1 cell names), and its picture as palette indices, 0 clear.
/// <para>
/// The picture is decoded when it is first asked for, not when the file is read: a town's tile set is some
/// thousands of tiles and a card's backdrop draws a few hundred of them - decoding them all allocated 35 to
/// 80 MB a town (measured), which the editor then held as garbage.
/// </para>
/// </summary>
/// <param name="Orientation">0 a floor, 1-14 a wall standing on the cell, 15 a roof, above 15 a wall hanging below it; 10 and 11 are markers the game never draws.</param>
/// <param name="Rarity">How often the game picks it among tiles of one place; 0 is never picked while another has more.</param>
/// <param name="Left">Where the picture's first column sits, from the cell's left corner.</param>
/// <param name="Top">Where its first row sits, from the cell's top corner: 0 for a floor, negative for a wall standing above it.</param>
public sealed class Dt1Tile(
    int orientation, int mainIndex, int subIndex, int rarity, int roofHeight,
    int left, int top, int width, int height, Func<byte[]> decode)
{
    public int Orientation => orientation;
    public int MainIndex => mainIndex;
    public int SubIndex => subIndex;
    public int Rarity => rarity;
    public int RoofHeight => roofHeight;
    public int Left => left;
    public int Top => top;
    public int Width => width;
    public int Height => height;

    private byte[]? _indices;

    /// <summary>The picture, <see cref="Width"/> by <see cref="Height"/>: decoded once, the first time it is asked for.</summary>
    public byte[] Indices => _indices ??= decode();
}

/// <summary>
/// The game's map tiles: a DT1 file holds the floors, walls and roofs of one tile set, each a few blocks of
/// 32 pixels drawn onto an isometric cell 160 wide and 80 tall. Read for the town a character card stands in
/// (TownScene); the editor ships no map art, as it ships no item art.
/// <para>
/// The layout is the one the community's map editors document (Paul Siramy's DS1 editor): a 276-byte file
/// header, a 96-byte header per tile, and per tile its blocks - 20 bytes of header each, then the pixels. A
/// floor's block is a bare diamond, 32 by 15 with no bytes spent on its clear corners; every other block is
/// run-length rows: a count to skip, a count to draw, the bytes, and 0,0 to end the row.
/// </para>
/// </summary>
public static class Dt1
{
    private const int FileHeader = 276, TileHeader = 96, BlockHeader = 20;

    /// <summary>A cell on screen: 160 wide, 80 tall.</summary>
    public const int CellWidth = 160, CellHeight = 80;

    /// <summary>Every tile of a file. A file that is not a DT1 (its two version numbers are 7 and 6) gives none.</summary>
    public static IReadOnlyList<Dt1Tile> Read(byte[] data)
    {
        var tiles = new List<Dt1Tile>();
        if (data.Length < FileHeader || BitConverter.ToInt32(data, 0) != 7 || BitConverter.ToInt32(data, 4) != 6)
            return tiles;

        int count = BitConverter.ToInt32(data, 268);
        int first = BitConverter.ToInt32(data, 272);

        for (int n = 0; n < count; n++)
        {
            int at = first + n * TileHeader;
            if (at + TileHeader > data.Length)
                break;

            int roof = BitConverter.ToInt16(data, at + 4);
            int orientation = BitConverter.ToInt32(data, at + 20);
            int main = BitConverter.ToInt32(data, at + 24);
            int sub = BitConverter.ToInt32(data, at + 28);
            int rarity = BitConverter.ToInt32(data, at + 32);
            int blocksAt = BitConverter.ToInt32(data, at + 72);
            int blocks = BitConverter.ToInt32(data, at + 80);

            // The blocks' own box: a tile's stated size is not to be trusted (a floor says 80 or 128, a wall
            // 32 more than it draws), so the picture is as large as what is drawn.
            var placed = new List<(int X, int Y, int Format, int Length, int Offset)>();
            for (int b = 0; b < blocks; b++)
            {
                int header = blocksAt + b * BlockHeader;
                if (header + BlockHeader > data.Length)
                    break;
                placed.Add((BitConverter.ToInt16(data, header), BitConverter.ToInt16(data, header + 2),
                            BitConverter.ToInt16(data, header + 8), BitConverter.ToInt32(data, header + 10),
                            blocksAt + BitConverter.ToInt32(data, header + 16)));
            }
            if (placed.Count == 0)
            {
                tiles.Add(new Dt1Tile(orientation, main, sub, rarity, roof, 0, 0, 0, 0, () => []));
                continue;
            }

            int left = placed.Min(block => block.X), top = placed.Min(block => block.Y);
            int width = placed.Max(block => block.X) + 32 - left, height = placed.Max(block => block.Y) + 32 - top;

            // The pixels wait until the tile is drawn; the file's bytes are kept by the tiles that may be.
            byte[] Decode()
            {
                var indices = new byte[width * height];
                foreach (var block in placed)
                {
                    if (block.Offset < 0 || block.Offset + block.Length > data.Length)
                        continue;
                    if (block.Format == 1)
                        Diamond(data, block.Offset, block.Length, indices, width, block.X - left, block.Y - top);
                    else
                        Runs(data, block.Offset, block.Length, indices, width, height, block.X - left, block.Y - top);
                }

                return indices;
            }

            tiles.Add(new Dt1Tile(orientation, main, sub, rarity, roof, left, top, width, height, Decode));
        }

        return tiles;
    }

    /// <summary>How far in each of a floor block's fifteen rows starts, and how many pixels it holds: a diamond.</summary>
    private static readonly int[] RowStart = [14, 12, 10, 8, 6, 4, 2, 0, 2, 4, 6, 8, 10, 12, 14],
                                  RowLength = [4, 8, 12, 16, 20, 24, 28, 32, 28, 24, 20, 16, 12, 8, 4];

    /// <summary>A floor block: 256 bytes, the diamond's rows one after another with nothing between.</summary>
    private static void Diamond(byte[] data, int from, int length, byte[] into, int stride, int x, int y)
    {
        if (length != 256)
            return;

        for (int row = 0; row < 15; row++)
        {
            Array.Copy(data, from, into, (y + row) * stride + x + RowStart[row], RowLength[row]);
            from += RowLength[row];
        }
    }

    /// <summary>Any other block: rows of (skip, draw, bytes) runs, each row ended by 0,0.</summary>
    private static void Runs(byte[] data, int from, int length, byte[] into, int stride, int height, int x0, int y0)
    {
        int end = from + length, x = 0, y = 0;
        while (from + 1 < end)
        {
            int skip = data[from++], draw = data[from++];
            if (skip == 0 && draw == 0)
            {
                x = 0;
                y++;
                continue;
            }

            x += skip;
            if (from + draw > end || y0 + y >= height || x0 + x + draw > stride)
            {
                from += draw;
                x += draw;
                continue;
            }

            Array.Copy(data, from, into, (y0 + y) * stride + x0 + x, draw);
            from += draw;
            x += draw;
        }
    }
}
