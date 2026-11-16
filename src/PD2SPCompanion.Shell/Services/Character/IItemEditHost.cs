using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.ViewModels;


using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace PD2SPCompanion.Shell.Services.Character;

/// <summary>
/// What the item panel (the inventory page's right half) needs from wherever the item it edits
/// lives. The character's session is one host; the vault's Edit page is the other - so the one
/// panel edits both, and the two keep selections of their own.
/// <para>
/// Everything here was already what the panel asked of <see cref="CharacterSession"/>: the item
/// looked at, the facts beside it, a status line, a copy to stage edits on, the one call after
/// KEEP, and the few things that reach past the item - sockets emptied into a container, a
/// craft put in the hand - which a host with no containers refuses.
/// </para>
/// </summary>
public interface IItemEditHost : INotifyPropertyChanged
{
    /// <summary>The item the panel shows. Null shows nothing.</summary>
    ItemTileViewModel? SelectedItem { get; set; }

    /// <summary>
    /// Whether the page beside the panel is picking several items. The panel edits one, so it
    /// says so instead of standing empty.
    /// </summary>
    bool IsPicking { get; }

    /// <summary>How many are picked while <see cref="IsPicking"/>.</summary>
    int PickedCount { get; }

    /// <summary>The short facts under the item's name: code, quality, id, sockets.</summary>
    ObservableCollection<string> SelectedFacts { get; }

    /// <summary>The app's status line.</summary>
    string Status { get; set; }

    /// <summary>The right page's strip, where the item panel says what its actions came to.</summary>
    MessageLog RightMessages { get; }

    /// <summary>A detached copy to work edits out on, or null when one cannot be made.</summary>
    Item? Copy(Item item);

    /// <summary>Called once KEEP has written to the item: the host puts it where it belongs.</summary>
    void MarkEdited();

    /// <summary>Says the selected item was written to, so what is drawn from it is drawn again.</summary>
    void SelectedItemChanged();

    int MaximumSockets(ItemTileViewModel tile);

    /// <summary>The gems that would have nowhere to go if they left their sockets.</summary>
    IReadOnlyList<Item> WithoutRoom(IEnumerable<Item> gems);

    /// <summary>Takes a gem out of its socket and puts it somewhere; null when done, else why not.</summary>
    string? Unsocket(ItemTileViewModel tile, Item gem);

    /// <summary>
    /// Whether a gem may come out of its socket here. The vault cannot - nowhere for it to go - so
    /// the panel offers no unsocketing there, and removes only empty sockets.
    /// </summary>
    bool CanEmptySockets => true;

    /// <summary>
    /// Whether every change is written to the item as it is made, with no KEEP to wait on and nothing to
    /// undo. The Forge first: its item is in no file, so nothing is at stake in writing it, and the holder
    /// beside the panel shows the item as it is. The character's
    /// Edit page and the vault's too, the undo, the redo and the history making an autosave
    /// safe: the autosave writes the file, and the session's history takes an edit
    /// back - KEEP and UNDO were the one place the editor asked for a second act to mean the first.
    /// </summary>
    bool KeepsAtOnce => false;

    /// <summary>
    /// A change to <paramref name="tile"/>'s item has been kept at once: the host names the item to the
    /// session's history, which makes one step of a run of changes to one item - a number stepped up a dozen
    /// times, a second later a line added - and not a step a change. Nothing where nothing is recorded.
    /// </summary>
    void EditStreak(ItemTileViewModel tile)
    {
    }

    /// <summary>
    /// Whether the host's page chose what the item is - its base, its rarity, which unique or runeword. The
    /// Forge: the panel then offers no button that makes the item into another kind (craft, runeword,
    /// superior), since asking for another kind is what the page's own boxes are for.
    /// </summary>
    bool ChoosesItsItem => false;

    /// <summary>
    /// Whether the Horadric Cube's upgrade of the base is offered. Not where the host's page chose the item -
    /// a jewel in its socket - but on the Forge it is: a unique a tier up is on a base
    /// its boxes cannot name, so the upgrade is done to the item in the holder, and the page says so
    /// (ForgeViewModel.UpgradeNote).
    /// </summary>
    bool UpgradesBase => !ChoosesItsItem;

    /// <summary>
    /// Whether the page's reroll is of an item that is not yet anyone's - the Forge - and so rolls it as the
    /// game would make it anew, where an item a character owns is rolled as the game's Demonic Cube rolls one
    /// (VP_ViewModel_Inv_R.Roll): their goals are different.
    /// </summary>
    bool RollsAsDrop => false;

    /// <summary>Raised when the page has written a change into the item: the Forge then knows its item is no longer as made.</summary>
    event System.Action? Edited
    {
        add { }
        remove { }
    }
}

/// <summary>
/// The facts the panel lists under an item's name. One list for both hosts, lifted out of the
/// character session where it was written.
/// </summary>
public static class ItemFacts
{
    /// <summary>
    /// The facts the item's summary carries under its name: its code, its rarity -
    /// a runeword is one, whatever its base's quality says - and, where the item is in a file, its id.
    /// </summary>
    public static IEnumerable<string> Brief(ItemTileViewModel tile, bool withId)
    {
        var item = tile.Item;

        yield return Coded(tile);

        // Not for a materials-tab slot, as below: its tile stands for a count.
        if (!item.IsCompact && tile.TabSlot is null)
        {
            string rarity = L(ItemOrder.Rarities[ItemOrder.Rarity(item)]);
            yield return L($"Rarity: {rarity}");

            if (withId)
                yield return L($"Item ID: {item.Id:X8}");

            // The season before's item, kept as it was ("Legacy · S13").
            if (PD2SPCompanion.Core.Saves.ItemSeason.Mark(item) is { } legacy)
                yield return legacy;
        }

        if (!item.IsIdentified)
            yield return L("Unidentified");
        if (tile.View.Warning is not null)
            yield return $"! {tile.View.Warning}";
    }

    /// <summary>
    /// The item's code with its tier beside it: a base's - Normal, Exceptional, Elite - or a map's own,
    /// "Tier 3" or "Dungeon". A belt's size after it, the potions it holds - "16 slots":
    /// what decides which potions leave when one belt takes another's place, and no
    /// tooltip of the game's says it.
    /// </summary>
    private static string Coded(ItemTileViewModel tile)
    {
        string? tier = ItemTier.Of(tile.Item.Code) ?? MapRecipes.TierName(tile.Item.Code);
        if (System.Linq.Enumerable.Contains(ItemTypes.LineageOfBase(tile.Item.Code), "belt", StringComparer.OrdinalIgnoreCase))
        {
            int slots = PD2SPCompanion.Core.Characters.Belts.BoxesFor(tile.Item.Code);
            return tier is null
                ? L($"Code: {tile.View.Code} ({slots} slots)")
                : L($"Code: {tile.View.Code} ({tier}, {slots} slots)");
        }

        return tier is null ? L($"Code: {tile.View.Code}") : L($"Code: {tile.View.Code} ({tier})");
    }

    public static IEnumerable<string> Of(ItemTileViewModel tile)
    {
        var item = tile.Item;

        yield return Coded(tile);

        // Not for a materials-tab slot: its tile stands for a count, and the item behind it was
        // made to draw it - that quality and that identifier belong to nothing the player has.
        if (!item.IsCompact && tile.TabSlot is null)
        {
            yield return L($"Quality: {item.Quality}");

            // The item's own 32-bit identifier, in hex as every tool that shows it does. It is
            // read in the same part of the record the quality is, so a simple item has no
            // meaningful one and is not asked.
            yield return L($"Item ID: {item.Id:X8}");
        }

        if (item.IsSocketed)
            yield return L($"Sockets: {item.NumberOfSocketedItems} of {item.TotalNumberOfSockets}");
        if (item.IsEthereal)
            yield return L("Ethereal");
        if (!item.IsIdentified)
            yield return L("Unidentified");
        if (tile.View.Warning is not null)
            yield return $"! {tile.View.Warning}";
    }
}
