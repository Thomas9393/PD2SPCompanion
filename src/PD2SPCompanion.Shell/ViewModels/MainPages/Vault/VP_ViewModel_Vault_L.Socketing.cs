using System;
using System.Collections.Generic;
using System.Linq;

using CommunityToolkit.Mvvm.Input;

using D2SLib.Model.Save;

using PD2SPCompanion.Core.Collection;
using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Shell.Services.Settings;

namespace PD2SPCompanion.Shell.ViewModels;

// What the Items page had of sockets and the vault's page had not - the opaque sockets and the socket guard among
// them, the toggle and the guard in step with the inventory so there is no inconsistency to frustrate:
//
//   - the sockets over an item's art shown or faded - the same button, and X; one state for both pages
//     (PlayRules.SocketsFaded);
//   - a gem, a rune or a jewel carried onto a card with an empty socket goes into it, as on the Items page - both
//     cards the vault's, the item rewritten and the gem's row gone; one of a stack, and the stack one less;
//   - the socket guard over that drop - the same setting (PlayRules.SocketGuard);
//   - Equip and Equip on mercenary on the tools line, for the card selected: what its menu's rows do, in sight.
public sealed partial class VP_ViewModel_Vault_L
{
    // ---- the sockets' look, and the guard: every page's ------------------------------------------------------------

    public bool SocketsFaded => Rules.SocketsFaded;

    public double SocketOpacity => Rules.SocketOpacity;

    public string SocketHint => SocketWords.Look(Rules.SocketsFaded);

    [RelayCommand]
    public void ToggleSockets()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        Rules.SocketsFaded = !Rules.SocketsFaded;
    }

    public bool SocketGuard => Rules.SocketGuard;

    public string SocketGuardHint => SocketWords.Guard(Rules.SocketGuard);

    [RelayCommand]
    private void ToggleSocketGuard()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        Rules.SocketGuard = !Rules.SocketGuard;
        Say(Rules.SocketGuard ? L("Socket guard on.") : L("Socket guard off."));
    }

    /// <summary>Changed on either page, the two are changed on this one: said by the rules both look at.</summary>
    private void FollowSocketRules() =>
        Rules.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is null or "" or nameof(PlayRules.SocketsFaded) or nameof(PlayRules.SocketOpacity))
            {
                OnPropertyChanged(nameof(SocketsFaded));
                OnPropertyChanged(nameof(SocketOpacity));
                OnPropertyChanged(nameof(SocketHint));
            }

            if (e.PropertyName is null or "" or nameof(PlayRules.SocketGuard))
            {
                OnPropertyChanged(nameof(SocketGuard));
                OnPropertyChanged(nameof(SocketGuardHint));
            }
        };

    // ---- a gem carried onto a card -------------------------------------------------------------------------------------

    /// <summary>The cards carried - by this page or the other, the vault on both - where every one is a gem, a rune or a jewel; none else.</summary>
    private IReadOnlyList<ItemTileViewModel> CarriedFillers =>
        Carrier is { _carried: { Count: > 0 } carried } && carried.All(card => Socketing.FillerCode(card.Item.Code) is not null)
            ? carried
            : [];

    /// <summary>
    /// Whether what is carried would go into this card's sockets, let go of on it - the guard aside. Not where either end
    /// is on its way out of the vault: a gem put into a leaving row goes with it at the next save, and is lost; one that
    /// is itself leaving would be on the character and in the socket both.
    /// </summary>
    private bool FitsSockets(ItemTileViewModel? host) =>
        SocketsTake(host) && !Leaves(host!) && !CarriedFillers.Any(Leaves);

    /// <summary>The gesture's shape: fillers carried, a card with an empty socket, a folder not locked.</summary>
    private bool SocketsTake(ItemTileViewModel? host) =>
        host is not null && Store is not null && !IsFolderLocked && Socketing.HasRoom(host.Item)
        && CarriedFillers is { Count: > 0 } fillers && !fillers.Contains(host);

    /// <summary>Whether a card's row is marked to leave the vault with the next save: withdrawn, or sold.</summary>
    private bool Leaves(ItemTileViewModel card) => _moves?.IsWithdrawing(card.Key) == true;

    /// <summary>Whether what is carried would go into this card's sockets, were it let go of now: the card is lit for it.</summary>
    public bool WouldSocket(ItemTileViewModel? host) => !Rules.SocketGuard && FitsSockets(host);

    /// <summary>The card lit as what the carried gem would go into; null, none.</summary>
    private ItemTileViewModel? _socketTarget;

    /// <summary>What is carried is over a card, or over none: the one it would go into is lit.</summary>
    public void HoverCard(ItemTileViewModel? card)
    {
        var target = WouldSocket(card) ? card : null;
        if (ReferenceEquals(target, _socketTarget))
            return;

        if (_socketTarget is { } was)
            was.IsSocketTarget = false;
        _socketTarget = target;
        if (target is not null)
            target.IsSocketTarget = true;
    }

    /// <summary>
    /// The carried cards let go of on a card. Fillers all, and the card with an empty socket: into it, one after
    /// another while it has one - the item rewritten in its row, each gem's row gone, one of a stack taken and the
    /// stack one less. True where it was this gesture's: socketed, or refused in words. False for any other drop,
    /// which is then what it always was.
    /// </summary>
    public bool SocketCarried(ItemTileViewModel? host)
    {
        if (!SocketsTake(host) || host is null || Store is not { } store)
            return false;

        var fillers = CarriedFillers.ToList();
        string hostName = host.Name;
        EndCarryOver();
        HoverCard(null);
        _sfxManager?.Method_ButtonSFX_Press();

        if (Rules.SocketGuard)
        {
            Say(L("Socket guard on."));
            return true;
        }

        // A card on its way out at either end: saved first, as Delete does, and nothing socketed. The save that works
        // takes the row out, so what was aimed at may be gone: the player looks again.
        if (Leaves(host) || fillers.Any(Leaves))
        {
            _moves?.SaveNow?.Invoke();
            Say(L("Saving. Try again."));
            return true;
        }

        Item into;
        try
        {
            into = ItemBlob.Restore(host.Row.Blob, store.StatIdBits);
        }
        catch (Exception problem)
        {
            PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
            Say(L($"{hostName} not read: {problem.Message}"));
            return true;
        }

        string stamp = into.Version ?? "";
        var gone = new List<long>();
        var fewer = new List<(long Key, Item Stack)>();
        int went = 0;
        string? last = null, refused = null;
        Runewords.Word? made = null;

        foreach (var card in fillers)
        {
            if (!Socketing.HasRoom(into))
                break;

            Item lying;
            try
            {
                lying = ItemBlob.Restore(card.Row.Blob, store.StatIdBits);
            }
            catch (Exception problem)
            {
                PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
                refused = L($"{card.Name} not read: {problem.Message}");
                break;
            }

            var taken = Socketing.Take(lying, hostName, stamp, PD2SPCompanion.Core.Saves.ItemSeason.Mix, Random.Shared);
            if (taken.Refused is { } no)
            {
                refused = no;
                break;
            }

            var filler = taken.Filler!;
            if (taken.Left > 0)
            {
                lying.Quantity = (ushort)taken.Left;
                fewer.Add((card.Key, lying));
            }
            else
            {
                gone.Add(card.Key);
            }

            last = ItemPresenter.Describe(filler).Name;
            made = Socketing.Put(into, filler, Random.Shared) ?? made;
            went++;
        }

        if (went == 0)
        {
            Say(refused ?? L("Nothing socketed."));
            return true;
        }

        // The item first, the gems inside it: should what follows fail, a gem is in the vault twice and none is lost.
        try
        {
            if (store.Replace(host.Key, into, out string? no) is null)
            {
                Say(L($"{hostName} not written to vault: {no}"));
                return true;
            }

            foreach (var (key, stack) in fewer)
                store.Replace(key, stack, out _);
            store.Remove(gone);
        }
        catch (Exception problem)
        {
            PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
            Say(L($"{hostName} not written to vault: {problem.Message}"));
            ReloadBoth();
            return true;
        }

        SelectedEntry = null;
        ReloadBoth();

        string said = made is not null
            ? L($"{last} socketed into {hostName}: {Runewords.DisplayName(made)} made.")
            : went == 1
                ? L($"{last} socketed into {hostName}.")
                : L($"{went} items socketed into {hostName}.");
        Say(refused is null ? said : said + " " + refused);
        return true;
    }

    // ---- the tools' Equip, for the card selected ---------------------------------------------------------------------

    /// <summary>The one card selected, not a pick of several.</summary>
    private ItemTileViewModel? OneCard => !IsPicking && SelectedEntry is ItemTileViewModel card ? card : null;

    private (int Slots, string? Why, bool IsGear, string Does)? WearChoice(bool mercenary) =>
        OneCard is { } card && Session.IsLoaded ? EquipChoice?.Invoke(card.Item, mercenary) : null;

    private bool InventoryBeside => IsOnRight?.Invoke() == true;

    public bool CanEquip => InventoryBeside && WearChoice(false) is { Slots: > 0 };

    public bool CanEquipMerc => InventoryBeside && WearChoice(true) is { Slots: > 0 };

    /// <summary>What the button does, and where it can do nothing, why: no character, the Items page not beside, the item's own reason.</summary>
    public string EquipHint => EquipSays(L("Equip selected item"), mercenary: false);

    public string EquipMercHint => EquipSays(L("Equip selected item on mercenary"), mercenary: true);

    private string EquipSays(string does, bool mercenary)
    {
        if (!Session.IsLoaded)
            return does + Environment.NewLine + L("Require open character.");

        if (OneCard is null)
            return does;

        if (!InventoryBeside)
            return does + Environment.NewLine + L("Require inventory page open.");

        return WearChoice(mercenary) is { Slots: 0, Why: { Length: > 0 } why } ? does + Environment.NewLine + why : does;
    }

    [RelayCommand]
    private void EquipSelected()
    {
        if (OneCard is { } card)
            Wear(card);
    }

    [RelayCommand]
    private void EquipSelectedOnMerc()
    {
        if (OneCard is { } card)
            Wear(card, mercenary: true);
    }

    /// <summary>The selection changed, or what stands beside this page: the two buttons say what they would do now.</summary>
    public void SayEquipTools()
    {
        OnPropertyChanged(nameof(CanEquip));
        OnPropertyChanged(nameof(CanEquipMerc));
        OnPropertyChanged(nameof(EquipHint));
        OnPropertyChanged(nameof(EquipMercHint));
        OnPropertyChanged(nameof(CanIdentify));
        OnPropertyChanged(nameof(IdentifyHint));
    }
}
