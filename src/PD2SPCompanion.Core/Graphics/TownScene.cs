namespace PD2SPCompanion.Core.Graphics;

/// <summary>
/// An act's town as the game draws it, cut around the place its stash stands: the ground a character card's
/// figure is shown on, so the opened card is the character in its town and not a figure on black.
/// Read from the install like every picture the editor shows: the town's map (DS1) and its tile
/// sets (DT1), found through the game's own tables.
/// <para>
/// Which map and which tiles is the tables' to say: <c>levels.txt</c> names each act's first level - its town -
/// and that level's tile set; <c>lvlprest.txt</c> the town's map file and which of the set's files it uses
/// (<c>Dt1Mask</c>); <c>lvltypes.txt</c> the set's files. The palette is the act's own.
/// </para>
/// <para>
/// Tiles only: the townspeople, the stash chest, the waypoint and the fire are objects the game places and
/// animates over the map, not part of it, and are left out, deliberately: no townspeople, and no stash where it is an
/// in-game object.
/// </para>
/// </summary>
public sealed class TownScene
{
    private const string Tiles = @"data\global\tiles\";

    private readonly Ds1 _map;
    private readonly Dictionary<(int Orientation, int Main, int Sub), Dt1Tile> _tiles = [];
    private readonly Palette _palette;

    public Ds1 Map => _map;

    public int Act { get; }

    /// <summary>The map file the scene was read from, as lvlprest.txt names it.</summary>
    public string MapFile { get; }

    private TownScene(int act, string mapFile, Ds1 map, Palette palette)
    {
        Act = act;
        MapFile = mapFile;
        _map = map;
        _palette = palette;
    }

    /// <summary>
    /// The town of an act, 1 to 5, or null where the tables or the archives do not give one (a classic install
    /// has no fifth).
    /// </summary>
    public static TownScene? Open(GameData game, int act)
    {
        try
        {
            var levels = game.Excel("levels");
            int idColumn = levels.IndexOfColumn("Id"), actColumn = levels.IndexOfColumn("Act"), typeColumn = levels.IndexOfColumn("LevelType");
            if (idColumn < 0 || actColumn < 0 || typeColumn < 0)
                return null;

            // An act's town is its first level. The first act's rows leave the Act cell empty: nought.
            int ActOf(string[] row) => Cell(row, actColumn).Length == 0 ? 0 : Number(row, actColumn);
            var town = levels.Rows
                .Where(row => ActOf(row) == act - 1 && Number(row, idColumn) > 0)
                .OrderBy(row => Number(row, idColumn))
                .FirstOrDefault();
            if (town is null)
                return null;
            int level = Number(town, idColumn), type = Number(town, typeColumn);

            var presets = game.Excel("lvlprest");
            var preset = presets.Rows.FirstOrDefault(row => Number(row, presets.IndexOfColumn("LevelId")) == level);
            if (preset is null)
                return null;

            string? file = Enumerable.Range(1, 6)
                .Select(n => Cell(preset, presets.IndexOfColumn($"File{n}")))
                .FirstOrDefault(name => name.Length > 1);
            if (file is null || game.Read(Tiles + file.Replace('/', '\\')) is not { } mapData || Ds1.Read(mapData.Data) is not { } map)
                return null;

            var palette = game.Read($@"data\global\palette\ACT{act}\pal.dat") is { } colours
                ? Palette.FromDat(colours.Data)
                : Palette.Current ?? Palette.Load(game);

            var scene = new TownScene(act, file, map, palette);

            // The tile set's files the map uses: bit n of Dt1Mask is lvltypes' "File n+1".
            var types = game.Excel("lvltypes");
            var set = types.Rows.FirstOrDefault(row => Number(row, types.IndexOfColumn("Id")) == type);
            long mask = long.TryParse(Cell(preset, presets.IndexOfColumn("Dt1Mask")), out long bits) ? bits : -1;
            if (set is not null)
            {
                for (int n = 0; n < 32; n++)
                {
                    string name = Cell(set, types.IndexOfColumn($"File {n + 1}"));
                    if ((mask & (1L << n)) == 0 || name.Length <= 1 || game.Read(Tiles + name.Replace('/', '\\')) is not { } tileData)
                        continue;

                    foreach (var tile in Dt1.Read(tileData.Data))
                    {
                        // The first of a place stands, unless it is one the game never picks and a later one is not.
                        var key = (tile.Orientation, tile.MainIndex, tile.SubIndex);
                        if (!scene._tiles.TryGetValue(key, out var held) || (held.Rarity == 0 && tile.Rarity > 0))
                            scene._tiles[key] = tile;
                    }
                }
            }

            return scene._tiles.Count == 0 ? null : scene;
        }
        catch (Exception problem)
        {
            Diagnostics.Log.Caught(problem);
            return null;
        }
    }

    private static string Cell(string[] row, int column) => column >= 0 && column < row.Length ? row[column].Trim() : "";

    private static int Number(string[] row, int column) => int.TryParse(Cell(row, column), out int value) ? value : -1;

    /// <summary>Where a cell's box starts on the map's own canvas: cell 0,0 at 0,0, each step across a cell 80 right and 40 down.</summary>
    public static (int X, int Y) PlaceOf(int column, int row) => ((column - row) * (Dt1.CellWidth / 2), (column + row) * (Dt1.CellHeight / 2));

    /// <summary>Where a point given in fifths of a cell (an object's place) is on that canvas.</summary>
    public static (int X, int Y) PlaceOfPoint(double x, double y) =>
        ((int)Math.Round(Dt1.CellWidth / 2 + (x - y) * 16), (int)Math.Round((x + y) * 8));

    /// <summary>
    /// The map drawn through a window of the canvas: <paramref name="left"/>, <paramref name="top"/> its corner,
    /// in the canvas's own pixels. Floors and what hangs below them first, then the walls from the back of the
    /// map to the front, then the roofs - the order the game draws in. Clear where no tile reaches.
    /// </summary>
    /// <param name="roofs">
    /// Whether roofs are drawn. The game lifts a roof off while the character stands under it; a scene
    /// centred on a place under one - the Pandemonium Fortress's stash stands in its arcade - leaves them off.
    /// </param>
    public Picture Render(int left, int top, int width, int height, bool roofs = true)
    {
        var canvas = new uint[width * height];

        void Draw(Dt1Tile? tile, int x, int y)
        {
            if (tile is null || tile.Width == 0)
                return;

            x += tile.Left - left;
            y += tile.Top - top;
            if (x >= width || y >= height || x + tile.Width <= 0 || y + tile.Height <= 0)
                return;

            byte[] indices = tile.Indices;
            for (int row = Math.Max(0, -y); row < tile.Height && y + row < height; row++)
            {
                int from = row * tile.Width, to = (y + row) * width + x;
                for (int column = Math.Max(0, -x); column < tile.Width && x + column < width; column++)
                {
                    byte index = indices[from + column];
                    if (index != 0)
                        canvas[to + column] = _palette[index] | 0xFF000000;
                }
            }
        }

        Dt1Tile? Tile(int orientation, Ds1Cell cell) =>
            _tiles.GetValueOrDefault((orientation, cell.MainIndex, cell.SubIndex));

        // A cell reaches the window where its box does, with room above for the tallest wall and below for
        // what hangs under a floor.
        bool Reaches(int x, int y) => x < width && x + Dt1.CellWidth > 0 && y - 480 < height && y + 320 > 0;

        void Pass(Action<int, int, int> each)
        {
            for (int row = 0; row < _map.Height; row++)
            {
                for (int column = 0; column < _map.Width; column++)
                {
                    var (x, y) = PlaceOf(column, row);
                    x -= left;
                    y -= top;
                    if (Reaches(x, y))
                        each(row * _map.Width + column, x + left, y + top);
                }
            }
        }

        // Under the floors: the walls that hang below a cell (a cliff's face).
        Pass((cell, x, y) =>
        {
            foreach (var layer in _map.Walls)
            {
                var wall = layer[cell];
                if (!wall.IsEmpty && !wall.IsHidden && wall.Orientation > 15)
                    Draw(Tile(wall.Orientation, wall), x, y + Dt1.CellHeight);
            }
        });

        Pass((cell, x, y) =>
        {
            foreach (var layer in _map.Floors)
            {
                var floor = layer[cell];
                if (!floor.IsEmpty && !floor.IsHidden)
                    Draw(Tile(0, floor), x, y);
            }
        });

        // The walls standing on a cell. 10 and 11 are the map's markers, never drawn; a left-and-top corner
        // (3) is two tiles, its other half kept under orientation 4.
        Pass((cell, x, y) =>
        {
            foreach (var layer in _map.Walls)
            {
                var wall = layer[cell];
                if (wall.IsEmpty || wall.IsHidden || wall.Orientation is 0 or 10 or 11 or >= 15)
                    continue;

                Draw(Tile(wall.Orientation, wall), x, y + Dt1.CellHeight);
                if (wall.Orientation == 3)
                    Draw(Tile(4, wall), x, y + Dt1.CellHeight);
            }
        });

        if (roofs)
        {
            Pass((cell, x, y) =>
            {
                foreach (var layer in _map.Walls)
                {
                    var wall = layer[cell];
                    if (!wall.IsEmpty && !wall.IsHidden && wall.Orientation == 15 && Tile(15, wall) is { } roof)
                        Draw(roof, x, y - roof.RoofHeight);
                }
            });
        }

        return new Picture(width, height, canvas);
    }

    /// <summary>The stash's row in objects.txt ("bank").</summary>
    private const int BankObject = 267;

    /// <summary>
    /// The number a town's map gives its stash, act by act. A map does not name an object by its objects.txt
    /// row: it gives an entry of a table of 150 the engine keeps for each act, in no file the game ships as
    /// text. Read out of D2Common.dll (1.13c, as PD2 runs it): the one table whose first act
    /// holds the Rogue bonfire (row 39) at entry 2, the waypoint (row 119) at 52 and the stash (row 267) at
    /// 102 - the three that had been told on the drawn Rogue Encampment by where they stand. The stash is
    /// entry 102, 21, 34, 2 and 44 of the five acts (the fifth's table holds it at 4 as well); each is on
    /// its town's map exactly once. A number of 150 or more is an objects.txt row plus 150, in any act.
    /// </summary>
    private static readonly int[][] StashEntry = [[102], [21], [34], [2], [4, 44]];

    /// <summary>Where the stash stands, in cells, or null where the map places none.</summary>
    public (double Column, double Row)? Stash =>
        _map.Objects.FirstOrDefault(placed => placed.Type == 2
                && (placed.Id == BankObject + 150 || (Act is >= 1 and <= 5 && StashEntry[Act - 1].Contains(placed.Id))))
            is { } stash
            ? (stash.X / 5.0, stash.Y / 5.0)
            : null;

    /// <summary>
    /// Where a character stands by the stash, in cells from it: beside the chest on open ground, the way a
    /// player stands to open it. Chosen by eye on each town's drawn map - what is walkable and in
    /// sight is the engine's to know. The Rogue Encampment's as in a screenshot, right of the
    /// barrel cart; the Pandemonium Fortress's out on the tiles before the arcade, since its stash stands in
    /// the hall behind a wall this view cannot see through - and down its three steps onto the floor below them
    /// (on the last step's edge the figure stood with half its feet in the air).
    /// </summary>
    private static readonly (double Column, double Row)[] StandBy =
        [(0.65, -0.3), (1.1, 0.1), (1.1, 0.1), (5.1, 0.9), (1.6, 0.9)];

    /// <summary>
    /// The backdrop's size in the game's pixels: more than an opened card shows of it at the figure's scale -
    /// some 440 by 460 at the least scale, 290 by 310 at the most (measured on the card) - with
    /// room for a wider window. The card darkens its own rim (the Shell's TownBackdrop).
    /// </summary>
    public const int BackdropWidth = 640, BackdropHeight = 560;

    /// <summary>
    /// How far from its middle the backdrop is whole, and where it has faded out, as shares of its half size:
    /// only its rim, so a card wider than it shows no cut edge.
    /// </summary>
    private const double Whole = 0.85, Gone = 1.0;

    /// <summary>How far from its middle the backdrop's light is measured: about what the card shows.</summary>
    private const double Measured = 0.4;

    /// <summary>
    /// How light the backdrop's middle is drawn, as its mean of 255: the towns' own tiles differ widely -
    /// the game lights them itself, and Lut Gholein's sand is near three times as light as Kurast's stone (89
    /// against 32, measured) - so each is brought toward one level the card's text reads over, by
    /// no more than <see cref="MostGain"/> and no less than <see cref="LeastGain"/>.
    /// </summary>
    private const double Light = 50, LeastGain = 0.7, MostGain = 1.6;

    /// <summary>
    /// The town around its stash, the ground a character card's figure stands on: <see cref="BackdropWidth"/>
    /// by <see cref="BackdropHeight"/>, the place to stand at its middle, no roofs, lit evenly and faded out
    /// toward its edges so it ends in the card's black with no frame of its own. Null where the map places
    /// no stash.
    /// </summary>
    public Picture? Backdrop()
    {
        if (Stash is not { } stash)
            return null;

        var by = StandBy[Math.Clamp(Act, 1, 5) - 1];
        var (x, y) = PlaceOfPoint((stash.Column + by.Column) * 5, (stash.Row + by.Row) * 5);
        var picture = Render(x - BackdropWidth / 2, y - BackdropHeight / 2, BackdropWidth, BackdropHeight, roofs: false);

        static double Far(int column, int row)
        {
            double dx = (column - BackdropWidth / 2.0) / (BackdropWidth / 2.0), dy = (row - BackdropHeight / 2.0) / (BackdropHeight / 2.0);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // How light the middle is as the tiles give it.
        double sum = 0;
        int counted = 0;
        for (int row = 0; row < BackdropHeight; row++)
        {
            for (int column = 0; column < BackdropWidth; column++)
            {
                uint pixel = picture.Bgra[row * BackdropWidth + column];
                if (pixel >> 24 == 0 || Far(column, row) > Measured)
                    continue;
                sum += (((pixel >> 16) & 0xFF) * 299 + ((pixel >> 8) & 0xFF) * 587 + (pixel & 0xFF) * 114) / 1000.0;
                counted++;
            }
        }

        double gain = counted == 0 || sum == 0 ? 1 : Math.Clamp(Light / (sum / counted), LeastGain, MostGain);

        for (int row = 0; row < BackdropHeight; row++)
        {
            for (int column = 0; column < BackdropWidth; column++)
            {
                uint pixel = picture.Bgra[row * BackdropWidth + column];
                if (pixel >> 24 == 0)
                    continue;

                double kept = Math.Clamp((Gone - Far(column, row)) / (Gone - Whole), 0, 1);
                kept = kept * kept * (3 - 2 * kept);   // eased, so the fade has no edge of its own

                // Faded toward black rather than toward clear: the card behind is black, and a picture's
                // edge half clear draws lighter than its middle where it is scaled.
                uint Channel(int shift) => (uint)Math.Min(255, Math.Round(((pixel >> shift) & 0xFF) * gain * kept));
                picture.Bgra[row * BackdropWidth + column] = 0xFF000000 | (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
            }
        }

        return picture;
    }

    /// <summary>The whole map's box on the canvas, with room for the walls at its top.</summary>
    public (int Left, int Top, int Width, int Height) Bounds()
    {
        int left = -(_map.Height - 1) * (Dt1.CellWidth / 2);
        int width = (_map.Width + _map.Height) * (Dt1.CellWidth / 2);
        int height = (_map.Width + _map.Height) * (Dt1.CellHeight / 2) + 240;
        return (left, -240, width, height);
    }
}
