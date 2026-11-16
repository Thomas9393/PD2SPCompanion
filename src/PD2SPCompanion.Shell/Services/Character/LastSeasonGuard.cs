using D2SLib.Model.Save;
using System.Collections.Generic;
using System.Linq;

namespace PD2SPCompanion.Shell.Services.Character;

/// <summary>
/// Nothing from another file comes into a file read in last season's encoding: it is written back the same way, and an item from this
/// season's vault, character or stash would be written in last season's encoding too, where a value grown since
/// may not fit its field. So it only takes back what it held: its own items may leave and return.
/// <para>
/// <b>Only until this season's item stamp is known.</b> The game never rewrites an old file - it reads each item by
/// its own stamp, and keeps S13 items beside S14's - and so does the parser once both seasons' stamps
/// are known (<c>Globals.ItemEra</c>): a file then reads as this season's, whatever it holds, and nothing is
/// refused. The stamp is learned from a character of this season opened in the editor - which is what the refusal
/// asks for. "Require one game session", as it said, never ended it.
/// </para>
/// </summary>
public static class LastSeasonGuard
{
    /// <summary>The items a file held when read, while it is in last season's encoding; null when it is not.</summary>
    public static IReadOnlySet<Item>? Own(bool lastSeason, IEnumerable<Item> items) =>
        lastSeason
            ? new HashSet<Item>(items.WithSockets(), ReferenceEqualityComparer.Instance)
            : null;

    /// <summary>
    /// The items a character's file held when read, all of them: what lies on a corpse or the golem is its own too, and
    /// comes back to the character, so it is no arrival from elsewhere.
    /// </summary>
    public static IReadOnlySet<Item>? OwnOf(bool lastSeason, D2S save) =>
        Own(lastSeason, SaveItems.Walk(save, ItemScope.Own | ItemScope.Mercenary | ItemScope.Corpses | ItemScope.Golem));

    /// <summary>What to say to an item arriving from elsewhere at a last-season file; null where it may come.</summary>
    public static string? Refuses(IReadOnlySet<Item>? own, Item item, string file) =>
        own is not null && !own.Contains(item)
            ? L($"{file} read as last season's. Require one character of this season opened.")
            : null;
}
