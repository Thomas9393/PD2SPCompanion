using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Collection;

/// <summary>
/// One move across files that a save is about to write, as its journal has it. <see cref="Kind"/> is
/// <see cref="MoveRecovery.Store"/>, <see cref="MoveRecovery.Withdraw"/> or
/// <see cref="MoveRecovery.Gold"/>. Each side is a count - copies of the item in the file and
/// the vault, the purse's gold - before the save and after it, so
/// what is found later tells how far the save got. Compact items have no id; counting copies
/// covers them too. Gold's vault side is the count of writes of the vault's gold
/// (<see cref="Vault.GoldWrites"/>): reached once it stands at <see cref="VaultAfter"/> or over.
/// </summary>
public sealed record PendingMove(
    string Kind,
    string Path,
    string Character,
    string Name,
    uint? ItemId,
    string Code,
    byte[]? Blob,
    string Collection,
    long? VaultKey,
    long? FolderId,
    string Purse,
    long Amount,
    long FileBefore,
    long FileAfter,
    long VaultBefore,
    long VaultAfter)
{
    public long Batch { get; init; }

    /// <summary>Its row in the journal, once read from it.</summary>
    public long Id { get; init; }

    public DateTimeOffset At { get; init; }
}

/// <summary>What a file holds, as a journal counts it: copies of an item, and a purse's gold.</summary>
public sealed class SaveCounts
{
    private readonly List<Item> _items;
    private readonly Func<string, long> _purse;

    private SaveCounts(List<Item> items, Func<string, long> purse)
    {
        _items = items;
        _purse = purse;
    }

    /// <summary>A character's items - its own and its mercenary's - and its two purses.</summary>
    public static SaveCounts Of(D2S save)
    {
        var stats = save.Attributes.Stats;
        return new SaveCounts([.. SaveItems.Walk(save, ItemScope.Own | ItemScope.Mercenary)], purse => purse switch
        {
            MoveRecovery.CarriedPurse => stats.GetValueOrDefault(AttributeEditor.Carried),
            MoveRecovery.StashedPurse => stats.GetValueOrDefault(AttributeEditor.Stashed),
            _ => 0,
        });
    }

    /// <summary>The shared stash's items and its purse.</summary>
    public static SaveCounts Of(SharedStash stash) =>
        new([.. stash.Items.Items], purse => purse == MoveRecovery.SharedPurse ? stash.Gold : 0);

    /// <summary>
    /// A file as it is on disk: a character, or else the shared stash. Reading a character
    /// installs its layout - the caller puts its own back.
    /// </summary>
    public static SaveCounts Read(string path) =>
        path.EndsWith(".d2s", StringComparison.OrdinalIgnoreCase)
            ? Of(SaveVerifier.Load(path).Save)
            : Of(SharedStash.Read(path));

    /// <summary>Copies of an item: by id, or - a compact item - by code.</summary>
    public long Count(uint? id, string code) =>
        id is { } wanted
            ? _items.Count(item => !item.IsCompact && item.Id == wanted)
            : _items.Count(item => item.IsCompact && item.Code.Trim() == code.Trim());

    public long Purse(string purse) => _purse(purse);
}

/// <summary>
/// Puts right a save that did not finish, from the moves its journal left. The rule: a duplicate is acceptable,
/// a loss is not. So this only ever adds - an item or gold found in neither place goes back to
/// the vault - and a duplicate is left where it is and said.
/// <para>
/// A save writes the vault's gains before the files and its losses after them, so a save cut
/// short should only ever leave a duplicate. A loss is put right all the same, should the order
/// ever be broken.
/// </para>
/// </summary>
public static class MoveRecovery
{
    public const string Store = "store";
    public const string Withdraw = "withdraw";
    public const string Gold = "gold";

    /// <summary>The purses, as the gold log names them.</summary>
    public const string CarriedPurse = "inventory";
    public const string StashedPurse = "stash";
    public const string SharedPurse = "shared";

    /// <summary>
    /// Checks every move <paramref name="vault"/>'s journal holds and clears it. Returns what was found,
    /// one line each, for the message strip.
    /// </summary>
    public static IReadOnlyList<string> Recover(Vault vault)
    {
        var said = new List<string>();
        var moves = vault.Journal();
        if (moves.Count == 0)
            return said;

        var files = new Dictionary<string, SaveCounts?>(StringComparer.OrdinalIgnoreCase);

        // A file there but not read - the game writing it this moment, say: its moves are kept for the next start,
        // not cleared unchecked. A file gone is cleared: nothing will read it again.
        var unread = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        SaveCounts? FileOf(string path)
        {
            if (!files.TryGetValue(path, out var counts))
            {
                try
                {
                    counts = File.Exists(path) ? SaveCounts.Read(path) : null;
                }
                catch (Exception)
                {
                    counts = null;
                    unread.Add(path);
                }

                files[path] = counts;
            }

            return counts;
        }

        // What could not be put back stays in the journal, the only copy of it: a later build may read it.
        var notPutBack = new HashSet<long>();

        // Read once, as the cut-short save left it: a purse's gold put back below counts as a write of its own, and
        // the next purse read it as its own save's, its gold gone with no word.
        long goldWrites = vault.GoldWrites();
        foreach (var move in moves)
        {
            bool putBack = true;
            string? line = move.Kind == Gold
                ? RecoverGold(vault, move, FileOf(move.Path), goldWrites)
                : RecoverItem(move, FileOf(move.Path), vault, out putBack);
            if (line is not null)
                said.Add(line);
            if (!putBack)
                notPutBack.Add(move.Id);
        }

        // One move at a time, not its batch: a move checked is done with. Kept for the sake of another in its batch, it
        // would be put right again at every start - a row put back has a new key, gold paid back leaves the file as it was.
        foreach (var move in moves)
        {
            if (!notPutBack.Contains(move.Id) && !unread.Contains(move.Path))
                vault.ClearJournalMove(move.Id);
        }

        return said;
    }

    private static string FileName(PendingMove move) =>
        move.Path.EndsWith(".d2s", StringComparison.OrdinalIgnoreCase) ? move.Character : L("shared stash");

    /// <summary>Where each side stands: moved, as it was, or neither (something else changed it since).</summary>
    private static (bool Moved, bool Stayed) Side(long now, long before, long after) =>
        before == after ? (true, false) : (now == after, now == before);

    private static string? RecoverItem(PendingMove move, SaveCounts? file, Vault vault, out bool putBack)
    {
        putBack = true;
        if (file is null)
            return L($"Save cut short: {move.Name} not checked.");

        var (fileMoved, fileStayed) = Side(file.Count(move.ItemId, move.Code), move.FileBefore, move.FileAfter);

        long vaultNow = move.Kind == Withdraw && move.VaultKey is { } key
            ? (vault.HasRow(key) ? 1 : 0)
            : vault.CountHeld(move.ItemId, move.Blob);
        var (vaultMoved, vaultStayed) = Side(vaultNow, move.VaultBefore, move.VaultAfter);

        // Into the vault: the file lets go, the vault takes it. Out of it: the other way.
        bool lost = move.Kind == Store ? fileMoved && vaultStayed : vaultMoved && fileStayed && move.FileBefore != move.FileAfter;
        bool both = move.Kind == Store ? vaultMoved && fileStayed && move.FileBefore != move.FileAfter : fileMoved && vaultStayed;

        if (both)
            return L($"Save cut short: {move.Name} in both {FileName(move)} and vault.");

        if (!lost)
            return null;

        if (move.Blob is null)
            return L($"Save cut short: {move.Name} not in {FileName(move)} or vault.");

        var row = new VaultRow(0, move.Blob, move.ItemId, move.At, move.Name, 0, 0, move.FolderId);
        putBack = vault.PutBack(row, out string? refused) is not null;
        return putBack
            ? L($"Save cut short: {move.Name} put back in vault.")
            : L($"Save cut short: {move.Name} not put back: {refused}.");
    }

    private static string? RecoverGold(Vault vault, PendingMove move, SaveCounts? file, long goldWrites)
    {
        if (file is null)
            return L($"Save cut short: {Math.Abs(move.Amount):N0} gold not checked.");

        var (fileMoved, fileStayed) = Side(file.Purse(move.Purse), move.FileBefore, move.FileAfter);
        // The vault's side by the writes of its gold: this save's reached, or not yet.
        bool vaultMoved = goldWrites >= move.VaultAfter, vaultStayed = !vaultMoved;

        // Into the vault (positive): the purse pays first in the plan's terms, but the vault is
        // written first. Out of it: the purse is written first.
        bool into = move.Amount > 0;
        bool lost = into ? fileMoved && vaultStayed : vaultMoved && fileStayed;
        bool both = into ? vaultMoved && fileStayed : fileMoved && vaultStayed;
        long amount = Math.Abs(move.Amount);

        if (both)
            return L($"Save cut short: {amount:N0} gold in both {PurseName(move)} and vault.");

        if (!lost)
            return null;

        vault.RecordGold([new GoldMove(move.Purse, amount)]);
        return L($"Save cut short: {amount:N0} gold put back in vault.");
    }

    private static string PurseName(PendingMove move) => move.Purse switch
    {
        CarriedPurse => L($"{move.Character}'s inventory"),
        StashedPurse => L($"{move.Character}'s stash"),
        _ => L("shared stash"),
    };
}
