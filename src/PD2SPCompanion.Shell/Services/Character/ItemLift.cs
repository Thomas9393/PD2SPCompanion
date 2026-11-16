using D2SLib.Model.Save;
using System.Collections.Generic;
using System.Linq;

namespace PD2SPCompanion.Shell.Services.Character;

/// <summary>
/// An item a file has let go of, on its way somewhere. Out of every list until it lands, which is the one state a save must
/// never be written in, so a lift says how it ends: <see cref="Done"/>, it stays where it went, or <see cref="Back"/>, it
/// lies as it did. Back asks nothing of a file: a file that refused an item once may refuse it again, and it would then be
/// in no list. There is no call that moves an item from end to end, because what a caller checks between the steps differs
/// at every site; only the steps that lost items when written by hand live here.
/// </summary>
public sealed class ItemLift
{
    private readonly IItemHolder _from;
    private readonly ItemOrigin _origin;
    private readonly uint _id;

    /// <summary>The file it landed in, where it has: it is taken out of there before it goes back.</summary>
    private IItemHolder? _landedIn;

    /// <summary>Done or back: the item is in a list, and going back again would hold it twice.</summary>
    private bool _settled;

    private ItemLift(IItemHolder from, Item item, ItemOrigin origin)
    {
        _from = from;
        _origin = origin;
        _id = item.Id;
        Item = item;
    }

    /// <summary>The item on its way.</summary>
    public Item Item { get; }

    /// <summary>Taken down where it lies, the item still in its list.</summary>
    private static ItemLift? Noted(IItemHolder from, Item item) =>
        from.OriginOf(item) is { } origin ? new ItemLift(from, item, origin) : null;

    /// <summary>The item let go of by its file, and noted first; null where the file does not hold it or will not let go.</summary>
    public static ItemLift? Of(IItemHolder from, Item item) =>
        Noted(from, item) is { } lift && from.Release(item, rebuild: false) is not null ? lift : null;

    /// <summary>
    /// Several let go of, every place noted before any leaves: an index taken after another had gone would be off by it. In
    /// the order given, without those whose file does not hold them or will not let go.
    /// </summary>
    public static List<ItemLift> OfAll(IEnumerable<(IItemHolder From, Item Item)> items)
    {
        var noted = items.Select(each => Noted(each.From, each.Item)).OfType<ItemLift>().ToList();
        return [.. noted.Where(lift => lift._from.Release(lift.Item, rebuild: false) is not null)];
    }

    /// <summary>Taken in by a file, no rebuild. Null when it came, else why not; refused, the lift is still to be settled.</summary>
    public string? Into(IItemHolder to, int page, int x, int y, bool keepId = false)
    {
        string? refused = to.Adopt(Item, page, x, y, rebuild: false, keepId: keepId);
        if (refused is null)
            _landedIn = to;

        return refused;
    }

    /// <summary>It stays where it went, or is gone for good: into a count, a socket. Back then does nothing.</summary>
    public void Done() => _settled = true;

    /// <summary>Every one stays where it went.</summary>
    public static void Done(IEnumerable<ItemLift> lifts)
    {
        foreach (var lift in lifts)
            lift.Done();
    }

    /// <summary>The item as it lay, its identifier too, out of wherever it had landed. Only once; nothing after <see cref="Done"/>.</summary>
    public void Back() => Back([this]);

    /// <summary>
    /// Every one as it lay. All out of where they landed before any goes back, and then the earliest in its list first, so
    /// each index stands and the list is as it was. One whose landing will not let go of it stays there: never held twice.
    /// </summary>
    public static void Back(IEnumerable<ItemLift> lifts)
    {
        var open = lifts.Where(lift => !lift._settled).ToList();
        foreach (var lift in open)
            lift._settled = true;

        var coming = open.Where(lift => lift._landedIn is null || lift._landedIn.Release(lift.Item, rebuild: false) is not null).ToList();
        foreach (var lift in coming.OrderBy(lift => lift._origin.Index))
        {
            lift.Item.Id = lift._id;
            lift._from.PutBack(lift.Item, lift._origin);
        }
    }
}
