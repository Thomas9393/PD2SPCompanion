using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Character;

namespace PD2SPCompanion.Shell.Services.Vault;

/// <summary>
/// What restoring a character from a backup does besides replacing its file: <b>a restore only adds</b>:
/// never a loss, a duplicate allowed, since a player is upset over a loss and not over a duplicate. The vault and the shared stash are every
/// character's, so neither is rolled back. What the file being replaced holds and the backup does not would
/// vanish with it - so each such thing that is nowhere else is put into the vault first, in a folder named
/// Recovered, and the player is told before and after.
/// <para>
/// <b>From the files in hand at that moment, and nothing kept.</b> It read a history until then - what this
/// character withdrew and stored since the backup, and the vault's own copy of each withdrawn item - which
/// described items after they had left the vault, where the game does with them what it will. The file as
/// it is now, the backup, the vault and the shared stash say all that is needed:
/// </para>
/// <list type="bullet">
///   <item>an item with an id, in the file now and not in the backup, and in neither the vault nor the shared
///   stash: into the vault;</item>
///   <item>runes, gems, whatever the materials tab keeps and every other compact item, by count, whether or not
///   they carry an id: a stack grows under the id it had. Five of a kind now and three in the backup, two go into
///   the vault - which may make a copy, and cannot lose one;</item>
///   <item>gold the character holds over the backup's: into the vault's;</item>
///   <item>an item the backup brings back that was stored in the vault since: left in both, and said.
///   Nothing is taken away.</item>
/// </list>
/// <para>
/// A file that will not read has nothing to compare: it is replaced, as it must be, and the copy of it the
/// restore keeps is all there is of what it held. Not covered: something socketed since the backup into an
/// item the backup has too - the item comes back as it was, and the replaced file's copy holds the rest. Nor
/// a potion or a scroll, whatever a belt takes: the game's small change, and a vault folder full of them is no
/// recovery. The replaced file's copy holds those as well.
/// </para>
/// </summary>
public sealed class RestoreReconcile(CharacterSession character, SharedStashSession stash, VaultService vaults)
{
    /// <summary>What a restore will add to the vault, and what it leaves held twice.</summary>
    /// <param name="Recover">Items with an id that would be nowhere: into the vault.</param>
    /// <param name="Loose">Compact items and stacks the file holds over the backup, a kind and how many more.</param>
    /// <param name="InBoth">Items the backup brings back that the vault holds too, by name.</param>
    /// <param name="Gold">Gold the character holds over the backup's.</param>
    public sealed record Plan(
        IReadOnlyList<Item> Recover,
        IReadOnlyList<(string Code, int More, IReadOnlyList<Item> Held)> Loose,
        IReadOnlyList<string> InBoth,
        long Gold)
    {
        public bool IsEmpty => Recover.Count == 0 && Loose.Count == 0 && InBoth.Count == 0 && Gold == 0;

        /// <summary>What the restore will do, or did, a line each: the dialog's before, the strip's after.</summary>
        public IReadOnlyList<string> Lines(bool done)
        {
            var lines = new List<string>();

            var names = Recover.Select(item => ItemPresenter.Describe(item).Name)
                .Concat(Loose.Select(each => each.More == 1 ? ItemPresenter.BaseName(each.Code) : $"{each.More} {ItemPresenter.BaseName(each.Code)}"))
                .ToList();
            if (names.Count > 0)
            {
                string kept = names.Count <= 3 ? Joined(names) : L($"{names.Count} items");
                lines.Add(done ? L($"Not in backup, put in vault folder Recovered: {kept}.")
                    : L($"Not in backup, goes to vault folder Recovered: {kept}."));
            }

            if (Gold > 0)
                lines.Add(done ? L($"{Gold:N0} gold put in vault.") : L($"{Gold:N0} gold goes to vault."));

            if (InBoth.Count > 0)
            {
                string both = InBoth.Count <= 3 ? Joined(InBoth) : L($"{InBoth.Count} items");
                lines.Add(done ? L($"{both} now in both character and vault.") : L($"{both} will be in both character and vault."));
            }

            return lines;
        }
    }

    /// <summary>What a file holds, a corpse's and the golem's item too: they go with the file as anything else does.</summary>
    private static IEnumerable<Item> Of(D2S save) =>
        SaveItems.Walk(save, ItemScope.Own | ItemScope.Mercenary | ItemScope.Corpses | ItemScope.Golem);

    /// <summary>
    /// A kind counted and not told apart: any stack the materials tab of either season keeps - <c>r08s</c>, and Season
    /// 14's plain <c>r08</c>, <c>gpy</c> and white maps, which stack themselves - or any
    /// compact item - a rune, a gem, a quest item, an ear (only those a socket takes were,
    /// and the others were in no list) - but what a belt takes.
    /// </summary>
    private static bool IsCounted(Item item) =>
        item.Code.Trim() is { Length: > 0 }
        && (item.IsCompact ? !Belts.Takes(item) : Materials.TakesAny(item) is not null);

    /// <summary>What a counted item is counted as: the materials tab's slot where it keeps one, so <c>r08</c> and <c>r08s</c> are one kind; else its code.</summary>
    private static string KindOf(Item item) =>
        Materials.TakesAny(item) is { } taken ? "#" + taken.Slot : item.Code.Trim().ToLowerInvariant();

    /// <summary>How many of its kind an item is: a stack's quantity, or one.</summary>
    private static int Units(Item item) => item.IsCompact ? 1 : Math.Max(1, (int)item.Quantity);

    /// <summary>
    /// What restoring the backup on screen would add to the vault; null where there is no backup on screen,
    /// no vault, or the file being replaced will not read. Nothing is written.
    /// </summary>
    public Plan? PlanFor()
    {
        if (!character.IsViewingBackup || character.CharacterPath is not { } live || !File.Exists(live)
            || vaults.Store is not { } store)
        {
            return null;
        }

        try
        {
            // The three things in hand: the file as it is, the backup on screen, the shared stash as it will
            // stand. Reading a character installs its layout: the session's is put back after each.
            var now = SaveVerifier.Load(live).Save;
            character.UseMyLayout();
            var backup = D2SLib.Core.ReadD2S(character.SavedBytes);
            var shared = stash.SavedBytes is { } stashBytes ? SharedStash.Read(stashBytes, stash.FileName) : null;
            character.UseMyLayout();

            static bool HasId(Item item) => !item.IsCompact && item.Id != 0;

            var inBackup = Of(backup).WithSockets().Where(HasId).Select(item => item.Id).ToHashSet();
            var inStash = shared is null ? [] : shared.Items.Items.WithSockets().Where(HasId).Select(item => item.Id).ToHashSet();
            bool InVault(uint id) => store.CountHeld(id, null) > 0;

            // With an id: in the file now, not in the backup, in neither the vault nor the stash.
            var recover = Of(now)
                .Where(item => HasId(item) && !IsCounted(item) && !inBackup.Contains(item.Id) && !inStash.Contains(item.Id) && !InVault(item.Id))
                .ToList();

            // Without one, by count: what the file holds over the backup, a kind at a time.
            var loose = new List<(string, int, IReadOnlyList<Item>)>();
            foreach (var kind in Of(now).Where(IsCounted).GroupBy(KindOf))
            {
                int was = Of(backup).Where(item => IsCounted(item) && KindOf(item) == kind.Key).Sum(Units);
                int more = kind.Sum(Units) - was;
                if (more > 0)
                {
                    // A stack first where the kind holds both forms: one stack of it takes the whole count.
                    var held = kind.OrderBy(item => item.IsCompact).ToList();
                    loose.Add((held[0].Code.Trim(), more, held));
                }
            }

            // What the backup brings back that the vault holds: in both once it is back. Only what the
            // restore itself makes so - an item the character holds now as well was in both before it, and
            // is nothing this restore does (the pad's six said so at every restore, until this was asked).
            var inNow = Of(now).WithSockets().Where(HasId).Select(item => item.Id).ToHashSet();
            var both = Of(backup).Where(item => HasId(item) && !inNow.Contains(item.Id) && InVault(item.Id))
                .Select(item => ItemPresenter.Describe(item).Name).Distinct().ToList();

            long Purses(D2S save)
            {
                var counts = SaveCounts.Of(save);
                return counts.Purse(MoveRecovery.CarriedPurse) + counts.Purse(MoveRecovery.StashedPurse);
            }

            return new Plan(recover, loose, both, Math.Max(0, Purses(now) - Purses(backup)));
        }
        catch (Exception problem)
        {
            // A file that will not read: nothing to compare, and the restore goes on without a plan.
            Log.Caught(problem);
            return null;
        }
        finally
        {
            character.UseMyLayout();
        }
    }

    /// <summary>
    /// Puts into the vault what the plan recovers, before the character is written - a gain of the vault's, so
    /// a failure after it leaves a copy, never a loss. Returns why something did not go in.
    /// </summary>
    public IReadOnlyList<string> Apply(Plan plan)
    {
        if (plan.Recover.Count == 0 && plan.Loose.Count == 0 && plan.Gold == 0)
            return [];

        if (vaults.Store is not { } store)
            return [L("Vault not open: nothing put in it.")];

        var refused = new List<string>();
        try
        {
            long? folder = null;
            if (plan.Recover.Count > 0 || plan.Loose.Count > 0)
            {
                string name = L("Recovered");
                folder = store.Folders().FirstOrDefault(each => each.ParentId is null && each.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Id
                         ?? store.CreateFolder(null, name);
            }

            void Put(Item item, string named)
            {
                if (store.Add(item, folder, out string? why) is null)
                    refused.Add(L($"{named} not put in vault: {why}."));
            }

            foreach (var item in plan.Recover)
                Put(item, ItemPresenter.Describe(item).Name);

            foreach (var (code, more, held) in plan.Loose)
            {
                string named = ItemPresenter.BaseName(code);
                if (held[0].IsCompact)
                {
                    // Loose ones: that many of those the file holds.
                    foreach (var item in held.Take(more))
                        Put(item, named);
                }
                else
                {
                    // A stack: one of its kind, holding what the file has over the backup.
                    Put(ItemForge.Material(code, more, held[0].Version ?? ""), named);
                }
            }

            if (plan.Gold > 0)
                store.RecordGold([new GoldMove(MoveRecovery.CarriedPurse, plan.Gold)]);
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            refused.Add(L($"Not all put in vault: {problem.Message}"));
        }
        finally
        {
            character.UseMyLayout();
        }

        vaults.SayChanged();
        return refused;
    }
}
