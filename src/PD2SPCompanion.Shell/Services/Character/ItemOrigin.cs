using D2SLib.Model.Save;

namespace PD2SPCompanion.Shell.Services.Character;

/// <summary>
/// Where an item lies, taken down before a file lets go of it, so that a move refused after that can put it back as it
/// was (<see cref="CharacterSession.PutBack"/>, <see cref="SharedStashSession.PutBack"/>). Every field the place is
/// written in, not only a page and a cell: a shared item's page is in <see cref="Location"/>, its <see cref="Page"/> the
/// same for every page of the file; a worn item's slot is in <see cref="Location"/> and its list may be the mercenary's;
/// and a save's order is what says which of two weapons went on first.
/// </summary>
public readonly record struct ItemOrigin(ItemList List, int Index, ItemMode Mode, ItemLocation Location, byte Page, byte X, byte Y)
{
    /// <summary>The place of an item in the list that holds it.</summary>
    public static ItemOrigin Of(Item item, ItemList list) =>
        new(list, list.Items.IndexOf(item), item.Mode, item.Location, item.Page, item.X, item.Y);

    /// <summary>The item back in its list, where it stood, and written as it lay. Never asks: it was this file's.</summary>
    public void Restore(Item item)
    {
        (item.Mode, item.Location, item.Page, item.X, item.Y) = (Mode, Location, Page, X, Y);
        List.Items.Insert(System.Math.Clamp(Index, 0, List.Items.Count), item);
    }
}
