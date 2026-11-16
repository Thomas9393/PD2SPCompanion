using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>One framed block of the materials tab: a small grid, and where it sits.</summary>
/// <param name="JoinsAbove">
/// Drawn as part of the block above - the fourth rune row: no top border, its top frame laid over
/// the block above's bottom frame. See <c>ContainerGrid.IsJoinedAbove</c>.
/// </param>
public sealed record MaterialPanel(GridViewModel Grid, double Left, double Top, bool JoinsAbove = false)
{
    /// <summary>Where the block sits, as a margin: the tab sizes itself to what the blocks take.</summary>
    public Avalonia.Thickness Offset => new(Left, Top, 0, 0);
}

/// <summary>A heading over a part of the tab, and where it sits.</summary>
public sealed record MaterialCaption(string Text, double Left, double Top)
{
    /// <summary>Where the heading sits, as a margin, as a block does.</summary>
    public Avalonia.Thickness Offset => new(Left, Top, 0, 0);
}

/// <summary>
/// The tab as the Items page lists it - one "Materials" page, Season 13's or Season 14's - or the rune wizard's runes.
/// Its blocks and headings, drawn by <c>MaterialsTab</c>.
/// </summary>
public sealed partial class MaterialSheet(string name, string shortName) : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    /// <summary>What the pages list calls it.</summary>
    public string Name { get; } = name;

    /// <summary>Its name where the pages list is squeezed.</summary>
    public string Short { get; } = shortName;

    public ObservableCollection<MaterialPanel> Panels { get; } = [];

    public ObservableCollection<MaterialCaption> Captions { get; } = [];

    /// <summary>
    /// The size it is shown at, scrolling inside: Season 14's, a full stash page's - beside a full item page, anything
    /// less was too small. Not a number for a sheet that takes the size of its blocks.
    /// </summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    public partial double ViewWidth { get; set; } = double.NaN;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    public partial double ViewHeight { get; set; } = double.NaN;
}

/// <summary>
/// The shared stash's materials tab, laid out as the game lays it out. Season 13's is one sheet: runes across the
/// top, keys and organs down the left, the potions, cube, shards and dusts in the middle, the gems on the right, the
/// orbs along the bottom. Season 14's game splits it into tabs, and so does this (<see cref="Season14"/>).
/// <para>
/// Each block is an ordinary grid drawn by <c>ContainerGrid</c>, so the cells, the art, the
/// count, the hover and the clicks are the same ones every container on the page has. The
/// tiles stand for counts rather than items - see <see cref="ItemTileViewModel.Tally"/> - and a
/// tile or a cell is traced back to its slot by reference, never by a page number, so nothing
/// else on the page can be mistaken for part of the tab.
/// </para>
/// </summary>
public sealed class MaterialsViewModel
{
    /// <summary>A block, in cells of the grid pitch, and whether it joins the block above (see <see cref="JoinedFrame"/>).</summary>
    private sealed record Block(double X, double Y, int Columns, int Rows, bool JoinsAbove = false);

    /// <summary>A heading, in cells of the grid pitch.</summary>
    private sealed record Heading(string Text, double X, double Y);

    /// <summary>A sheet's blocks and headings, and where each slot it shows is drawn: its block, and its cell in that block.</summary>
    private sealed record Layout(string Name, string Short, Block[] Blocks, IReadOnlyDictionary<int, (int Block, int X, int Y)> Where,
                                 IReadOnlyList<Heading>? Headings = null);

    /// <summary>
    /// How far the fourth rune row sits below three cells: the top frame of the block above - a
    /// 1-pixel border and 2 of padding (ContainerGrid's gridframe) - which is where that block's
    /// cells end. With no top border or top padding of its own (<c>IsJoinedAbove</c>), the row's
    /// cells start there and cover the block above's bottom frame, and its gold edge shows only
    /// where the row is not - one box stepping in, as the game draws it.
    /// Placed a whole frame lower, as it was, the two frames sat one under the other. One pixel
    /// more than the frame: at exactly the frame, the row's gold sides stuck up past the block
    /// above's bottom edge (seen on a screenshot).
    /// </summary>
    private const double JoinedFrame = 1 + 2 + 1;

    /// <summary>A grid's frame across, both sides: a 1-pixel border and 2 of padding each (ContainerGrid's gridframe).</summary>
    private const double GridFrame = 2 * (1 + 2);

    /// <summary>Room beside Season 14's page for its scroll bar, so the bar covers no cell.</summary>
    private const double ScrollBarRoom = 14;

    /// <summary>
    /// The code a slot's tile is drawn as. The rune, gem and skull slots keep <b>stacks</b>
    /// (<c>r01s</c>, <c>glys</c>, <c>skzs</c>), whose pictures (<c>invrsEl</c>, <c>invgsyds</c>) carry a
    /// gold "+"; the game's tab draws the plain item (<c>invrEl</c>, <c>invgsyd</c> - two
    /// screenshots of the tab) - so the tile is the plain item. Only the picture and the
    /// tooltip's name follow: taking from and putting into the tab go by slot and by what the stash
    /// hands out (<see cref="Core.Saves.SharedStash.MaterialCode"/>).
    /// </summary>
    private static string Drawn(int slot) => Materials.Plain(slot);

    /// <summary>The runes are the first 33 slots.</summary>
    private const int RuneSlots = 33;

    /// <summary>
    /// Each gem column holds Topaz, Diamond, Amethyst, Sapphire, Emerald, Ruby, Skull in the
    /// file; the tab draws them Ruby, Amethyst, Sapphire, Emerald, Topaz, Diamond, Skull. This
    /// is the row each file position is drawn on, read off the numbered tab - and in Season 14's
    /// Gems tab the column, the same order turned on its side (the numbered tab).
    /// </summary>
    private static readonly int[] GemRow = [4, 5, 1, 2, 3, 0, 6];

    /// <summary>The runes, El to Zod: three rows of nine, the fourth row of six half a cell in, joined to them.</summary>
    private static readonly Block[] RuneBlocks = [new(0, 0, 9, 3), new(1.5, 3, 6, 1, JoinsAbove: true)];

    private static void LayRunes(Dictionary<int, (int, int, int)> at)
    {
        for (int i = 0; i < 27; i++) at[1 + i] = (0, i % 9, i / 9);
        for (int i = 0; i < 6; i++) at[28 + i] = (1, i, 0);
    }

    /// <summary>
    /// Season 13's tab, one sheet. The blocks measured off the game's own tab: a rune cell is
    /// one, the fourth row of runes sits half a cell in, and the middle column starts half a
    /// cell past the left block.
    /// </summary>
    private static readonly Layout Season13 = LaySeason13();

    private static Layout LaySeason13()
    {
        Block[] blocks =
        [
            .. RuneBlocks,             // 0, 1 runes
            new(0, 4.5, 3, 7),         // 2 keys, organs, sigils, clone, Rathma, Lucion
            new(3.5, 4.5, 2, 1),       // 3 the two potions
            new(3.5, 6, 2, 3),         // 4 cube, fragments, shards, Larzuk's
            new(3.5, 9.5, 2, 2),       // 5 the four essences
            new(6, 4.5, 3, 7),         // 6 gems and infusions
            new(1, 12, 7, 2),          // 7 the orbs
        ];

        var at = new Dictionary<int, (int, int, int)>();
        LayRunes(at);

        // The keys stand two cells tall, so the rest of the block starts below them.
        for (int i = 0; i < 3; i++) at[55 + i] = (2, i, 0);
        for (int i = 0; i < 15; i++) at[58 + i] = (2, i % 3, 2 + i / 3);

        for (int i = 0; i < 2; i++) at[73 + i] = (3, i, 0);
        for (int i = 0; i < 6; i++) at[75 + i] = (4, i % 2, i / 2);
        for (int i = 0; i < 4; i++) at[81 + i] = (5, i % 2, i / 2);

        for (int column = 0; column < 3; column++)
            for (int j = 0; j < 7; j++)
                at[34 + 7 * column + j] = (6, column, GemRow[j]);

        for (int i = 0; i < 14; i++) at[85 + i] = (7, i % 7, i / 7);

        return new Layout(L("Materials"), L("Mat"), blocks, at);
    }

    /// <summary>The runes alone, El to Zod, as both seasons draw them: Season 14's Runes tab and the rune wizard's.</summary>
    private static readonly Layout RunesAlone = LayRunesAlone();

    private static Layout LayRunesAlone()
    {
        var at = new Dictionary<int, (int, int, int)>();
        LayRunes(at);
        return new Layout(L("Runes"), L("Runes"), RuneBlocks, at);
    }

    /// <summary>
    /// Season 14's tab: the game's tabs one under another on one page, each under its name, the page scrolling (each
    /// tab a row of the pages list was tried first, and was too small beside a full item page). Each part laid out off
    /// screenshots of the game's tabs: each cell is the slot the numbered file showed in it.
    /// The editor's slots (<see cref="Materials.CodeAt"/>):
    /// 1-98 as Season 13 numbers them, 99 on <see cref="Materials.Season14Codes"/>. The panel under the maps, which the
    /// game shows with every tier, is here once, after the third.
    /// </summary>
    private static readonly Layout Season14 = Stack(L("Materials"), L("Mat"),
    [
        (L("Gems"), LayGems()), (L("Runes"), RunesAlone), (L("Misc"), LayMisc()),
        (L("Tier 1 Maps"), LayTier(1)), (L("Tier 2 Maps"), LayTier(2)), (L("Tier 3 Maps"), LayTier(3)), (null, LayUnderMaps()),
        (L("Ubers"), LayUbers()),
    ]);

    /// <summary>The page's width the parts are centred in, in cells: a stash page's ten.</summary>
    private const int StackWidth = 10;

    /// <summary>A heading's room, and the room between one part and the next heading, in cells.</summary>
    private const double HeadingRoom = 0.8, PartGap = 0.6;

    /// <summary>Parts one under another, each centred and under its heading (none for a part that belongs to the one above).</summary>
    private static Layout Stack(string name, string brief, IReadOnlyList<(string? Heading, Layout Part)> parts)
    {
        var blocks = new List<Block>();
        var at = new Dictionary<int, (int, int, int)>();
        var headings = new List<Heading>();
        double y = 0;

        foreach (var (heading, part) in parts)
        {
            double width = part.Blocks.Max(block => block.X + block.Columns);
            double height = part.Blocks.Max(block => block.Y + block.Rows);
            double x = (StackWidth - width) / 2;

            if (heading is not null)
            {
                if (y > 0)
                    y += PartGap;
                headings.Add(new Heading(heading, x, y));
                y += HeadingRoom;
            }
            else
            {
                y += PartGap / 2;
            }

            int first = blocks.Count;
            blocks.AddRange(part.Blocks.Select(block => block with { X = block.X + x, Y = block.Y + y }));
            foreach (var (slot, (block, column, row)) in part.Where)
                at[slot] = (first + block, column, row);
            y += height;
        }

        return new Layout(name, brief, [.. blocks], at, headings);
    }

    /// <summary>The editor's slot of one of <see cref="Materials.Season14Codes"/>.</summary>
    private static int Added(string code) => Materials.Count + 1 + Materials.Season14Codes.ToList().IndexOf(code);

    /// <summary>Six rows of seven: chipped, flawed, plain, flawless, perfect, the infusions - each Ruby first, Skull last.</summary>
    private static Layout LayGems()
    {
        var at = new Dictionary<int, (int, int, int)>();
        int lower = Added("gcy");
        for (int row = 0; row < 3; row++)
            for (int j = 0; j < 7; j++)
                at[lower + 7 * row + j] = (0, GemRow[j], row);
        for (int row = 0; row < 3; row++)
            for (int j = 0; j < 7; j++)
                at[34 + 7 * row + j] = (0, GemRow[j], 3 + row);
        return new Layout(L("Gems"), L("Gems"), [new(0, 0, 7, 6)], at);
    }

    /// <summary>
    /// Three blocks: fragments, the Puzzlepiece, the cube, the Vial and the Mirror standing two tall, and under them the
    /// Worldstone Shard, the Puzzlebox, the Tainted Shard; the four essences and the Token; the two potions.
    /// </summary>
    private static Layout LayMisc()
    {
        var at = new Dictionary<int, (int, int, int)>
        {
            [76] = (0, 0, 0), [80] = (0, 1, 0), [75] = (0, 2, 0), [Added("lsvl")] = (0, 3, 0), [Added("llmr")] = (0, 4, 0),
            [78] = (0, 0, 1), [79] = (0, 1, 1), [77] = (0, 2, 1),
            [81] = (1, 0, 0), [82] = (1, 1, 0), [83] = (1, 2, 0), [84] = (1, 3, 0), [Added("toa")] = (1, 4, 0),
            [73] = (2, 0, 0), [74] = (2, 1, 0),
        };
        return new Layout(L("Misc"), L("Misc"), [new(0, 0, 5, 2), new(0, 3, 5, 1), new(1.5, 5, 2, 1)], at);
    }

    /// <summary>A tier of maps, two rows of nine, in the file's order (<see cref="Materials.Season14Codes"/>).</summary>
    private static Layout LayTier(int tier)
    {
        string[] maps = tier switch
        {
            1 => ["t11", "t21", "t22", "t24", "t33", "t34", "t36", "t26", "t14", "t18"],
            2 => ["t12", "t25", "t32", "t37", "t38", "t16", "t3a", "t17", "t29", "t2a"],
            _ => ["t13", "t23", "t31", "t35", "t39", "t15", "t28", "t27", "t3b"],
        };

        var at = new Dictionary<int, (int, int, int)>();
        for (int i = 0; i < maps.Length; i++)
            at[Added(maps[i])] = (0, i % 9, i / 9);
        return new Layout("", "", [new(0, 0, 9, 2)], at);
    }

    /// <summary>The panel the game shows under every tier: the standard, the shards and orbs in two rows of seven, the ears in a third.</summary>
    private static Layout LayUnderMaps()
    {
        var at = new Dictionary<int, (int, int, int)>();
        for (int i = 0; i < 14; i++)
            at[85 + i] = (0, i % 7, i / 7);
        for (int i = 0; i < 7; i++)
            at[Added("ivea") + i] = (0, i, 2);
        return new Layout("", "", [new(0, 0, 7, 3)], at);
    }

    /// <summary>
    /// The keys (two tall), the organs and the Uber Tristram map under them; beside them the sigils, the clone's,
    /// Rathma's and Lucion's in four columns, the four uber maps along the bottom.
    /// </summary>
    private static Layout LayUbers()
    {
        var at = new Dictionary<int, (int, int, int)>();
        for (int i = 0; i < 3; i++) at[55 + i] = (0, i, 0);
        for (int i = 0; i < 3; i++) at[58 + i] = (0, i, 2);
        at[Added("ubtm")] = (1, 0, 0);

        // Down each column: the three sigils; the clone's essence, soul and soulstone; Rathma's jawbone, splinter and
        // ashes; Lucion's insignia, then the flesh, then the talisman - as the game has them.
        int[][] columns = [[61, 62, 63], [64, 65, 66], [67, 68, 69], [70, 72, 71]];
        for (int column = 0; column < 4; column++)
            for (int row = 0; row < 3; row++)
                at[columns[column][row]] = (2, column, row);
        string[] maps = ["uba", "dcma", "rtma", "luca"];
        for (int column = 0; column < 4; column++)
            at[Added(maps[column])] = (2, column, 3);

        return new Layout(L("Ubers"), L("Ubers"), [new(0, 0, 3, 3), new(1, 3, 1, 1, JoinsAbove: true), new(4, 0, 4, 4)], at);
    }

    /// <summary>The sheets now drawn: Season 13's tab, Season 14's, or the runes alone.</summary>
    public IReadOnlyList<MaterialSheet> Sheets => _sheets;

    /// <summary>The first sheet - the whole tab in Season 13, the runes in the rune wizard.</summary>
    public MaterialSheet? First => _sheets.Count > 0 ? _sheets[0] : null;

    /// <summary>Every block of every sheet drawn: what a search, a hover or a click is asked against.</summary>
    public IEnumerable<MaterialPanel> Panels => _sheets.SelectMany(sheet => sheet.Panels);

    /// <summary>The sheet each layout is drawn into, kept so a row of the pages list keeps its sheet across redraws.</summary>
    private readonly Dictionary<Layout, MaterialSheet> _sheetOf = [];

    private List<MaterialSheet> _sheets = [];

    /// <summary>The sheets a stash of this kind is drawn as, for the pages list to offer one row each: one, either season.</summary>
    public IReadOnlyList<MaterialSheet> SheetsFor(bool season14) => [SheetOf(season14 ? Season14 : Season13)];

    private MaterialSheet SheetOf(Layout layout)
    {
        if (!_sheetOf.TryGetValue(layout, out var sheet))
            _sheetOf[layout] = sheet = new MaterialSheet(layout.Name, layout.Short);
        return sheet;
    }

    /// <summary>What the panels were drawn from - the tables, the stamp, every count and note. Null when empty.</summary>
    private string? _drawnFrom;

    private readonly Dictionary<ItemTileViewModel, int> _slotOfTile = [];
    private readonly Dictionary<GridCell, int> _slotOfCell = [];

    /// <summary>The tile now drawn for a slot - a redraw makes new ones. A slot on several sheets (the panel under the maps) answers its first.</summary>
    public ItemTileViewModel? TileFor(int slot) =>
        _slotOfTile.FirstOrDefault(pair => pair.Value == slot).Key;

    /// <summary>The slot a tile shows, when it is one of the tab's.</summary>
    public int? SlotOf(ItemTileViewModel tile) =>
        _slotOfTile.TryGetValue(tile, out int slot) ? slot : null;

    /// <summary>The slot a cell belongs to, when it is one of the tab's.</summary>
    public int? SlotAt(GridCell cell) =>
        _slotOfCell.TryGetValue(cell, out int slot) ? slot : null;

    /// <summary>Whether a cell is part of the tab at all, filled or not.</summary>
    public bool Owns(GridCell cell) => Panels.Any(panel => panel.Grid.Cells.Contains(cell));

    /// <summary>Every cell the tab draws, for clearing a hover.</summary>
    public IEnumerable<GridCell> Cells => Panels.SelectMany(panel => panel.Grid.Cells);

    /// <summary>Empties the tab, for when there is no stash to draw.</summary>
    public void Clear()
    {
        _drawnFrom = null;
        foreach (var sheet in _sheetOf.Values)
        {
            sheet.Panels.Clear();
            sheet.Captions.Clear();
        }
        _slotOfTile.Clear();
        _slotOfCell.Clear();
    }

    /// <summary>
    /// Draws the tab again from the counts. The tiles are made the way a material is made when
    /// it is taken out - <see cref="ItemForge.Material"/> - so the art is the item's own.
    /// </summary>
    /// <param name="onlyRunes">
    /// The two rune blocks alone, El to Zod - how the rune wizard shows what there is to spend.
    /// </param>
    /// <param name="notesOf">Lines a slot's tooltip adds under its quantity, where there are any.</param>
    /// <param name="season14">Season 14's tab, for a stash that keeps what it holds (<c>SharedStash.Holds</c>).</param>
    /// <param name="page">A stash page's grid, columns by rows: Season 14's tab is shown that size and scrolls.</param>
    public void Refresh(Func<int, int> countOf, string stampAs, bool onlyRunes = false,
                        Func<int, IReadOnlyList<string>>? notesOf = null, bool season14 = false, (int Columns, int Rows)? page = null)
    {
        IReadOnlyList<Layout> layouts = onlyRunes ? [RunesAlone] : [season14 ? Season14 : Season13];
        int last = onlyRunes ? RuneSlots : season14 ? Materials.Every : Materials.Count;
        var counts = new int[last + 1];
        var notes = new IReadOnlyList<string>?[last + 1];
        for (int slot = 1; slot <= last; slot++)
        {
            counts[slot] = countOf(slot);
            notes[slot] = notesOf?.Invoke(slot);
        }

        // Drawn again only from something new: every edit and every move asked, and the tab - every
        // tile and its cells, made afresh - was 3 ms of each move on this PC (measured)
        // for counts no move had touched.
        string from = string.Join("|",
            PD2SPCompanion.Core.GameFiles.InstalledTables.Generation, stampAs, onlyRunes, season14, page, string.Join(",", counts),
            string.Join(";", notes.Select(lines => lines is null ? "" : string.Join("/", lines))));
        if (from == _drawnFrom && Panels.Any())
            return;

        Clear();
        _drawnFrom = from;
        _sheets = [.. layouts.Select(SheetOf)];

        foreach (var layout in layouts)
        {
            var grids = layout.Blocks
                .Select(block => new GridViewModel("Materials", 0, block.Columns, block.Rows))
                .ToList();

            foreach (var (slot, (block, x, y)) in layout.Where)
            {
                int count = counts[slot];

                var item = ItemForge.Material(Drawn(slot), Math.Max(1, count), stampAs);
                var tile = new ItemTileViewModel(item, slot - 1, x, y, tally: count, tabSlot: slot,
                                                 notes: notes[slot]);

                grids[block].Items.Add(tile);
                _slotOfTile[tile] = slot;

                foreach (var cell in grids[block].Cells.Where(cell =>
                             cell.Column >= x && cell.Column < x + tile.View.GridWidth
                             && cell.Row >= y && cell.Row < y + tile.View.GridHeight))
                {
                    _slotOfCell[cell] = slot;
                }
            }

            var sheet = SheetOf(layout);
            foreach (var heading in layout.Headings ?? [])
                sheet.Captions.Add(new MaterialCaption(heading.Text, heading.X * ItemTileViewModel.CellSize, heading.Y * ItemTileViewModel.CellSize));

            // A full stash page's size: its grid's frame around the cells, and room for the scroll bar beside.
            bool sized = layout == Season14 && page is not null;
            sheet.ViewWidth = sized ? page!.Value.Columns * ItemTileViewModel.CellSize + GridFrame + ScrollBarRoom : double.NaN;
            sheet.ViewHeight = sized ? page!.Value.Rows * ItemTileViewModel.CellSize + GridFrame : double.NaN;

            for (int i = 0; i < layout.Blocks.Length; i++)
            {
                var block = layout.Blocks[i];
                sheet.Panels.Add(new MaterialPanel(
                    grids[i],
                    block.X * ItemTileViewModel.CellSize,
                    block.Y * ItemTileViewModel.CellSize + (block.JoinsAbove ? JoinedFrame : 0),
                    JoinsAbove: block.JoinsAbove));
            }
        }
    }
}
