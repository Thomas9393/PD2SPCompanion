using System;
using System.Collections.Generic;
using System.Linq;

using CommunityToolkit.Mvvm.Input;

using D2SLib.Model.Save;

using PD2SPCompanion.Core.Collection;
using PD2SPCompanion.Shell.Services.Generic;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// The vault page's half of the clipboard (<see cref="ItemClipboard"/>): Ctrl + X marks
/// the cards acted on - or, from a folder's menu, the folder; Ctrl + C holds the cards to be copied; Ctrl + V
/// pastes what is held into the folder this page shows. A cut of the vault's is moved there, by its keys - the
/// cards need not be in sight, nor on this page; a cut of the character's is stored there; a copy is a second of
/// each under a new id, where the play mode makes seconds. It took the place of Duplicate, which copied in place.
/// </summary>
public sealed partial class VP_ViewModel_Vault_L
{
    private static ItemClipboard Clip => ItemClipboard.Shared;

    /// <summary>Cut takes what is selected, the folder's folders too; Copy the items alone - the vault makes no second of a folder.</summary>
    public bool CanCut => HasSelection;

    public bool CanCopy => HasActing;

    public bool CanPaste => !Clip.IsEmpty;

    public string CutHint => ShortcutText.Dimmed(ShortcutText.With(L("Cut"), L("Ctrl + X")), CanCut ? null : L("Nothing selected."));

    public string CopyHint => ShortcutText.Dimmed(ShortcutText.With(L("Copy"), L("Ctrl + C")), CanCopy ? null : L("No item selected."));

    /// <summary>Paste, and under it what is held: "3 items cut".</summary>
    public string PasteHint => ShortcutText.With(L("Paste"), L("Ctrl + V")) + (Clip.IsEmpty ? "" : Environment.NewLine + Clip.Says);

    /// <summary>The clipboard changed, or what is acted on: the three buttons say what they would do now, and the rows which is cut.</summary>
    private void SayClipboard()
    {
        OnPropertyChanged(nameof(CanCut));
        OnPropertyChanged(nameof(CutHint));
        OnPropertyChanged(nameof(CopyHint));
        OnPropertyChanged(nameof(CanPaste));
        OnPropertyChanged(nameof(PasteHint));
        // A folder's tile on the sheet is its row of the tree's own (FolderTile.Place): said with the rows.
        foreach (var place in Places.Concat(QuickPlaces))
            place.SayCut();

        // The line a cut left under this page goes with the cut: pasted on the other page, it stood on.
        if (Clip.Kind != ClipKind.Cut && _saidCut is { } said)
        {
            _saidCut = null;
            if (Notice == said)
                Notice = "";
        }
    }

    /// <summary>Ctrl + X: the cards acted on are marked to be moved by the paste - dimmed, and nothing moves yet.</summary>
    [RelayCommand]
    private void Cut()
    {
        var cards = Acting;
        var folders = ActingFolders;
        if (cards.Count + folders.Count == 0 || IsCarrying)
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        if (folders.Count == 0)
        {
            Clip.HoldCards(ClipKind.Cut, cards.Select(card => card.Key));
            Notice = _saidCut = Plural(cards.Count, "1 item cut. Paste to move it.", $"{cards.Count} items cut. Paste to move them.");
            return;
        }

        // Folders among them, from the sheet: cut together, moved together by the paste.
        Clip.HoldVault(cards.Select(card => card.Key), folders.Select(tile => tile.Place.Id!.Value));
        Notice = _saidCut = folders is [var one] && cards.Count == 0
            ? L($"{one.Name} cut. Paste to move it.")
            : L($"{cards.Count + folders.Count} cut. Paste to move them.");
    }

    /// <summary>What this page said of a cut: taken back once the cut is pasted or let go, wherever that was done.</summary>
    private string? _saidCut;

    /// <summary>A folder cut, from its menu: marked, and moved - with all in it - into the folder a paste is made in.</summary>
    public void CutFolder(long folder)
    {
        if (_folders.FirstOrDefault(each => each.Id == folder) is not { } cut)
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        Clip.HoldFolder(folder);
        Notice = _saidCut = L($"{cut.Name} cut. Paste to move it.");
    }

    /// <summary>The game's text of a copy, for the system's clipboard: the view's to write, which has the window's.</summary>
    public Action<string>? TextCopied { get; set; }

    /// <summary>
    /// Copy, by every way to it - the tools' button, Ctrl + C and the menu's row: the game's text of what is
    /// selected on the system's clipboard (<see cref="CopyText"/>, which says what it copied), and the selection
    /// held to be pasted as a second of itself (<see cref="Copy"/>). One command where there were two routes: the
    /// menu's row held the copy, and neither said so nor wrote the text, and the row of the tools' more-list did nothing at all.
    /// </summary>
    [RelayCommand]
    private void CopySelected()
    {
        if (CopyText() is { } text)
            TextCopied?.Invoke(text);
        Copy();
    }

    /// <summary>
    /// Ctrl + C: the cards acted on are held to be made seconds of by the paste, where the play mode makes
    /// seconds. The view puts the game's text of them on the system's clipboard besides (<see cref="CopyText"/>).
    /// </summary>
    public void Copy()
    {
        var cards = Acting;
        if (cards.Count > 0 && Rules.Duplicates)
            Clip.HoldCards(ClipKind.Copy, cards.Select(card => card.Key));
    }

    /// <summary>The inventory page's store of the character's items into a folder here (joined by the main view model): a cut of the character's, pasted.</summary>
    public Action<IReadOnlyCollection<Item>, long?>? StorePasted { get; set; }

    [RelayCommand]
    private void Paste() => PasteInto(_here);

    /// <summary>
    /// Ctrl + V, or a folder's menu: what the clipboard holds, into a folder - the one shown, or the one whose
    /// menu it was. One step of the history.
    /// </summary>
    public void PasteInto(long? folder)
    {
        if (IsCarrying || Store is not { } store)
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        DisarmDelete();

        if (Clip.IsEmpty)
        {
            Notice = L("Nothing to paste.");
            return;
        }

        string name = folder is { } into ? _folders.FirstOrDefault(each => each.Id == into)?.Name ?? TopName : TopName;

        // A gem cut in its socket: turned out onto the character by the Edit page that shows its item, and then
        // stored here, as any item of the character's cut and pasted here is.
        if (Clip.SocketedGems.Count > 0 && Clip.SocketHost is { } host)
        {
            var gems = Clip.SocketedGems.ToList();
            Clip.Clear();
            if (Clip.EjectSocketed?.Invoke(host, gems, null) is { } loose)
                StorePasted?.Invoke(loose, folder);
            else
                Notice = L("Nothing to paste.");
            return;
        }

        // A folder, with all in it: where it would go inside itself the vault refuses, and says so.
        if (Clip.Folders.Count > 0)
        {
            var folders = Clip.Folders.Select(id => CardOf(id)).OfType<FolderCard>().ToList();
            IReadOnlyList<long> keys = Clip.Cards.Count > 0 ? [.. store.RowsOf(Clip.Cards).Select(row => row.Key)] : [];
            Clip.Clear();
            if (keys.Count + folders.Count > 0)
                MoveInto(keys, folders, folder, name);
            return;
        }

        if (Clip.Cards.Count > 0)
        {
            var rows = store.RowsOf(Clip.Cards);
            if (rows.Count == 0)
            {
                Clip.Clear();
                Notice = L("Nothing to paste.");
                return;
            }

            if (Clip.Kind == ClipKind.Cut)
            {
                Clip.Clear();
                if (rows.All(row => row.FolderId == folder))
                {
                    Notice = L("Already in this folder.");
                    return;
                }

                MoveInto([.. rows.Select(row => row.Key)], [], folder, name);
            }
            else
            {
                PasteCopies(store, rows, folder, name);
            }

            return;
        }

        // The character's: a cut is stored as Store stores it; a copy is a second of each, made here.
        if (Clip.Kind == ClipKind.Cut)
        {
            var items = Clip.Items.ToList();
            Clip.Clear();
            StorePasted?.Invoke(items, folder);
        }
        else
        {
            PasteCopiesOfItems(store, folder, name);
        }
    }

    /// <summary>A second of each copied card, under a new id, in the folder pasted in - as Duplicate made them beside the first.</summary>
    private void PasteCopies(Vault store, IReadOnlyList<VaultRow> rows, long? folder, string name)
    {
        if (!Rules.Duplicates)
        {
            Notice = L("This play mode makes no copies.");
            return;
        }

        int made = 0;
        string? refused = null, only = null;
        try
        {
            foreach (var row in rows)
            {
                var copy = ItemBlob.Restore(row.Blob, store.StatIdBits);
                copy.Id = (uint)Random.Shared.NextInt64(1, uint.MaxValue);
                if (store.Add(copy, folder, out string? no) is null)
                {
                    refused ??= no;
                    continue;
                }

                Session.Made(copy);
                only = ItemPresenter.Describe(copy).Name;
                made++;
            }
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            refused ??= problem.Message;
        }

        SayCopied(made, rows.Count == 1 ? only : null, name, refused);
    }

    /// <summary>A second of each copied item of the character's, stored in the folder pasted in. The character keeps its own.</summary>
    private void PasteCopiesOfItems(Vault store, long? folder, string name)
    {
        if (!Rules.Duplicates)
        {
            Notice = L("This play mode makes no copies.");
            return;
        }

        int made = 0;
        string? refused = null, only = null;
        try
        {
            foreach (var item in Clip.Items)
            {
                if (Session.CopyOf(item) is not { } copy)
                {
                    refused ??= L("Not all could be copied.");
                    continue;
                }

                if (store.Add(copy, folder, out string? no) is null)
                {
                    refused ??= no;
                    continue;
                }

                Session.Made(copy);
                only = ItemPresenter.Describe(copy).Name;
                made++;
            }
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            refused ??= problem.Message;
        }

        SayCopied(made, Clip.Items.Count == 1 ? only : null, name, refused);
    }

    /// <summary>What a paste of copies made: one by its name, as Duplicate said it; several by their count and the folder.</summary>
    private void SayCopied(int made, string? only, string name, string? refused)
    {
        Say((only is not null ? L($"Copied {only}.") : L($"Copied {made} items.")) + (refused is null ? "" : " " + refused));
        Notice = "";
        if (made > 0)
            ReloadBoth();
    }

    /// <summary>
    /// Cards withdrawn to the character by their keys, in sight on this page or not: a cut of the vault's pasted
    /// on the inventory page (joined by the main view model). As Withdraw takes the cards acted on.
    /// </summary>
    public void WithdrawKeys(IReadOnlyCollection<long> keys)
    {
        if (CardsOf(keys) is { Count: > 0 } cards)
            WithdrawCards(cards, at: null);
    }

    /// <summary>A card by its key withdrawn and put on a worn slot: a cut of the vault's pasted on a slot of the inventory page (joined by the main view model).</summary>
    public void WearKeys(IReadOnlyCollection<long> keys, D2SLib.Model.Save.ItemLocation at, bool mercenary)
    {
        if (CardsOf(keys) is { Count: > 0 } cards)
            WearCards(cards, at, mercenary);
    }

    /// <summary>Cards by their keys, in sight on this page or not; none said on the strip.</summary>
    private List<ItemTileViewModel>? CardsOf(IReadOnlyCollection<long> keys)
    {
        if (Store is not { } store)
            return null;

        var cards = store.RowsOf(keys)
            .Select((row, index) => Cards.FirstOrDefault(card => card.Key == row.Key) ?? ItemTileViewModel.OfVault(row, index, store.StatIdBits))
            .ToList();
        if (cards.Count == 0)
            Notice = L("Nothing to paste.");

        return cards;
    }
}
