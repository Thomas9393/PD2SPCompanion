using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;

using PD2SPCompanion.Core;
using PD2SPCompanion.Shell.Services.Generic;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// The vault page's right-click menus:
/// what can be done to a folder from its row of the tree, its row of quick access, its tile on the sheet or its
/// step of the path - without going to it first, which every one of these asked for until then ("a folder is
/// acted on only once it is the folder shown", the first gap found against Explorer). The view says what was
/// clicked and shows the menu (ActMenuService); what is in it is decided here.
/// </summary>
public sealed partial class VP_ViewModel_Vault_L
{
    /// <summary>
    /// A folder's menu - the vault's top, with null. Open first, as a click on it does; then where else it can
    /// be shown, what can be made in it and where it can go, and its two marks, the lock and the pin. Rename and
    /// Delete are icons at the menu's head. The top is neither renamed, moved, deleted nor pinned.
    /// <para>
    /// <paramref name="onSheet"/>: the menu of a tile of the sheet, which the right press selected - or left
    /// selected with the others, where it was one of several. Then Cut, Delete and Move to take them all, as an
    /// item's menu does (else they take the one folder and leave the rest selected), and Rename,
    /// which names one, is dimmed. Its icons say their keys, which act on what is selected; a row of the tree's
    /// and a step of the path's say none - Ctrl + X, F2 and Del do nothing to those (they said them all).
    /// </para>
    /// </summary>
    public ActMenu MenuFor(long? folder, bool onSheet = false)
    {
        if (Store is not { } store || folder is { } asked && _folders.All(each => each.Id != asked))
            return new ActMenu([], []);

        var place = Places.FirstOrDefault(each => each.Id == folder);
        bool locked = place?.IsLocked == true;
        bool pinned = folder is { } id && QuickPlaces.Any(each => each.Id == id);
        bool shown = folder == _here;
        int with = ActingFolders.Count + Acting.Count;
        bool many = onSheet && with > 1 && ActingFolders.Any(tile => tile.Place.Id == folder);

        var icons = new List<MenuAct>();
        var rows = new List<MenuAct>
        {
            new(L("Open"), MenuGlyph.OpenFolder, () => GoTo(folder)) { IsMain = true, Keys = L("Enter") },
        };

        // On the other page: the left page's puts the vault on both, as its Compare does; the right page's works
        // only while the left page is the vault too - else the other page is the inventory's, and the row is
        // dimmed and says so (rather than left out, which changed the menu's rows with the left page).
        if (Twin is { } twin)
        {
            bool can = IsPrimary ? CompareRequested is not null : IsComparing;
            rows.Add(new(L("Open on the other page"), MenuGlyph.Compare, () =>
            {
                if (!IsComparing)
                    CompareRequested?.Invoke();
                twin.GoTo(folder);
            }) { IsEnabled = can, Why = can ? "" : L("Require vault on both pages.") });
        }

        rows.Add(new(L("New folder"), MenuGlyph.NewFolder, () =>
        {
            if (!shown)
                GoTo(folder);
            NewFolderCommand.Execute(null);
        }) { RuleAbove = true });

        if (folder is { } moved)
        {
            rows.Add(many
                ? new(ActMenu.Counted(L("Move to folder..."), with), MenuGlyph.MoveFolder, () => OpenMoveCommand.Execute(null))
                : new(L("Move to folder..."), MenuGlyph.MoveFolder, () =>
                {
                    if (CardOf(moved) is { } card)
                        OpenMoveFor([], [card], L($"Move {card.Name}"), fromHere: false);
                }));
        }

        // The tree under it, all unfolded or all folded - the whole tree, from the vault's own row.
        if (folder is null ? CanFoldTree : place?.HoldsFolders == true)
        {
            rows.Add(new(folder is null ? L("Unfold all") : L("Unfold all inside"), MenuGlyph.ChevronsDown, () => FoldAll(true, folder)) { RuleAbove = true });
            rows.Add(new(folder is null ? L("Fold all") : L("Fold all inside"), MenuGlyph.ChevronsUp, () => FoldAll(false, folder)));
        }

        rows.Add(new(locked ? L("Unlock") : L("Lock"), locked ? MenuGlyph.Unlock : MenuGlyph.Lock, () => SetFolderLock(folder, !locked)) { RuleAbove = true });

        if (folder is { } pin)
        {
            rows.Add(new(pinned ? L("Unpin from quick access") : L("Pin to quick access"), MenuGlyph.Star, () => PinFolder(pin, !pinned)));

            // Cut, to be moved - with all in it - into the folder a paste is made in. A key only where the key
            // would do the same: on a tile, which is selected; Ctrl + V pastes into the folder shown.
            icons.Add(new(L("Cut"), MenuGlyph.Cut, () =>
            {
                if (many)
                    CutCommand.Execute(null);
                else
                    CutFolder(pin);
            }) { Keys = onSheet ? L("Ctrl + X") : "" });
            icons.Add(new(L("Paste"), MenuGlyph.Paste, () => PasteInto(folder)) { Keys = shown ? L("Ctrl + V") : "", IsEnabled = CanPaste, Why = CanPaste ? Clip.Says : "" });

            // Named on its tile: where the sheet shown has none of it - a row of the tree, a step of the path,
            // the folder shown itself - the folder it lies in is gone to first (RenameFolderInPlace).
            icons.Add(new(L("Rename"), MenuGlyph.Pencil, () => RenameFolderInPlace(pin)) { Keys = onSheet ? L("F2") : "", IsEnabled = !many });

            // Asked in the menu where the folder holds nothing (a bin always asks); where it
            // holds something its question is the asking, and the menu's own is left out.
            bool holds = many ? DeleteAsksAboutContents : CardOf(pin) is { } deleted && !IsEmpty(store, deleted);
            icons.Add(new(L("Delete"), MenuGlyph.Bin, () =>
            {
                if (!many)
                {
                    DeleteFolder(pin);
                    return;
                }

                DeleteArmed = true;
                DeleteCommand.Execute(null);
            }) { Keys = onSheet ? L("Del") : "", IsBin = true, Confirm = holds ? "" : L("Confirm") });
        }

        // The top takes a paste too: what is cut goes to the vault's top level.
        if (folder is null)
            icons.Add(new(L("Paste"), MenuGlyph.Paste, () => PasteInto(null)) { Keys = shown ? L("Ctrl + V") : "", IsEnabled = CanPaste, Why = CanPaste ? Clip.Says : "" });

        return new ActMenu(icons, rows);
    }

    /// <summary>
    /// The menu of the sheet's empty room: Paste, into the folder shown; New folder, in it; Select all.
    /// </summary>
    public ActMenu MenuForSpace() => Store is null ? new ActMenu([], []) : new(
    [
        new(L("Paste"), MenuGlyph.Paste, () => PasteInto(_here)) { Keys = L("Ctrl + V"), IsEnabled = CanPaste, Why = CanPaste ? Clip.Says : "" },
    ],
    [
        new(L("New folder"), MenuGlyph.NewFolder, () => NewFolderCommand.Execute(null)),
        ActMenu.SortBy(Order),
        // Dimmed in a locked folder, with why: Select all stays out of it, and the row said so only once picked.
        new(L("Select all"), MenuGlyph.SelectAll, SelectAll)
        {
            Keys = L("Ctrl + A"),
            IsEnabled = !IsFolderLocked,
            Why = IsFolderLocked ? L("Folder locked.") : "",
        },
    ]);

    /// <summary>
    /// The menu of a find on the Search page: Go to its place first - what a click on the row does - then what
    /// is done to a vault item by its key, wherever its folder: withdrawn, moved, cut. The rest of an item's
    /// menu is its card's, a step away.
    /// </summary>
    public ActMenu MenuOfFind(long key, long? folder, string name)
    {
        bool can = Session.IsLoaded && !GameBlocked;
        return new ActMenu(
        [
            new(L("Cut"), MenuGlyph.Cut, () =>
            {
                Clip.HoldCards(ClipKind.Cut, [key]);
                Notice = _saidCut = L("1 item cut. Paste to move it.");
            }) { Keys = L("Ctrl + X") },
        ],
        [
            new(L("Go to its place"), MenuGlyph.Open, () => GoToFound(key, folder)) { IsMain = true },
            new(L("Withdraw"), MenuGlyph.FromVault, () => WithdrawKeys([key]))
            {
                RuleAbove = true,
                IsEnabled = can,
                Why = can ? "" : WithdrawWhy,
            },
            new(L("Move to folder..."), MenuGlyph.MoveToFolder, () => OpenMoveFor([key], [], L($"Move {name}"), fromHere: false)),
        ]);
    }

    /// <summary>
    /// A right press on a card: it is selected, as Explorer's right click selects - unless it is one of those
    /// acted on already, which the menu then speaks for. So the menu's rows are the tools' own commands.
    /// </summary>
    public void SelectForMenu(ItemTileViewModel card)
    {
        if (!IsCarrying && !Acting.Contains(card))
            ClickTileCommand.Execute(card);
    }

    /// <summary>
    /// Identify, in a card's menu: the unidentified among the cards acted on are identified
    /// and put back in their places - an edit of a stored item, so each has a new id, as the vault gives every
    /// edited item (Vault.Replace). In every play mode.
    /// </summary>
    public void Identify()
    {
        if (Store is not { } store || Acting.Where(card => !card.Item.IsIdentified).ToList() is not { Count: > 0 } going)
            return;

        _sfxManager?.Method_ButtonSFX_Press();

        int done = 0;
        string? trouble = null, last = null;
        foreach (var card in going)
        {
            card.Item.IsIdentified = true;
            try
            {
                if (store.Replace(card.Key, card.Item, out string? refused) is null)
                {
                    card.Item.IsIdentified = false;
                    trouble ??= refused;
                    continue;
                }

                last = ItemPresenter.Describe(card.Item).Name;
                done++;
            }
            catch (Exception problem)
            {
                Log.Caught(problem);
                card.Item.IsIdentified = false;
                trouble ??= problem.Message;
            }
        }

        Notice = done == 0
            ? L($"Not identified: {trouble}")
            : (done == 1 ? L($"{last} identified.") : L($"{done} items identified."))
              + (trouble is null ? "" : " " + L($"Not all: {trouble}"));

        if (done > 0)
            ReloadBoth();
    }

    /// <summary>
    /// The tools' Identify, shown only while a card acted on is unidentified - at the end of
    /// the line's near tools, so nothing moves when it comes and goes.
    /// </summary>
    public bool CanIdentify => Acting.Any(card => !card.Item.IsIdentified);

    public string IdentifyHint => ActMenu.Counted(L("Identify"), Acting.Count(card => !card.Item.IsIdentified));

    [RelayCommand]
    private void IdentifySelected() => Identify();

    /// <summary>What the Menu key's menu is for: the first of the cards acted on.</summary>
    public ItemTileViewModel? MenuCard() => Acting.FirstOrDefault();

    /// <summary>
    /// The menu of the card right-clicked - selected by the press, or one of several. Withdraw first, the vault's
    /// own act; for one card of gear, Equip - in the other slot where two take it, on the mercenary - while the
    /// inventory is beside; then where it can go within the vault: the other page's folder, any folder. Delete is
    /// an icon at the menu's head.
    /// </summary>
    public ActMenu MenuFor(ItemTileViewModel card)
    {
        bool several = MovedWith(card) > 1;
        bool beside = IsOnRight?.Invoke() == true;

        var rows = new List<MenuAct>
        {
            new(ActMenu.Counted(L("Withdraw"), MovedWith(card)), MenuGlyph.FromVault, () => WithdrawCommand.Execute(null))
            {
                IsMain = true,
                IsEnabled = CanWithdraw,
                Why = CanWithdraw ? "" : WithdrawWhy,
            },
        };

        if (!several && EquipChoice is { } choice && choice(card.Item, false) is { IsGear: true } own)
        {
            string away = L("Require inventory page open.");
            rows.Add(new(own.Does, MenuGlyph.BattleGear, () => Wear(card))
            {
                IsEnabled = beside && own.Slots > 0,
                Why = !beside ? away : own.Why ?? "",
            });

            if (own.Slots > 1)
                rows.Add(new(L("Equip in other slot"), MenuGlyph.BattleGear, () => Wear(card, index: 1)) { IsEnabled = beside, Why = beside ? "" : away });

            var hired = choice(card.Item, true);
            rows.Add(new(L("Equip on mercenary"), MenuGlyph.PersonPlus, () => Wear(card, mercenary: true))
            {
                IsEnabled = beside && hired.Slots > 0,
                Why = !beside ? away : hired.Why ?? "",
            });
        }

        // Identify, where one of them is not: as on the Items page.
        if (Acting.Count(each => !each.Item.IsIdentified) is > 0 and var unknown)
            rows.Add(new(ActMenu.Counted(L("Identify"), unknown), MenuGlyph.Magnifier, Identify) { RuleAbove = true });

        // Dimmed with why while the vault is on one page only, not left out.
        if (Twin is not null)
        {
            rows.Add(new(ActMenu.Counted(L("To other page"), MovedWith(card)), MenuGlyph.SwapAcross, () => SendAcross(card))
            {
                RuleAbove = true,
                IsEnabled = IsComparing,
                Why = IsComparing ? "" : L("Require vault on both pages."),
            });
        }

        rows.Add(new(ActMenu.Counted(L("Move to folder..."), MovedWith(card)), MenuGlyph.MoveToFolder, () => OpenMoveCommand.Execute(null))
        {
            RuleAbove = Twin is null,
            IsEnabled = CanMove,
        });

        // The clipboard's three, then the bin - as Windows' menu heads its own. Copy only where a copy can be
        // pasted: where the play mode makes seconds.
        var icons = new List<MenuAct> { new(L("Cut"), MenuGlyph.Cut, () => CutCommand.Execute(null)) { Keys = L("Ctrl + X"), IsEnabled = CanCut } };
        if (Rules.Duplicates)
            icons.Add(new(L("Copy"), MenuGlyph.Duplicate, () => CopySelectedCommand.Execute(null)) { Keys = L("Ctrl + C"), IsEnabled = CanCopy });
        icons.Add(new(L("Paste"), MenuGlyph.Paste, () => PasteCommand.Execute(null)) { Keys = L("Ctrl + V"), IsEnabled = CanPaste, Why = CanPaste ? Clip.Says : "" });
        // Asked in the menu (MenuAct.Confirm): by the time it runs the answer is yes, as the bin's second press is.
        // Not where folders that hold something are among the selected: their question is the asking.
        icons.Add(new(L("Delete"), MenuGlyph.Bin, () =>
        {
            DeleteArmed = true;
            DeleteCommand.Execute(null);
        }) { Keys = L("Del"), IsBin = true, Confirm = DeleteAsksAboutContents ? "" : L("Confirm"), IsEnabled = CanDeleteSelection });

        return new ActMenu(icons, rows);
    }

    /// <summary>A folder as the folder tools are handed one, whichever folder is shown.</summary>
    private FolderCard? CardOf(long folder) =>
        _folders.FirstOrDefault(each => each.Id == folder) is { } found ? new FolderCard(found) : null;

    /// <summary>
    /// A folder deleted from its menu: where it holds nothing the menu has asked (its bin's Confirm), else its
    /// three-way question does (<see cref="AskToDelete"/>). Where the folder shown is it, or in it, the page
    /// goes to what the folder was in.
    /// </summary>
    private void DeleteFolder(long folder)
    {
        _sfxManager?.Method_ButtonSFX_Press();

        if (Store is not { } store || CardOf(folder) is not { } card)
            return;

        if (IsEmpty(store, card))
            DeleteFolders(store, [card], [], everything: false);
        else
            AskToDelete(store, card);
    }

    /// <summary>A folder pinned to quick access or taken off it, from its menu: the tools' star does it for the one shown.</summary>
    private void PinFolder(long folder, bool pinned)
    {
        _sfxManager?.Method_ButtonSFX_Press();
        if (Store is not { } store)
            return;

        try
        {
            store.SetQuickAccess(folder, pinned);
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            Notice = problem.Message;
            return;
        }

        ReloadBoth();
    }
}
