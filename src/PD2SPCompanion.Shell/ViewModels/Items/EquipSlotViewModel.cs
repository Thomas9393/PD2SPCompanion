using CommunityToolkit.Mvvm.ComponentModel;
using D2SLib.Model.Save;
using System.Collections.Generic;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// One slot of the equipment panel: where it sits, how big its box is, and what the
/// character has in it.
/// <para>
/// The slots are fixed and the boxes are the sizes the game reserves, so a slot keeps its
/// shape whether or not anything is worn in it. An item's own grid size is not used here -
/// a 1x3 javelin sits in the same weapon box as a 2x4 bow.
/// </para>
/// </summary>
public sealed partial class EquipSlotViewModel(EquipSlot slot, ItemTileViewModel? item) : ObservableObject
{
    public EquipSlot Slot { get; } = slot;
    /// <summary>
    /// What is worn there. Settable: the session keeps one slot object per slot across rebuilds and
    /// puts the item in, so the panel drawing the slots is not built again with every move.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(IsFilled))]
    private ItemTileViewModel? _item = item;

    /// <summary>Something is carried over this slot; <see cref="IsAllowed"/> says whether it would go on.</summary>
    [ObservableProperty] private bool _isTarget;

    [ObservableProperty] private bool _isAllowed;

    public string Label => Slot.Label;
    public bool IsEmpty => Item is null;
    public bool IsFilled => Item is not null;

    /// <summary>
    /// Where the page puts it, in pixels on the panel: fixed by the slot, not by what is in it.
    /// Placed rather than laid out in rows, measured with the game's panel beside
    /// ours: a weapon box with its I and II tabs is 134 pixels tall, no whole number of rows, so
    /// rows either left a wide gap under it or could not put the gloves' bottom level with the
    /// belt's. The left edge includes the 3-pixel margin either side of the box.
    /// </summary>
    public double Left => Slot.Left;
    public double Top => Slot.Top;

    public double Width => Slot.Columns * ItemTileViewModel.CellSize;
    public double Height => Slot.Rows * ItemTileViewModel.CellSize;

    public bool IsSwap => Slot.IsSwap;

    /// <summary>A weapon box, which carries the I and II tabs above it.</summary>
    public bool HasSetTabs => Slot.HasSetTabs;

    private const int Cell = ItemTileViewModel.CellSize;

    /// <summary>A box's width on the panel with its 3-pixel margins: two cells, or one.</summary>
    private const int Wide = 2 * Cell + 6, Narrow = Cell + 6;

    /// <summary>
    /// The weapon set tabs above a weapon box: 20 high and a 6-pixel gap to the box (2 was too close) -
    /// the top row, tabs, helm and amulet, lifted by the 4 it gained.
    /// </summary>
    private const int Tabs = 26;

    /// <summary>How far the top row (tabs, helm, amulet) is lifted from the rest.</summary>
    private const int Lift = 4;

    /// <summary>The gap the game leaves between boxes stacked in a column.</summary>
    private const int Gap = 6;

    // The character's five columns: weapon and gloves; a ring; helm, armour and belt; the
    // amulet and the other ring; off-hand and boots.
    private const int C0 = 0, C1 = Wide, C2 = Wide + Narrow, C3 = 2 * Wide + Narrow, C4 = 2 * Wide + 2 * Narrow;

    // Down the middle: helm, then armour; the belt under the armour. The gloves and boots
    // start just under the weapon boxes and end level with the belt, which ends as low as either
    // needs: the weapon column's (tabs, four cells, a gap, the gloves' two) or the middle one's.
    private const int Armour = 2 * Cell + Gap + Lift;
    private const int Bottom = Tabs + 4 * Cell + Gap + 2 * Cell;
    private const int Belt = Bottom - Cell;
    private const int Gloves = Bottom - 2 * Cell;

    /// <summary>How big the character's panel is.</summary>
    public static double PanelWidth => C4 + Wide;
    public static double PanelHeight => Bottom;

    /// <summary>
    /// The slots of the game's equipment panel, where the game draws them: five columns - weapon
    /// over gloves; a ring; helm over armour over belt; the amulet beside the helm and the other
    /// ring; off-hand over boots. The rings and belt sit level with the bottom of the gloves and
    /// boots.
    /// </summary>
    public static IReadOnlyList<EquipSlot> Slots { get; } =
    [
        new(ItemLocation.RightHand,   L("Weapon"),   2, 4, C0, 0, HasSetTabs: true),
        new(ItemLocation.Head,        L("Helm"),     2, 2, C2, 0),
        new(ItemLocation.Neck,        L("Amu"),      1, 1, C3, Cell / 2),
        new(ItemLocation.LeftHand,    L("Off-hand"), 2, 4, C4, 0, HasSetTabs: true),

        new(ItemLocation.Torso,       L("Armor"),    2, 3, C2, Armour),

        new(ItemLocation.Gloves,      L("Gloves"),   2, 2, C0, Gloves),
        new(ItemLocation.RightFinger, L("Ring"),     1, 1, C1, Belt),
        new(ItemLocation.Waist,       L("Belt"),     2, 1, C2, Belt),
        new(ItemLocation.LeftFinger,  L("Ring"),     1, 1, C3, Belt),
        new(ItemLocation.Feet,        L("Boots"),    2, 2, C4, Gloves),

        // The second weapon set. It occupies the same two boxes as the first, which the
        // I and II tabs above them switch between, exactly as the game does.
        new(ItemLocation.SwapRight,   L("Weapon"),   2, 4, C0, 0, IsSwap: true, HasSetTabs: true),
        new(ItemLocation.SwapLeft,    L("Off-hand"), 2, 4, C4, 0, IsSwap: true, HasSetTabs: true),
    ];

    // The mercenary's three: weapon and gloves, helm armour and belt, off-hand and boots. No
    // tabs; the same rule for the bottom.
    private const int MercBottom = 2 * Cell + Gap + 3 * Cell + Gap + Cell;

    public static double MercPanelWidth => 3 * Wide;
    public static double MercPanelHeight => MercBottom;

    /// <summary>
    /// What a mercenary can wear. <b>Seven slots in PD2, not vanilla's three.</b>
    /// <para>
    /// The boxes are named outright by the <c>Hireling</c> row of <c>inventory.txt</c>:
    /// rArm, lArm, torso, head, belt, feet and gloves all carry real coordinates, and only
    /// the neck and the two ring boxes are -1. They fall into the same three columns the
    /// character's do - weapon and gloves down the left, helm, armour and belt up the
    /// middle, off-hand and boots down the right - which is what the game's own mercenary
    /// panel draws, the gloves and boots ending level with the belt.
    /// </para>
    /// </summary>
    public static IReadOnlyList<EquipSlot> MercSlots { get; } =
    [
        new(ItemLocation.RightHand, L("Weapon"),   2, 4, 0, 0),
        new(ItemLocation.Head,      L("Helm"),     2, 2, Wide, 0),
        new(ItemLocation.LeftHand,  L("Off-hand"), 2, 4, 2 * Wide, 0),

        new(ItemLocation.Torso,     L("Armor"),    2, 3, Wide, 2 * Cell + Gap),

        new(ItemLocation.Gloves,    L("Gloves"),   2, 2, 0, MercBottom - 2 * Cell),
        new(ItemLocation.Waist,     L("Belt"),     2, 1, Wide, MercBottom - Cell),
        new(ItemLocation.Feet,      L("Boots"),    2, 2, 2 * Wide, MercBottom - 2 * Cell),
    ];
}

/// <summary>Where one equipment slot sits and how big a box it gets.</summary>
public sealed record EquipSlot(
    ItemLocation Location,
    string Label,
    int Columns,
    int Rows,
    double Left,
    double Top,
    bool IsSwap = false,
    bool HasSetTabs = false);
