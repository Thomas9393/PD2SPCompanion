using System;
using System.Collections.Generic;
using System.Linq;

using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Character;

namespace PD2SPCompanion.Shell.Services.Vault;

/// <summary>
/// The trades, written down when the character is saved - which a repeat buy is told by. The items that came out of
/// the vault and those the editor made were written beside them until vault format 20: their one
/// reader was the grail, which is the game's Discovery since.
/// <para>
/// Held until Save and dropped by Undo, like every edit it stands for: a purchase undone did not
/// happen, and the ledger must not say it did. A save participant after the files and the
/// vault's own moves - see <see cref="ISaveParticipant.Order"/> - so a save that fails leaves no
/// record of what it did not write.
/// </para>
/// </summary>
public sealed class ItemHistory : ISaveParticipant
{
    private readonly CharacterSession _character;
    private readonly VaultService _vaults;
    private readonly SaveSet _saves;

    /// <summary>A trade waiting for the save, with the item it brought - its id is read when written.</summary>
    private readonly List<(TradeRecord Record, Item? Item)> _trades = [];

    public ItemHistory(CharacterSession character, VaultService vaults, SaveSet saves)
    {
        _character = character;
        _vaults = vaults;
        _saves = saves;

        saves.Takes(this);

        // A character opened, reloaded or closed takes its unsaved edits with it, and these too.
        _character.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(CharacterSession.IsLoaded) && !_character.IsLoaded)
                Drop();
        };
    }

    /// <summary>A purchase or a sale, to be written with the next save.</summary>
    public void Traded(TradeRecord record, Item item)
    {
        _trades.Add((record, item));
        _saves.SomethingChanged();
    }

    /// <summary>
    /// Whether this item is bought already and waiting for a save: the same listing, the same item
    /// relisted, or the same item posted twice - what <see cref="PD2SPCompanion.Core.Collection.Vault.BoughtBefore"/>
    /// asks of the written ledger.
    /// </summary>
    public bool IsBuying(string listingId, string siteItemId, string siteHash) =>
        _trades.Any(each => each.Record.Kind == "bought"
                            && ((listingId.Length > 0 && each.Record.ListingId == listingId)
                                || (siteItemId.Length > 0 && each.Record.SiteItemId == siteItemId)
                                || (siteHash.Length > 0 && each.Record.SiteHash == siteHash)));

    private void Drop()
    {
        if (_trades.Count == 0)
            return;

        _trades.Clear();
        _saves.SomethingChanged();
    }

    // ---- the save --------------------------------------------------------------------------------

    string ISaveParticipant.What => L("The vault's history");

    /// <summary>After the files and the vault's own moves.</summary>
    int ISaveParticipant.Order => 2;

    bool ISaveParticipant.HasChanges => _trades.Count > 0;

    void ISaveParticipant.UseMyLayout() => _character.UseMyLayout();

    private List<TradeRecord> _preparedTrades = [];

    string? ISaveParticipant.Prepare()
    {
        // A trade happened whether or not its item is still here - it may have been sold on, or
        // deleted - and the currency moved either way. Its id is the one it has now.
        _preparedTrades = [.. _trades.Select(each => each.Item is { IsCompact: false } item
            ? each.Record with { ItemId = item.Id }
            : each.Record)];

        return null;
    }

    string? ISaveParticipant.Write(string moment, SaveReport report)
    {
        // Without a vault there is nowhere to write it. The files are saved all the same: a ledger
        // is not worth refusing a save over.
        if (_vaults.Store is { } store)
        {
            try
            {
                store.RecordTrades(_preparedTrades);
                report.Trades.AddRange(_preparedTrades);
            }
            catch (Exception problem)
            {
                Log.Caught(problem);
                return L($"History not written: {problem.Message}");
            }
        }

        _trades.Clear();
        _preparedTrades = [];
        return null;
    }

    void ISaveParticipant.Revert() => Drop();
}
