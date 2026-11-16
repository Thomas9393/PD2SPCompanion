namespace PD2SPCompanion.Core.Graphics;

/// <summary>One cell of a DS1 layer: which tile it asks for. <see cref="IsEmpty"/> where it asks for none.</summary>
public readonly record struct Ds1Cell(byte Prop1, byte Prop2, byte Prop3, byte Prop4, int Orientation)
{
    /// <summary>Nothing here: the cell's first byte is its draw order, and 0 is "no tile".</summary>
    public bool IsEmpty => Prop1 == 0;

    /// <summary>Marked not to be drawn: the game keeps such a tile for what it blocks, not for its picture.</summary>
    public bool IsHidden => (Prop4 & 0x80) != 0;

    public int MainIndex => (Prop3 >> 4) | ((Prop4 & 0x03) << 4);

    public int SubIndex => Prop2;
}

/// <summary>A thing placed on a map: <see cref="Type"/> 1 a monster or townsperson, 2 an object; <see cref="X"/>, <see cref="Y"/> in fifths of a cell.</summary>
public sealed record Ds1Object(int Type, int Id, int X, int Y);

/// <summary>
/// A map as the game stores a hand-made area (a town, a preset): a grid of cells, each naming a floor tile
/// and up to four wall tiles by orientation and index, and the objects standing on it. Read for the town a
/// character card stands in (TownScene). The layout is the one Paul Siramy's DS1 editor documents; the
/// versions the game's own towns use are 3 to 18.
/// </summary>
public sealed class Ds1
{
    public int Version { get; private init; }
    public int Width { get; private init; }
    public int Height { get; private init; }

    /// <summary>The wall layers, each with its orientations read from the layer that follows it in the file.</summary>
    public IReadOnlyList<Ds1Cell[]> Walls { get; private init; } = [];

    public IReadOnlyList<Ds1Cell[]> Floors { get; private init; } = [];

    public IReadOnlyList<Ds1Object> Objects { get; private init; } = [];

    /// <summary>Orientations as files older than version 7 number them, to the numbering a DT1 uses.</summary>
    private static readonly byte[] OldOrientation =
    [
        0x00, 0x01, 0x02, 0x01, 0x02, 0x03, 0x03, 0x05, 0x05, 0x06, 0x06, 0x07, 0x07, 0x08, 0x08, 0x09, 0x09, 0x0A,
        0x0A, 0x0B, 0x0B, 0x0C, 0x0C, 0x0D, 0x0D, 0x0E, 0x0E, 0x0F, 0x0F, 0x10, 0x10, 0x11, 0x11, 0x12, 0x12, 0x14,
    ];

    public static Ds1? Read(byte[] data)
    {
        try
        {
            int at = 0;
            int Next()
            {
                int value = BitConverter.ToInt32(data, at);
                at += 4;
                return value;
            }

            int version = Next();
            int width = Next() + 1, height = Next() + 1;
            if (version >= 8)
                Next();   // the act
            int tagType = version >= 10 ? Next() : 0;

            if (version >= 3)
            {
                int files = Next();
                for (int n = 0; n < files; n++)
                {
                    while (data[at] != 0)
                        at++;
                    at++;
                }
            }

            if (version is >= 9 and <= 13)
                at += 8;

            int wallCount = 1, floorCount = 1;
            if (version >= 4)
            {
                wallCount = Next();
                if (version >= 16)
                    floorCount = Next();
            }

            int cells = width * height;
            Ds1Cell[] Layer(byte[]? orientations = null)
            {
                var layer = new Ds1Cell[cells];
                for (int n = 0; n < cells; n++, at += 4)
                    layer[n] = new Ds1Cell(data[at], data[at + 1], data[at + 2], data[at + 3], orientations?[n] ?? 0);
                return layer;
            }

            byte[] Orientations()
            {
                var found = new byte[cells];
                for (int n = 0; n < cells; n++, at += 4)
                {
                    byte stored = data[at];
                    found[n] = version < 7 ? (stored < OldOrientation.Length ? OldOrientation[stored] : (byte)0) : stored;
                }
                return found;
            }

            var walls = new List<Ds1Cell[]>();
            var floors = new List<Ds1Cell[]>();

            if (version < 4)
            {
                // One wall, one floor, the wall's orientations, a tag layer and the shadows.
                int wallAt = at;
                at += cells * 4;
                floors.Add(Layer());
                int after = at;
                var orientations = Orientations();
                int rest = at;
                at = wallAt;
                walls.Add(Layer(orientations));
                at = rest + cells * 8;
                _ = after;
            }
            else
            {
                for (int n = 0; n < wallCount; n++)
                {
                    int wallAt = at;
                    at += cells * 4;
                    var orientations = Orientations();
                    int rest = at;
                    at = wallAt;
                    walls.Add(Layer(orientations));
                    at = rest;
                }

                for (int n = 0; n < floorCount; n++)
                    floors.Add(Layer());

                at += cells * 4;   // the shadows
                if (tagType is 1 or 2)
                    at += cells * 4;
            }

            var objects = new List<Ds1Object>();
            if (version >= 2 && at + 4 <= data.Length)
            {
                int count = Next();
                for (int n = 0; n < count && at + 16 <= data.Length; n++)
                {
                    int type = Next(), id = Next(), x = Next(), y = Next();
                    if (version > 5)
                        Next();   // flags
                    objects.Add(new Ds1Object(type, id, x, y));
                }
            }

            return new Ds1 { Version = version, Width = width, Height = height, Walls = walls, Floors = floors, Objects = objects };
        }
        catch (Exception problem) when (problem is IndexOutOfRangeException or ArgumentException)
        {
            // Cut short, or not a DS1: no map.
            return null;
        }
    }
}
