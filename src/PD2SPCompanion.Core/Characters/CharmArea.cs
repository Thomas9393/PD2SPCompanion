using System.Text;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Characters;

/// <summary>
/// PD2's charm section: the lower half of the character's inventory, where a charm has to
/// sit for its bonus to count.
/// <para>
/// <b>Where the line is does not come from a table.</b> `inventory.txt` gives the grid one
/// size and says nothing about a division; PD2 splits it in its own code. The half is
/// taken as exactly that - the bottom <c>Rows / 2</c> - and the rest of this class exists
/// so that assumption sits in one place rather than spread through the UI.
/// </para>
/// <para>
/// Nothing here refuses a placement. The game does not either: a character's inventory
/// holds ordinary items below the line and charms above it, which is what says the split
/// governs whether a charm <i>works</i>, not where it may be put. So this warns and leaves
/// the decision alone.
/// </para>
/// </summary>
public static class CharmArea
{
    /// <summary>
    /// Item types the tables flag as charms. Read rather than matched on the name, so
    /// anything PD2 adds is picked up: the flag currently covers small, medium and large
    /// charms and their unique variants.
    /// </summary>
    private static HashSet<string> _charmTypes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Each class's inventory, as the tables size it: what <see cref="Requirements.Counted"/> holds a charm's place
    /// to, which has no tables to ask. Empty before <see cref="Load"/>.
    /// </summary>
    private static Dictionary<int, GridSize> _inventories = [];

    /// <summary>A class's inventory size, or null before the tables are read.</summary>
    public static GridSize? InventoryOf(int classId) =>
        _inventories.TryGetValue(classId, out var size) ? size : null;

    public static void Load(GameData game)
    {
        _inventories = Enumerable.Range(0, CharacterClasses.Names.Count)
            .ToDictionary(classId => classId, classId => InventoryLayout.Load(game, (byte)classId, expansion: true).Inventory);

        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (game.Lines("itemtypes") is { } lines)
            {
                string[] header = lines[0].Split('\t');
                int code = Array.IndexOf(header, "Code");
                int charm = Array.IndexOf(header, "Charm");

                if (code >= 0 && charm >= 0)
                {
                    foreach (string line in lines.Skip(1))
                    {
                        string[] cells = line.Split('\t');
                        if (cells.Length > charm && cells[charm].Trim() == "1"
                            && cells[code].Trim().Length > 0)
                        {
                            found.Add(cells[code].Trim());
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
            // Without the table nothing is treated as a charm, which shows no warnings
            // rather than wrong ones.
        }

        _charmTypes = found;
    }

    public static bool IsCharm(Item item)
    {
        if (_charmTypes.Count == 0)
            return false;

        string type = D2SLib.Core.TXT.ItemsTXT.GetByCode(item.Code)?.Get("type")?.Value?.Trim() ?? "";
        return type.Length > 0 && _charmTypes.Contains(type);
    }

    /// <summary>The first row of the charm section. Rows above it are ordinary storage.</summary>
    public static int FirstRow(GridSize inventory) => inventory.Rows / 2;

    /// <summary>Whether the inventory is big enough to have a section at all.</summary>
    public static bool Applies(GridSize inventory) => inventory.Rows >= 4;

    /// <summary>
    /// Whether an item standing at <paramref name="y"/> sits wholly within the charm
    /// section. A charm straddling the line is not in it.
    /// </summary>
    public static bool Holds(GridSize inventory, int y, int height) =>
        Applies(inventory) && y >= FirstRow(inventory) && y + height <= inventory.Rows;

    /// <summary>
    /// Whether any of an item standing at <paramref name="y"/> lies in the charm section: what a fast dump leaves alone. A
    /// charm counts only wholly inside (<see cref="Holds"/>); a tall item reaching in from above is in the player's way all the same.
    /// </summary>
    public static bool Touches(GridSize inventory, int y, int height) =>
        Applies(inventory) && y + height > FirstRow(inventory);

    /// <summary>
    /// Whether putting this item here is worth warning about: a charm that would not be
    /// in the section, and so would not do anything.
    /// </summary>
    public static bool WouldBeIdle(Item item, GridSize inventory, int y, int height) =>
        IsCharm(item) && Applies(inventory) && !Holds(inventory, y, height);

    /// <summary>
    /// Why a charm standing at <paramref name="y"/> in the inventory would do nothing, or <see cref="CharmIdle.None"/>
    /// where it works: outside the area, or a unique charm another of which counts already - "still only 1 of each
    /// charm can be active at a time" (PD2's General Changes). The one that counts is the first in the save's order
    /// (<paramref name="inventory"/>, the inventory's items as the save lists them), in the area and identified; an
    /// item not in the list comes after every one in it, as an item put in joins the list's end. What
    /// <see cref="Requirements.Counted"/> counts, the tile's red mark and the landing's.
    /// </summary>
    public static CharmIdle IdleAt(Item item, IEnumerable<Item> inventory, GridSize size, int y, int height)
    {
        if (!IsCharm(item) || !Applies(size))
            return CharmIdle.None;

        if (!Holds(size, y, height))
            return CharmIdle.OutsideArea;

        if (item.Quality != ItemQuality.Unique || !item.IsIdentified)
            return CharmIdle.None;

        foreach (var other in inventory)
        {
            if (ReferenceEquals(other, item))
                return CharmIdle.None;

            if (other.Quality == ItemQuality.Unique && other.FileIndex == item.FileIndex && other.IsIdentified
                && Holds(size, other.Y, ItemMover.Footprint(other).Height))
                return CharmIdle.AnotherCounts;
        }

        return CharmIdle.None;
    }
}

/// <summary>Why a charm in the inventory does nothing (<see cref="CharmArea.IdleAt"/>).</summary>
public enum CharmIdle
{
    None,

    /// <summary>Not wholly in the charm area.</summary>
    OutsideArea,

    /// <summary>A unique charm, another of which counts already.</summary>
    AnotherCounts,
}
