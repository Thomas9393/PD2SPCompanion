using System;
using System.Collections.Generic;

using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Services.Generic;

/// <summary>One line of an item tooltip's control strip: the key, and what it does with the item here.</summary>
public sealed record TipHint(string Keys, string Does);

/// <summary>
/// What an item's tooltip says can be done with the item, and what it would be compared with.
/// </summary>
/// <param name="Hints">The strip's lines; none where nothing can be done.</param>
/// <param name="Offers">Whether the item goes on someone - the comparison is then there to be opened (Tab).</param>
/// <param name="Compared">What putting the item on would take off as things stand - one piece, two for a weapon held in both hands over a weapon and a shield - or none: the slot is empty.</param>
/// <param name="Caption">Whose slot it is, over the comparison: "Equipped - Ring", "Mercenary - Weapon".</param>
/// <param name="Empty">What the comparison says of an empty slot.</param>
public sealed record TipInfo(IReadOnlyList<TipHint> Hints, bool Offers, IReadOnlyList<ItemTileViewModel> Compared, string Caption, string Empty);

/// <summary>
/// What the item tooltips of the Items page share:
/// whether the comparison is open, which slot and whose it shows, and who says what can be done with an item (the
/// main view model, which knows which pages are up). Static, as the tooltips are made by templates all over the
/// page and one window has the keys.
/// <para>
/// <b>The comparison's keys</b>. Open, the comparison shows what is worn where the item
/// would go, and Alt and Ctrl alone change that - Alt to the next slot that takes it, Ctrl between the character
/// and the mercenary. (A right click opens the item's menu, whose rows name each place: where the comparison
/// shows, the other slot, the mercenary.)
/// </para>
/// <para>
/// <b>Tab opens and shuts the comparison</b>, and it stays as left - a tap, not a hold: Shift, the usual key,
/// was the right click's "to the vault". Tab is taken only while a tooltip that offers it is up; elsewhere it
/// walks the window as ever. The slot and whose are the hover's alone: another item, and they are the first again.
/// </para>
/// </summary>
public static class TipActions
{
    /// <summary>Where an item is: a grid of the inventory page's, a worn slot (the character's or the mercenary's), a vault card.</summary>
    public const string Grid = "grid", Worn = "worn", Vault = "vault";

    /// <summary>Who says what can be done with an item where it is, as this class has the comparison: (item, origin).</summary>
    public static Func<ItemTileViewModel, string, TipInfo?>? Provider { get; set; }

    /// <summary>The comparison opened or shut, to be kept (the settings).</summary>
    public static Action<bool>? Keep { get; set; }

    /// <summary>Whether the comparison is open: a second box of what is worn where the click would put the item.</summary>
    public static bool Compares { get; set; }

    /// <summary>With the comparison open: which of the slots that take the item it shows, counted on by Alt.</summary>
    public static int Slot { get; private set; }

    /// <summary>With the comparison open: whether it shows the mercenary's slot, turned over by Ctrl.</summary>
    public static bool Mercenary { get; private set; }

    /// <summary>The comparison opened or shut, or its slot or whose changed: the tooltip that is up draws itself again.</summary>
    public static event Action? Changed;

    /// <summary>The tooltips up, and whether each offers the comparison.</summary>
    private static readonly Dictionary<object, bool> _up = new(ReferenceEqualityComparer.Instance);

    /// <summary>The item the tooltip that is up is of.</summary>
    private static ItemTileViewModel? _over;

    /// <summary>
    /// Whether the tooltip that is up offers the comparison - its item goes on someone: Tab is its key then, and
    /// only then. Over any other item Tab walks the window as ever.
    /// </summary>
    public static bool CanCompare => _up.ContainsValue(true);

    /// <summary>Whether the comparison is in sight: open, over an item that offers it. Alt and Ctrl are its keys then.</summary>
    public static bool IsShowing => Compares && CanCompare;

    /// <summary>A tooltip up, over an item: another item than the last, and the slot and whose are the first again.</summary>
    public static void Opened(object tip, ItemTileViewModel? tile)
    {
        _up.TryAdd(tip, false);
        if (!ReferenceEquals(_over, tile))
        {
            _over = tile;
            (Slot, Mercenary) = (0, false);
        }
    }

    public static void Closed(object tip)
    {
        _up.Remove(tip);
        _scrolling.Remove(tip);
        if (_up.Count == 0)
        {
            _over = null;
            (Slot, Mercenary) = (0, false);
        }
    }

    /// <summary>The tooltips up that are cut to the screen and scroll.</summary>
    private static readonly HashSet<object> _scrolling = new(ReferenceEqualityComparer.Instance);

    /// <summary>[ or ] pressed over a tooltip that scrolls: -1 up, 1 down.</summary>
    public static event Action<int>? Scrolled;

    /// <summary>A tooltip that is up says whether it scrolls: [ and ] are its keys then, and only then.</summary>
    public static void Scrolls(object tip, bool scrolls)
    {
        if (scrolls && _up.ContainsKey(tip))
            _scrolling.Add(tip);
        else
            _scrolling.Remove(tip);
    }

    /// <summary>[ or ]: the tooltip that is up scrolled a step. False where none scrolls - the key is then whatever it was.</summary>
    public static bool Scroll(int direction)
    {
        if (_scrolling.Count == 0)
            return false;

        Scrolled?.Invoke(direction);
        return true;
    }

    /// <summary>A tooltip that is up says whether it offers the comparison, each time it draws itself.</summary>
    public static void Offers(object tip, bool something)
    {
        if (_up.ContainsKey(tip))
            _up[tip] = something;
    }

    public static TipInfo? Describe(ItemTileViewModel tile, string origin)
    {
        try
        {
            return Provider?.Invoke(tile, origin);
        }
        catch (Exception problem)
        {
            // A tooltip that cannot say what it could do still shows its item.
            PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
            return null;
        }
    }

    /// <summary>Tab: the comparison open, or shut, and kept so.</summary>
    public static void Toggle()
    {
        Compares = !Compares;
        (Slot, Mercenary) = (0, false);
        Keep?.Invoke(Compares);
        Changed?.Invoke();
    }

    /// <summary>Alt, with the comparison in sight: the next slot that takes the item. False where it is not in sight.</summary>
    public static bool Cycle()
    {
        if (!IsShowing)
            return false;

        Slot++;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Ctrl, with the comparison in sight: the mercenary's slot, or the character's again. False where it is not in sight.</summary>
    public static bool SwitchWhose()
    {
        if (!IsShowing)
            return false;

        (Slot, Mercenary) = (0, !Mercenary);
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// The slot and whose as the comparison really shows them - the provider's to say: the count comes round to
    /// the first, and a mercenary who cannot wear the item is not turned to. Told to nobody: it is what was drawn.
    /// </summary>
    public static void Settle(bool mercenary, int slot) => (Mercenary, Slot) = (mercenary, slot);

}
