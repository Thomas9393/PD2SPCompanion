using D2SLib.Model.Save;
using PD2SPCompanion.Core.Characters;

namespace PD2SPCompanion.Core;

/// <summary>
/// A character of the season before, brought into the installed one <b>in place</b>: its name, quests, waypoints, mercenary,
/// statistics and corpse stay as they are, and two things move.
/// <list type="number">
/// <item><b>Gold</b>: its carried gold and its own stash's, into the shared stash's - the first load in Season 14 adds
/// the carried gold and <b>drops</b> the stash's (ShockSacri's 125,338).</item>
/// <item><b>Base life, mana and stamina</b>: moved by exactly what the new class table gives otherwise for its class,
/// level and points (<see cref="NewCharacter.Vitals"/> under each season's tables) - what a respec would give, the
/// points kept where they are. S14 changed mana per energy for five classes and per level for three. The old season's
/// table is its snapshot, or with none - a player who starts at this season - the values shipped with the program
/// (<see cref="SeasonSnapshot.KnownClassValues"/>).</item>
/// </list>
/// <b>Its items stay as they are</b> - its own, its mercenary's, its corpse's, its golem's, and what is socketed in them:
/// legacy items (<see cref="Saves.ItemSeason"/>), deliberately not migrated. The game never
/// rewrites an old item; it reads each by its own stamp. Only what it does to one on its first load is done here too: a
/// runeword's id numbered as this season numbers it. Asked for, dry run first.
/// </summary>
public static class CharacterMigration
{
    /// <param name="SavePath">The character's <c>.d2s</c>.</param>
    /// <param name="StashPath">The shared stash its gold goes to - the mode's own; null to leave the gold.</param>
    public sealed record Request(string SavePath, string? StashPath, bool DryRun);

    /// <summary>What was done, or a dry run's what would be.</summary>
    public sealed record Report(
        string Name, string From, string To, bool DryRun,
        IReadOnlyList<SeasonMigration.ItemReport> Items,
        long GoldMoved, long GoldLeft,
        (int Life, int Mana, int Stamina) Was, (int Life, int Mana, int Stamina) Now,
        IReadOnlyList<string> Backups)
    {
        public int Count(SeasonMigration.Outcome outcome) => Items.Count(item => item.Outcome == outcome);

        /// <summary>The report as a person reads it.</summary>
        public string Text()
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine(DryRun
                ? L($"Dry run - nothing written. {Name}: {From} to {To}.")
                : L($"{Name}: {From} to {To}."));
            text.AppendLine(L($"Items: {Items.Count}, kept as they are (legacy {From}); runewords renumbered {Count(SeasonMigration.Outcome.Adjusted)}"));
            text.AppendLine(GoldLeft > 0
                ? L($"Gold: {GoldMoved:N0} to the shared stash, {GoldLeft:N0} left on the character (the shared stash is full).")
                : L($"Gold: {GoldMoved:N0} to the shared stash."));
            text.AppendLine(L($"Life {Was.Life} -> {Now.Life}, mana {Was.Mana} -> {Now.Mana}, stamina {Was.Stamina} -> {Now.Stamina} (base)."));
            foreach (var item in Items.Where(item => item.Outcome != SeasonMigration.Outcome.Clean))
                text.AppendLine($"  {item.Collection} / {item.Name}: {string.Join("; ", item.Why)}");
            foreach (string backup in Backups)
                text.AppendLine(L($"Backup: {backup}"));
            return text.ToString();
        }
    }

    /// <summary>
    /// Whether a character has anything to migrate, without changing it: gold the new season would drop - on one this
    /// season has not played yet - or, for one of the season before, which holds its items, base life, mana or stamina
    /// that are not yet this season's. False for a character of this season, gold or none, and for one migrated already.
    /// One whose base was edited off both seasons' stays offered, its migration moving nothing of it. With the season
    /// before's class table known neither by a snapshot nor by the program, the base cannot move: gold alone offers it.
    /// </summary>
    public static bool Waits(D2S save, GameFiles.GameData? game)
    {
        if (Saves.SeasonReading.PreviousStamp is not { } previous)
            return false;

        var stamps = Everything(save).Select(each => each.Item.Version).Where(stamp => stamp is { Length: > 0 }).ToHashSet();
        bool holdsOld = stamps.Contains(previous);

        // Gold only where this season has not played the character yet - its first load moves the carried gold and drops
        // the stash's, so after it there is nothing to rescue: its items all the season before's, or, holding none, its
        // base not yet this season's. Gold alone kept a character of this season offered for good - one
        // deleted in S13 and made again in S14 under the same name, carrying 99,288.
        bool unplayed = holdsOld
            ? Saves.SeasonReading.CurrentStamp is not { } current || !stamps.Contains(current)
            : stamps.Count == 0 && game is not null && !BaseIsNew(game, save);
        if (unplayed && Gold(save, AttributeEditor.Carried) + Gold(save, AttributeEditor.Stashed) > 0)
            return true;

        return game is not null && holdsOld && OldClassesKnown() && !BaseIsNew(game, save);
    }

    /// <summary>Whether the season before's class table can be had: shipped, or in its snapshot.</summary>
    private static bool OldClassesKnown()
    {
        string from = D2SLib.Model.TXT.StatEncoding.PreviousSeason;
        return SeasonSnapshot.KnownClassValues.ContainsKey(from) || SeasonSnapshot.Read(SeasonSnapshot.FolderFor(from)) is not null;
    }

    /// <summary>Whether the character's base life, mana and stamina are what this season's class table gives it.</summary>
    private static bool BaseIsNew(GameFiles.GameData game, D2S save)
    {
        var now = BaseOf(game, save);
        bool Is(string most, int value, int[] gifts) =>
            gifts.Any(gift => AttributeScale.Display(most, Raw(save, most)) == value + gift);

        return Is("maxhp", now.Life, LifeGifts) && Is("maxmana", now.Mana, [0]) && Is("maxstamina", now.Stamina, [0]);
    }

    /// <summary>One character: what was done, or why not. <see cref="Run"/> throws the refusal instead.</summary>
    public static Report Run(Season installed, Request request)
    {
        var done = RunAll(installed, [request])[0];
        return done.Report ?? throw new InvalidOperationException(done.Refused);
    }

    /// <summary>A request's outcome: its report, or why it was not done.</summary>
    public sealed record Done(Request Request, Report? Report, string? Refused);

    /// <summary>A character read, with what its migration needs from the old season's tables.</summary>
    private sealed class Loaded(Request request, D2S save, List<(string Place, Action<Item> Put, Item Item)> old)
    {
        public Request Request { get; } = request;
        public D2S Save { get; } = save;
        public List<(string Place, Action<Item> Put, Item Item)> Old { get; } = old;
        public (int Life, int Mana, int Stamina) OldBase { get; set; }
    }

    /// <summary>
    /// Several characters at once - the Settings panel's every card: the old season's tables opened <b>once</b> for all of
    /// them and this season's put back once (0.6 s a time - six characters, one at a time, held the
    /// panel's opening some four seconds). Their shared stash is one in memory, each character's gold added to what the
    /// one before left, and written once. Every file is prepared and read back before any is replaced.
    /// </summary>
    public static IReadOnlyList<Done> RunAll(Season installed, IReadOnlyList<Request> requests)
    {
        if (Saves.SeasonReading.CurrentStamp is null || Saves.SeasonReading.PreviousStamp is not { } previous)
            throw new InvalidOperationException(L("Item versions unknown. Require one character of this season opened."));
        if (Saves.GameProcess.IsRunningFrom(installed.Root))
            throw new InvalidOperationException(L("Game running. Require game closed."));

        string to = SeasonSnapshot.NameFor(installed);
        string from = D2SLib.Model.TXT.StatEncoding.PreviousSeason;

        // ---- read every character --------------------------------------------------------------------------------------
        var loaded = new List<Loaded>();
        var done = new Dictionary<Request, Done>();
        foreach (var request in requests)
        {
            try
            {
                var (save, _) = Saves.SaveVerifier.Load(request.SavePath);
                loaded.Add(new Loaded(request, save, [.. Everything(save).Where(each => each.Item.Version == previous)]));
            }
            catch (Exception problem) when (problem is InvalidOperationException or InvalidDataException or IOException)
            {
                done[request] = new Done(request, null, problem.Message);
            }
        }

        // ---- the old season's view, once: each character's base stats -------------------------------------------------
        string? oldTables = SeasonSnapshot.Read(SeasonSnapshot.FolderFor(from)) is not null ? SeasonSnapshot.FolderFor(from) : null;
        if (oldTables is not null && loaded.Count > 0)
        {
            (string? Current, string? Previous) stamps = (Saves.SeasonReading.CurrentStamp, Saves.SeasonReading.PreviousStamp);
            try
            {
                using var then = Season.Open(installed.Root, oldTables, announce: false);
                foreach (var one in loaded)
                    one.OldBase = BaseOf(then.Game, one.Save);
            }
            finally
            {
                // This season's tables back, whatever happened above - and the stamps the run began with, which the
                // reinstall reads again from the snapshots: the legacy items stay at the season before's, and are
                // written by them (Globals.ItemEra).
                installed.Reinstall();
                Saves.SeasonReading.SetStamps(stamps.Current, stamps.Previous);
            }
        }
        else if (oldTables is null && SeasonSnapshot.KnownClassValues.TryGetValue(from, out var classes))
        {
            // No snapshot - a player who starts at this season, and will never have one: the values shipped.
            foreach (var one in loaded)
                one.OldBase = NewCharacter.Vitals(classes[one.Save.ClassId], one.Save.Level, Raw(one.Save, "vitality"), Raw(one.Save, "energy"), 0);
        }

        bool oldKnown = oldTables is not null || SeasonSnapshot.KnownClassValues.ContainsKey(from);

        // ---- each character carried, its gold into the stash of its mode, one stash per file in memory ----------------
        var stashes = new Dictionary<string, Saves.SharedStash>(StringComparer.OrdinalIgnoreCase);
        var written = new List<(Loaded One, byte[] Bytes, Report Report)>();
        foreach (var one in loaded)
        {
            try
            {
                var save = one.Save;
                var newBase = oldKnown ? BaseOf(installed.Game, save) : one.OldBase;
                // Its items as they are, legacy: only a runeword's id numbered as this season's strings number it, which
                // the game's first load would do itself.
                var reports = new List<SeasonMigration.ItemReport>();
                foreach (var (place, _, item) in one.Old.Where(each => !each.Item.IsCompact))
                {
                    var renumbered = Collection.Migration.Renumber(item);
                    reports.Add(new SeasonMigration.ItemReport(place, ItemPresenter.Describe(item).Name, "",
                        renumbered is null ? SeasonMigration.Outcome.Clean : SeasonMigration.Outcome.Adjusted,
                        renumbered is null ? [] : [renumbered.Why]));
                }

                long carried = Gold(save, AttributeEditor.Carried), stashed = Gold(save, AttributeEditor.Stashed);
                long moved = 0;
                Saves.SharedStash? into = null;
                if (one.Request.StashPath is { } path && carried + stashed > 0)
                {
                    if (!stashes.TryGetValue(path, out var stash) && File.Exists(path))
                        stashes[path] = stash = Saves.SharedStash.Read(path);
                    if (stash is not null)
                    {
                        into = stash;
                        moved = Math.Min(carried + stashed, Math.Max(0, stash.MostGold - (long)stash.Gold));
                        long fromStash = Math.Min(stashed, moved);
                        SetGold(save, AttributeEditor.Stashed, stashed - fromStash);
                        SetGold(save, AttributeEditor.Carried, carried - (moved - fromStash));
                    }
                }

                var was = Vitals(save);
                Move(save, "maxhp", "hitpoints", one.OldBase.Life, newBase.Life, LifeGifts);
                Move(save, "maxmana", "mana", one.OldBase.Mana, newBase.Mana, [0]);
                Move(save, "maxstamina", "stamina", one.OldBase.Stamina, newBase.Stamina, [0]);
                var now = Vitals(save);

                byte[] bytes = Saves.SaveWriter.Prepared(save, _ =>
                {
                    if (string.Equals(Environment.GetEnvironmentVariable(Saves.PatchGuard.FakeUnreadable), Path.GetFileName(one.Request.SavePath), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(L("does not read back exactly (test)"));
                });

                // Into the stash only now it is sure to go: added before the check, a character refused here kept its
                // gold in its file and the stash wrote it too.
                into?.SetGold((int)(into.Gold + moved));
                written.Add((one, bytes, new Report(save.Name, from, to, one.Request.DryRun, reports, moved, carried + stashed - moved, was, now, [])));
            }
            catch (Exception problem) when (problem is InvalidOperationException or InvalidDataException or IOException)
            {
                done[one.Request] = new Done(one.Request, null, problem.Message);
            }
        }

        // ---- write: every stash prepared and read back too, then each file replaced ------------------------------------
        var stashBytes = stashes.ToDictionary(pair => pair.Key, pair =>
        {
            byte[] bytes = pair.Value.Write();
            Saves.SharedStash.Read(bytes, Path.GetFileName(pair.Key));
            return bytes;
        }, StringComparer.OrdinalIgnoreCase);

        // The stash first: a failure after it leaves the gold in both files, never in neither. Replaced after them, a
        // stash that would not write took with it the gold of every character already written.
        var stashRefused = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, bytes) in stashBytes)
        {
            if (!written.Any(each => !each.One.Request.DryRun && each.Report.GoldMoved > 0
                                     && string.Equals(each.One.Request.StashPath, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            try
            {
                Saves.SaveWriter.Replace(path, bytes);
            }
            catch (Exception problem)
            {
                Log.Caught(problem);
                stashRefused[path] = problem.Message;
            }
        }

        // Each character alone: one that will not write is one refusal, and those written keep their reports.
        var notWritten = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var (one, bytes, report) in written)
        {
            if (one.Request.DryRun)
            {
                done[one.Request] = new Done(one.Request, report, null);
                continue;
            }

            if (one.Request.StashPath is { } stashPath && report.GoldMoved > 0 && stashRefused.TryGetValue(stashPath, out string? why))
            {
                done[one.Request] = new Done(one.Request, null, L($"Shared stash not written: {why}"));
                continue;
            }

            try
            {
                string backup = Saves.SaveWriter.Replace(one.Request.SavePath, bytes);
                done[one.Request] = new Done(one.Request, report with { Backups = [backup] }, null);
                Log.Info("season", $"Character {report.Name} migrated {from} -> {to}: {report.Items.Count} items, gold {report.GoldMoved:N0}, mana {report.Was.Mana} -> {report.Now.Mana}");
            }
            catch (Exception problem)
            {
                Log.Caught(problem);
                done[one.Request] = new Done(one.Request, null, problem.Message);
                if (one.Request.StashPath is { } taken && report.GoldMoved > 0)
                    notWritten[taken] = notWritten.GetValueOrDefault(taken) + report.GoldMoved;
            }
        }

        // A character that did not write still holds its gold: taken back out of the stash, or it would be there twice.
        foreach (var (path, gold) in notWritten)
        {
            try
            {
                var stash = stashes[path];
                stash.SetGold((int)(stash.Gold - gold));
                Saves.SaveWriter.Replace(path, stash.Write());
            }
            catch (Exception problem)
            {
                Log.Caught(problem);
            }
        }

        return [.. requests.Select(request => done[request])];
    }

    /// <summary>The base life, mana and stamina a character's class, level and points give under a season's tables.</summary>
    private static (int Life, int Mana, int Stamina) BaseOf(GameFiles.GameData game, D2S save) =>
        NewCharacter.Vitals(game, save.ClassId, save.Level, Raw(save, "vitality"), Raw(save, "energy"), 0);

    /// <summary>Every item the character's file holds, where it is, and how to put a carried copy in its place.</summary>
    private static IEnumerable<(string Place, Action<Item> Put, Item Item)> Everything(D2S save)
    {
        IEnumerable<(string, Action<Item>, Item)> Of(string place, List<Item>? list)
        {
            if (list is null)
                yield break;
            for (int at = 0; at < list.Count; at++)
            {
                int index = at;
                var parent = list[index];
                yield return (place, copy => list[index] = copy, parent);
                for (int hole = 0; hole < parent.SocketedItems.Count; hole++)
                {
                    int socket = hole;
                    yield return (place, copy => list[index].SocketedItems[socket] = copy, parent.SocketedItems[socket]);
                }
            }
        }

        foreach (var each in Of(L("Character"), save.PlayerItemList?.Items))
            yield return each;
        foreach (var each in Of(L("Mercenary"), save.MercenaryItemList?.ItemList?.Items))
            yield return each;
        foreach (var corpse in save.PlayerCorpses?.Corpses ?? [])
            foreach (var each in Of(L("Corpse"), corpse.ItemList?.Items))
                yield return each;
        // The golem's item and what is socketed in it - SkeleMerc's Pride kept its four runes at S13's stamp when the
        // golem's item alone was asked (a migration).
        if (save.Golem is { Exists: true, Item: { } golem })
        {
            yield return (L("Golem"), copy => save.Golem.Item = copy, golem);
            for (int hole = 0; hole < golem.SocketedItems.Count; hole++)
            {
                int socket = hole;
                yield return (L("Golem"), copy => save.Golem.Item.SocketedItems[socket] = copy, golem.SocketedItems[socket]);
            }
        }
    }

    private static int Raw(D2S save, string stat) => save.Attributes.Stats.TryGetValue(stat, out int value) ? value : 0;

    private static long Gold(D2S save, string purse) => Raw(save, purse);

    /// <summary>A purse set; a nought left out, as the game leaves it out.</summary>
    private static void SetGold(D2S save, string purse, long gold)
    {
        if (gold <= 0)
            save.Attributes.Stats.Remove(purse);
        else
            save.Attributes.Stats[purse] = (int)gold;
    }

    /// <summary>What quests add to the base life a character keeps: the Golden Bird's 20, once a difficulty.</summary>
    private static readonly int[] LifeGifts = [0, 20, 40, 60];

    /// <summary>
    /// A most and its current value moved from the old season's base to the new one's, by the same whole points, in the
    /// save's stored scale, never below nought - <b>only while the most is still the old season's</b>: the old base plus
    /// what quests give (<paramref name="gifts"/>), and not already the new one's. A character migrated once, or made in
    /// the new season, is left as it is: moved again, SkeleMerc's 292 mana became 381 (a copy).
    /// </summary>
    private static void Move(D2S save, string most, string current, int oldBase, int newBase, int[] gifts)
    {
        int points = newBase - oldBase;
        if (points == 0)
            return;

        int stored = AttributeScale.Display(most, Raw(save, most));
        if (!gifts.Any(gift => stored == oldBase + gift) || gifts.Any(gift => stored == newBase + gift))
            return;

        foreach (string stat in new[] { most, current })
        {
            if (!save.Attributes.Stats.TryGetValue(stat, out int raw))
                continue;
            long shifted = (long)raw + ((long)points << AttributeScale.ShiftFor(stat));
            save.Attributes.Stats[stat] = (int)Math.Max(0, shifted);
        }
    }

    private static (int Life, int Mana, int Stamina) Vitals(D2S save) =>
        (AttributeScale.Display("maxhp", Raw(save, "maxhp")), AttributeScale.Display("maxmana", Raw(save, "maxmana")),
         AttributeScale.Display("maxstamina", Raw(save, "maxstamina")));
}
