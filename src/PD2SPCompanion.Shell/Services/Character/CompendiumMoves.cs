using System.Collections.Generic;
using System.Linq;
using D2SLib.Model.Save;
using PD2SPCompanion.Core.Collection;
using PD2SPCompanion.Core.Items;

namespace PD2SPCompanion.Shell.Services.Character;

/// <summary>
/// Items into the compendium and out of it again, as the game's Uniques and Sets tabs Deposit and Withdraw: the Grail
/// page's arrows on a card, and from the Items page an item's menu and its key, G - anywhere,
/// in the right-click menu too, each of several that can.
/// <para>
/// One item a card, of any of its rows (<see cref="GrailCatalog.CardRowsOf"/>; proven in game with both Arm of King
/// Leoric). An item comes off the character's carried and stored items or the shared stash's pages - not one worn,
/// the mercenary's or on a corpse, nor one in a socket - and is written with the next save, the files together; Undo
/// takes it back. Every play mode: it moves an item and makes none.
/// </para>
/// </summary>
public sealed class CompendiumMoves(CharacterSession session, SharedStashSession stash, CompendiumSession compendium,
                                    ItemLanding landing, GameSync? sync = null)
{
    /// <summary>What the compendium holds has changed - stored into, taken from, read, undone (<see cref="CompendiumSession.Changed"/>).</summary>
    public event System.Action? Changed
    {
        add => compendium.Changed += value;
        remove => compendium.Changed -= value;
    }

    /// <summary>Why the compendium cannot be moved into or out of now, said on the strip ("" where the game says it); null where it can.</summary>
    public string? Blocked(string act)
    {
        if (!session.IsLoaded)
            return L("Require open character.");
        if (session.IsViewingBackup)
            return L("Close the backup first.");
        if (sync?.RefusedWhileRunning(act) == true)
            return "";
        if (!compendium.IsLoaded)
            return compendium.Trouble is { Length: > 0 } trouble ? trouble : L("Compendium not read.");
        return null;
    }

    /// <summary>The item in the compendium on this card - of any of its rows - or null.</summary>
    public Item? Deposited(string kind, IReadOnlyCollection<int> rows) =>
        compendium.Items.FirstOrDefault(item => GrailCatalog.EntryOf(item) is { } entry && entry.Kind == kind && rows.Contains(entry.Row));

    /// <summary>
    /// Why this item cannot go in, or null where it can: no card for it - not a unique or set piece the tabs have - one
    /// already on its card, or where it lies. Worded as a refusal, with no colon: it is a dimmed control's reason too
    /// (a "Keys: what" line is drawn as key caps).
    /// </summary>
    public string? WhyNot(Item item)
    {
        string name = ItemPresenter.Describe(item).Name;
        if (GrailCatalog.EntryOf(item) is not { } entry || GrailCatalog.CardRowsOf(entry.Kind, entry.Row) is not { Count: > 0 } rows)
            return L($"{name} has no place in compendium.");
        if (Deposited(entry.Kind, rows) is not null)
            return L($"{name} already in compendium.");
        if (From(item) is null)
            return session.IsCorpseItem(item) || session.IsMercenaryItem(item) || item.Mode != ItemMode.Stored
                ? L($"Take off {name} first.")
                : L($"{name} not stored.");
        return null;
    }

    /// <summary>Which of the two holds an item that may go in: the character (false) or the shared stash (true); null for neither.</summary>
    private bool? From(Item item)
    {
        if (InSocket(item))
            return null;
        if (stash.HeldItems.Any(each => ReferenceEquals(each, item)))
            return true;
        if (session.HeldItems.Any(each => ReferenceEquals(each, item))
            && item.Mode == ItemMode.Stored && !session.IsMercenaryItem(item) && !session.IsCorpseItem(item))
            return false;
        return null;
    }

    private bool InSocket(Item item) =>
        session.HeldItems.Concat(stash.HeldItems).Any(each => each.SocketedItems.Any(socketed => ReferenceEquals(socketed, item)));

    /// <summary>
    /// These items into the compendium, each that can go: off where it lies, one a card - a second of a card stays
    /// where it is. What happened is said on the strip, the item by name where it is one. The number stored.
    /// </summary>
    public int Store(IReadOnlyList<Item> items)
    {
        if (items.Count == 0)
            return 0;
        if (Blocked(L("store")) is { } blocked)
        {
            if (blocked.Length > 0)
                session.Status = blocked;
            return 0;
        }

        // Which go, and from where: a card taken once, by the first of them.
        var going = new List<(Item Item, bool Shared)>();
        var cards = new HashSet<(string, int)>();
        string? why = null;
        foreach (var item in items)
        {
            if (WhyNot(item) is { } not)
            {
                why ??= not;
                continue;
            }

            var entry = GrailCatalog.EntryOf(item)!.Value;
            if (!cards.Add((entry.Kind, GrailCatalog.CardRowsOf(entry.Kind, entry.Row)[0])))
            {
                why ??= L($"{ItemPresenter.Describe(item).Name} already in compendium.");
                continue;
            }

            going.Add((item, From(item) == true));
        }

        // Off the two quietly, each laid out again once at the end.
        int stored = 0;
        var fromStash = going.Where(each => each.Shared).Select(each => each.Item).ToList();
        var fromCharacter = going.Where(each => !each.Shared).Select(each => each.Item).ToList();
        foreach (var (list, release) in new[]
                 {
                     (fromStash, (System.Func<Item, bool, Item?>)stash.Release),
                     (fromCharacter, (System.Func<Item, bool, Item?>)session.Release),
                 })
        {
            for (int at = 0; at < list.Count; at++)
            {
                if (release(list[at], at == list.Count - 1) is not { } taken)
                {
                    why ??= L($"{ItemPresenter.Describe(list[at]).Name} not stored.");
                    continue;
                }

                compendium.Store(taken);
                stored++;
            }
        }

        session.Status = (stored, items.Count) switch
        {
            (0, _) => why ?? L("Nothing stored."),
            (1, 1) => L($"{ItemPresenter.Describe(going[0].Item).Name} stored in compendium."),
            var (one, all) when one == all => L($"{one} stored in compendium."),
            var (some, all) => L($"{some} of {all} stored in compendium."),
        };
        return stored;
    }

    /// <summary>
    /// An item out of the compendium, as the game's Withdraw: into the first room the pages shown and the character have
    /// (ItemLanding), its identifier kept. Refused, and left in, with no room. Whether it came out.
    /// </summary>
    public bool TakeOut(Item item)
    {
        if (Blocked(L("take out")) is { } blocked)
        {
            if (blocked.Length > 0)
                session.Status = blocked;
            return false;
        }

        if (landing.WithoutRoom([item], null).Count > 0)
        {
            session.Status = L("No free space.");
            return false;
        }

        if (!compendium.TakeOut(item))
            return false;

        var (placed, _, _, refused) = landing.Welcome([item]);
        if (placed == 0)
        {
            // Never in neither: back in, last.
            compendium.Store(item);
            session.Status = refused ?? L("No free space.");
            return false;
        }

        session.Status = L($"{ItemPresenter.Describe(item).Name} taken out of compendium.");
        return true;
    }
}
