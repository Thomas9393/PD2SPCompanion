using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using PD2SPCompanion.Core.Collection;

namespace PD2SPCompanion.Shell.Services.Character;

/// <summary>
/// One file that is saved as part of a larger whole.
/// <para>
/// Saving is in two steps, and the split is the point. <b>Preparing writes nothing</b>: it
/// builds the bytes and checks they read back, so anything that is going to refuse has
/// refused while every file is still untouched. Only when every party has prepared does
/// anything get replaced.
/// </para>
/// </summary>
public interface ISaveParticipant
{
    /// <summary>What this is, for saying which one went wrong.</summary>
    string What { get; }

    /// <summary>Whether it holds anything not yet written.</summary>
    bool HasChanges { get; }

    /// <summary>
    /// When it is written against the others: lower first. The files are nought; the vault's gains
    /// go before them and its losses after, so a save that fails part-way leaves a duplicate and
    /// never a loss. Among equals, a file gaining what another loses goes first
    /// (<see cref="Holdings"/>), and the order they joined the set after that.
    /// </summary>
    int Order => 0;

    /// <summary>
    /// What it holds as it stands and as prepared, for telling which file an item is moving to; null
    /// where it has nothing to say. Asked after <see cref="Prepare"/>, in its own layout, and only of a
    /// save that writes another file of the same <see cref="Order"/>.
    /// </summary>
    SaveHoldings? Holdings() => null;

    /// <summary>
    /// Puts the parser into the layout this file is read and written at.
    /// <para>
    /// The parser keeps one layout at a time in a global, and every file that is read sets
    /// it - a character has skill bytes and ten-bit stat ids, a shared stash has neither.
    /// So each participant says which it wants rather than trusting what the last one left,
    /// which is a mistake this has already made three times.
    /// </para>
    /// </summary>
    void UseMyLayout();

    /// <summary>
    /// Works out what would be written and checks it reads back, <b>without touching the
    /// file</b>. Null when ready, otherwise why not.
    /// </summary>
    string? Prepare();

    /// <summary>
    /// Puts what was prepared in place of the file, keeping a copy of what was there under
    /// <paramref name="moment"/> - the one every file in the set shares, so the copies read
    /// as one state of everything. What it did that anyone may want to know of later goes on
    /// <paramref name="report"/>, the save's own, not in a field for whoever asks next.
    /// </summary>
    string? Write(string moment, SaveReport report);

    /// <summary>Throws away what is staged, back to the file as it stands on disk.</summary>
    void Revert();
}

/// <summary>
/// What one save did, handed to each file as it writes and back to whoever asked for the save: the copy it took of the
/// character, what it put into the vault and took out, the gold it moved, the trades it wrote. Filled as far as the save
/// got: one that failed part-way did what it says.
/// </summary>
public sealed class SaveReport
{
    /// <summary>The copy this save took of the character, where it took one: the asker captions it (SaveWriter.Caption).</summary>
    public string? Backup { get; set; }

    /// <summary>The rows it put in the vault, whole, so an undo can take them out.</summary>
    public List<VaultRow> Added { get; } = [];

    /// <summary>The rows it took out of the vault, whole, so an undo can put them back.</summary>
    public List<VaultRow> Removed { get; } = [];

    /// <summary>The gold it moved.</summary>
    public List<GoldMove> Gold { get; } = [];

    /// <summary>The trades it wrote into the ledger: an undo of its step takes them out again, a redo writes them back.</summary>
    public List<TradeRecord> Trades { get; } = [];
}

/// <summary>
/// The moves across files a save is part of, written down before its first file and cleared
/// after its last (<see cref="PD2SPCompanion.Shell.Services.Vault.VaultMoves"/>). One left behind is a save cut short, put right
/// the next time the vault opens - or at once, when a write fails.
/// </summary>
public interface ISaveJournal
{
    /// <summary>
    /// After every part has prepared, before anything is written. Null when written down, or
    /// nothing to; otherwise why the save must not go ahead.
    /// </summary>
    string? Open(IReadOnlyList<ISaveParticipant> acting);

    /// <summary>
    /// After the writes: every one done, or one failed part-way - then put right at once, and
    /// what was found returned for the message.
    /// </summary>
    string? Close(bool completed);
}

/// <summary>
/// The files that are saved together, and the one place that decides when.
/// <para>
/// A character and the shared stash are two files, and an item moved between them is written
/// to both. Saving one without the other loses the item or leaves a copy in each - so they
/// are saved as one thing, and this is the thing.
/// </para>
/// <para>
/// It exists because the alternative was tried. The character session owned the commit and
/// took the stash along as a passenger, which made it both a participant and the umpire: it
/// set the parser layout for itself and a passenger then changed it underneath, and it wrote
/// the passengers from inside its own attempt, so "everything is verified before anything is
/// replaced" was a comment rather than a fact. The stash was written, the character refused,
/// and the item that had moved between them was in neither file.
/// </para>
/// <para>
/// Two files still cannot be replaced in one indivisible step. What this guarantees is that
/// <b>every refusal happens before any write</b>, and that if a write itself fails the
/// report says which files had already been replaced and that a copy of each sits beside it.
/// </para>
/// <para>
/// And the file gaining an item is written before the file losing it, decided each save from what
/// moved (<see cref="SaveHoldings"/>), so a write that fails leaves the item in both and not in
/// neither: a duplicate is acceptable, a loss is not. The one thing no order
/// serves is one save moving items both ways between two files, each gaining what the other
/// loses: autosave writes after a move, so it takes moves made faster than that. There the copy
/// beside each file is the way back, and the report says so.
/// </para>
/// </summary>
public sealed partial class SaveSet : ObservableObject
{
    private readonly List<ISaveParticipant> _parts = [];

    /// <summary>
    /// Adds a file to the set. They are written by <see cref="ISaveParticipant.Order"/>, then the
    /// gaining file first, and in the order they joined after that.
    /// </summary>
    public void Takes(ISaveParticipant part)
    {
        if (!_parts.Contains(part))
            _parts.Add(part);
    }

    /// <summary>What writes down the moves a save is part of, when there is one.</summary>
    public ISaveJournal? Journal { get; set; }

    /// <summary>Whether anything in the set is waiting to be written.</summary>
    public bool HasChanges => _parts.Any(part => part.HasChanges);

    /// <summary>What the Save button asks.</summary>
    public bool CanSave => HasChanges;

    /// <summary>
    /// How many times a participant has said something of its own changed (<see cref="SomethingChanged"/>): every
    /// edit of the character and of the shared stash passes there (their MarkEdited), every save and every
    /// revert. Whoever keeps something worked out from the items - the search's index (InventorySearch) - keeps
    /// it for as long as this stands.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// Told by a participant when its own changes come or go, so the buttons follow. The
    /// participants are the ones who know; this only reports.
    /// </summary>
    public void SomethingChanged()
    {
        Version++;
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(CanSave));
    }

    /// <summary>
    /// Asked before anything is written, with the act's name: why not, or null. GameSync's - the game running, the
    /// patch guard's hold (<c>GameSync.RefuseWrite</c>). None in a set nothing guards.
    /// </summary>
    public Func<string, string?>? Refuse { get; set; }

    /// <summary>Why the files may not be written now, worded for <paramref name="act"/>; null when they may.</summary>
    public string? Refused(string act) => Refuse?.Invoke(act);

    /// <summary>
    /// Raised for every save that wrote anything, whoever asked for it and however far it got, with what it did. The
    /// session's history keeps it for its next step: a save with no step of its own is part of that one.
    /// </summary>
    public event Action<SaveReport>? Written;

    /// <inheritdoc cref="Commit(out SaveReport)"/>
    public string? Commit() => Commit(out _);

    /// <summary>
    /// Writes everything that has changed, or nothing. Null when written, otherwise what
    /// went wrong - already worded for showing.
    /// </summary>
    /// <param name="report">What this save did, as far as it got.</param>
    public string? Commit(out SaveReport report)
    {
        report = new SaveReport();

        var acting = _parts.Where(part => part.HasChanges).OrderBy(part => part.Order).ToList();
        if (acting.Count == 0)
            return null;

        // The one gate under every write of the game's files, whoever asks for it:
        // undo wrote through here past the game running and the patch guard's hold alike.
        if (Refused(L("save")) is { } blocked)
            return blocked;

        // Nothing on disk is touched by this pass. Anything that is going to refuse - a
        // stat that will not fit, a stash that will not read back - refuses here, where a
        // refusal costs nothing at all.
        foreach (var part in acting)
        {
            part.UseMyLayout();

            if (part.Prepare() is { } refused)
                return refused;
        }

        acting = GainsFirst(acting);

        // One moment for the whole set, so the copies left behind pair up and a restore
        // can take all of them or none.
        string moment = PD2SPCompanion.Core.Saves.SaveWriter.Moment();

        // The moves across files, written down before the first of them.
        if (Journal?.Open(acting) is { } unjournaled)
            return unjournaled;

        string? trouble = WriteAll(acting, moment, report);
        Written?.Invoke(report);
        return trouble;
    }

    /// <summary>Every file in its turn, stopping at one that fails. Null when all are written, else what to say.</summary>
    private string? WriteAll(List<ISaveParticipant> acting, string moment, SaveReport report)
    {
        var replaced = new List<string>();

        foreach (var part in acting)
        {
            part.UseMyLayout();

            if (part.Write(moment, report) is { } failed)
            {
                // What the cut-short moves left, said in place of the general warning: the
                // duplicate this made is always said.
                if (Journal?.Close(completed: false) is { } found)
                    return $"{failed} {found}";

                if (replaced.Count == 0)
                    return failed;

                // The bad case, and the reader deserves to know exactly where they stand.
                string written = Joined(replaced);
                return L($"{failed} {written} already written. Previous versions kept beside them.");
            }

            replaced.Add(part.What);
        }

        Journal?.Close(completed: true);
        SomethingChanged();
        return null;
    }

    /// <summary>
    /// The files in the order to write them: within each <see cref="ISaveParticipant.Order"/>, one gaining what another
    /// loses before it, and otherwise as they stood. Where two each gain what the other loses, the first as it stood goes
    /// first, and it is logged. Holdings that will not read say nothing of that file, which is written as it stood.
    /// </summary>
    private static List<ISaveParticipant> GainsFirst(List<ISaveParticipant> acting)
    {
        var ordered = new List<ISaveParticipant>(acting.Count);
        foreach (var group in acting.GroupBy(part => part.Order))
        {
            var left = group.ToList();
            if (left.Count < 2)
            {
                ordered.AddRange(left);
                continue;
            }

            var holdings = left.ToDictionary(part => part, HoldingsOf);

            bool WaitsFor(ISaveParticipant part, ISaveParticipant other) =>
                !ReferenceEquals(part, other) && holdings[other] is { } gaining && holdings[part] is { } losing
                && gaining.GainsWhatLoses(losing);

            while (left.Count > 0)
            {
                var next = left.FirstOrDefault(part => !left.Any(other => WaitsFor(part, other)));
                if (next is null)
                {
                    next = left[0];
                    Log.Warning("save", "Items move both ways between files in one save: no order keeps them from a failed write.");
                }

                ordered.Add(next);
                left.Remove(next);
            }
        }

        return ordered;
    }

    private static SaveHoldings? HoldingsOf(ISaveParticipant part)
    {
        try
        {
            part.UseMyLayout();
            return part.Holdings();
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            return null;
        }
    }

    /// <summary>Throws away what every participant has staged.</summary>
    public void Revert()
    {
        foreach (var part in _parts)
        {
            part.UseMyLayout();
            part.Revert();
        }

        SomethingChanged();
    }
}
