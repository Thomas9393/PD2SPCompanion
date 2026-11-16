using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Saves;

/// <summary>
/// The parts of a character's file a walk over its items covers. The file keeps them in separate sections, and what
/// a walk leaves out decides what it can miss, so every caller names its parts.
/// </summary>
[Flags]
public enum ItemScope
{
    /// <summary>The character's own list: worn, belt, inventory, its own stash, cube.</summary>
    Own = 1,

    /// <summary>The mercenary's list.</summary>
    Mercenary = 2,

    /// <summary>What lies on the corpses, each with a list of its own.</summary>
    Corpses = 4,

    /// <summary>The Iron Golem's item, in no list: there only while the golem stands.</summary>
    Golem = 8,

    /// <summary>What sits in the sockets of whatever else is walked, each right after the item it is in.</summary>
    Sockets = 16,
}

/// <summary>The one way to walk the items a character's file holds, or a list of items and what is in their sockets.</summary>
public static class SaveItems
{
    /// <summary>
    /// The items of these parts, in the file's order: own, mercenary, corpses, golem. No save, no items. Not an
    /// iterator: the lists are those of the moment of the call, and read as they stand when enumerated.
    /// </summary>
    public static IEnumerable<Item> Walk(D2S? save, ItemScope scope)
    {
        if (save is null)
            return [];

        IEnumerable<Item> items = [];
        if (scope.HasFlag(ItemScope.Own))
            items = items.Concat(save.PlayerItemList?.Items ?? []);
        if (scope.HasFlag(ItemScope.Mercenary))
            items = items.Concat(save.MercenaryItemList?.ItemList?.Items ?? []);
        if (scope.HasFlag(ItemScope.Corpses))
            items = items.Concat((save.PlayerCorpses?.Corpses ?? []).SelectMany(corpse => corpse.ItemList?.Items ?? []));
        if (scope.HasFlag(ItemScope.Golem) && save.Golem is { Exists: true, Item: { } golem })
            items = items.Append(golem);

        return scope.HasFlag(ItemScope.Sockets) ? items.WithSockets() : items;
    }

    /// <summary>Each item, then what sits in its sockets. A socket's filler has no sockets of its own.</summary>
    public static IEnumerable<Item> WithSockets(this IEnumerable<Item> items) => items.SelectMany(WithSockets);

    /// <summary>This item, then what sits in its sockets.</summary>
    public static IEnumerable<Item> WithSockets(this Item item) => item.SocketedItems.Prepend(item);
}
