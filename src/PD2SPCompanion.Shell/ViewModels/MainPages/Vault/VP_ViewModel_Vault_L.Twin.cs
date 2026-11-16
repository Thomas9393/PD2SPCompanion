using System.Linq;
using CommunityToolkit.Mvvm.Input;

using System;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// The vault on both pages of the book: two vault pages over the one vault, each its own - its folder, its ways back and forward, its
/// tree, its scroll, its selection, its tools - and what is carried from one let go of on the other moves there.
/// <para>
/// The left page is the first of the two: it has the Compare button that puts the vault on the right page as
/// well, it is the one the Edit page edits from, and what arrives from elsewhere lands in its folder. The right
/// page is the one beside the inventory, where the two trade items.
/// </para>
/// <para>
/// One search at a time, and it is the page's that began it: while its finds are listed the other vault page
/// stands under a dark layer and takes nothing.
/// </para>
/// </summary>
public sealed partial class VP_ViewModel_Vault_L
{
    /// <summary>The names of the two pages (<see cref="PageName"/>).</summary>
    public const string LeftPage = "Left", RightPage = "Right";

    /// <summary>The other vault page. Null where there is one alone - a test, the designer.</summary>
    public VP_ViewModel_Vault_L? Twin { get; set; }

    /// <summary>The left page: the first of the two.</summary>
    public bool IsPrimary => PageName == LeftPage;

    /// <summary>The folder shown. Null is the vault's top.</summary>
    public long? Here => _here;

    /// <summary>Whether the book shows this page now (the main view model's to say).</summary>
    public Func<bool>? InSight { get; set; }

    /// <summary>The page has been in sight: before that it has no folder of its own to speak of.</summary>
    public bool WasShown { get; private set; }

    /// <summary>
    /// Shown for the first time while the other page has been about: it opens where the other is,
    /// and goes its own way from there.
    /// </summary>
    public void OpenWhereTwinIs()
    {
        if (WasShown || Twin is not { WasShown: true } twin || twin._here == _here)
            return;

        _here = twin._here;
        SayHere();
    }

    // ---- compare -----------------------------------------------------------------------------------------

    /// <summary>Whether the vault is on both pages: the main view model's to say, and to change.</summary>
    public Func<bool>? Compares { get; set; }

    public Action? CompareRequested { get; set; }

    public bool IsComparing => Compares?.Invoke() == true;

    /// <summary>The left page's button: the vault on the right page too, or off it again.</summary>
    [RelayCommand]
    private void ToggleCompare()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        CompareRequested?.Invoke();
    }

    /// <summary>
    /// Ctrl + Shift + right click on a card while the vault is on both pages, the same interaction: the card - every one acted on, where it is one of
    /// them - into the folder the other page shows, which scrolls to where it landed. The click is the drag
    /// without the carrying, as between the inventory's two panes (VP_ViewModel_Inv_L.SendAcross).
    /// </summary>
    public void SendAcross(ItemTileViewModel card)
    {
        if (IsCarrying)
            return;

        if (!IsComparing || Twin is not { } twin)
        {
            _sfxManager?.Method_ButtonSFX_Press();
            Notice = L("Require vault on both pages.");
            return;
        }

        if (twin._here == _here)
        {
            _sfxManager?.Method_ButtonSFX_Press();
            Notice = L("Already in this folder.");
            return;
        }

        var going = Acting.Contains(card) ? Acting : [card];
        long first = going[0].Key;
        string name = twin._here is { } there ? _folders.FirstOrDefault(each => each.Id == there)?.Name ?? TopName : TopName;

        _sfxManager?.Method_ButtonSFX_Press();
        MoveInto([.. going.Select(each => each.Key)], [], twin._here, name);
        twin.ShowArrival(first);
    }

    /// <summary>
    /// What has just arrived in the folder shown brought into sight, as a find is gone to - and not selected:
    /// the page it came from is the one being worked on.
    /// </summary>
    private void ShowArrival(long key)
    {
        if (Store is null || !_spots.TryGetValue(key, out var spot))
            return;

        int on = spot.Page, column = spot.X;
        LoadPages([PageAt(on)]);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => PageSought?.Invoke(on, column), Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>The book's pages have changed: whether the two are compared, and whose the search and the arrivals are.</summary>
    public void BookChanged()
    {
        OnPropertyChanged(nameof(IsComparing));
        SayHere();
    }

    // ---- where things from elsewhere land ---------------------------------------------------------------------

    /// <summary>
    /// Whether what is stored from elsewhere lands in this page's folder (the main view model's to say: the
    /// left page's while it shows the vault, else the right's, else whichever did last). Alone, it does.
    /// </summary>
    public Func<bool>? TakesArrivals { get; set; }

    /// <summary>The folder shown, told to the vault where this page takes what arrives.</summary>
    private void SayHere()
    {
        if (_vaults is not null && TakesArrivals?.Invoke() != false)
            _vaults.CurrentFolder = _here;
    }

    // ---- what is carried, from this page or the other -----------------------------------------------------------

    /// <summary>The page whose cards are being carried: this one, or the other where its drag is over this page.</summary>
    private VP_ViewModel_Vault_L? Carrier => IsCarrying ? this : Twin is { IsCarrying: true } twin ? twin : null;

    /// <summary>Cards of the vault are being carried, from either page: this page may be where they are let go of.</summary>
    public bool IsCarriedOver => Carrier is not null;

    /// <summary>What is carried let go of on this page where nothing takes it: the carry ends, whoever's it is.</summary>
    public void EndCarryOver() => Carrier?.EndCarry();

    /// <summary>This page changed the vault: itself read again, and the other page, which shows the same vault.</summary>
    private void ReloadBoth()
    {
        Reload();

        if (Twin is { WasShown: true } twin)
            twin.Reload();
    }

    // ---- the search, of the two -------------------------------------------------------------------------------------

    /// <summary>
    /// The search is the left page's, always. While one stands the right page is its own - the item search's
    /// page, or the Grail with a card chosen - so the vault is never on the right under a search.
    /// </summary>
    public bool OwnsSearch => IsPrimary;

    /// <summary>A search stands and it is this page's: its finds are lit on the sheet.</summary>
    public bool IsSearchingHere => IsSearching && OwnsSearch;
}
