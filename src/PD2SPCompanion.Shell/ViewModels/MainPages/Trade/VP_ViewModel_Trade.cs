using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Vault;
using D2SLib.Model.Save;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// Virtual Trading, the Items page's Trade bookmark. A PD2 trade listing's link goes in, and the item
/// it sells is rebuilt and shown as the game would show it.
/// <para>
/// Confirm opens the payment - see <see cref="PaymentViewModel"/> - counted out of the shared
/// stash's materials tab at the player's own price table.
/// </para>
/// </summary>
public partial class VP_ViewModel_Trade : ViewModelBase
{
    public CharacterSession Session { get; }

    /// <summary>Used by the designer, which gets no services.</summary>
    public VP_ViewModel_Trade()
        : this(new CharacterSession(), new EditorSettings(), null!, new PlayRules(new EditorSettings()), null!, null!, null, null, null!) { }

    public VP_ViewModel_Trade(CharacterSession session, EditorSettings settings, SharedStashSession stash, PlayRules rules,
                               VaultService vaults, ItemHistory history, GameSync? sync, VaultMoves? moves,
                               ItemLanding landing)
    {
        _landing = landing;
        Session = session;
        _settings = settings;
        _follow = new WhenShown(Offer);
        _stash = stash;
        _rules = rules;
        _vaults = vaults;
        _history = history;
        _moves = moves;

        // Beside the vault, the Sell tab offers the card selected there.
        if (vaults is not null)
        {
            vaults.ChosenChanged += () =>
            {
                if (WithVault)
                    FollowLeft();
            };
        }
        Payment = new PaymentViewModel(settings, stash, Settle, sync);

        // The panel's own close: a purchase's, or a sale's with its search.
        Payment.CloseRequested += () =>
        {
            IsPaymentOpen = false;
            IsSaleOpen = false;
        };

        // The HR held follows the tab's counts and the price table's worths.
        if (stash is not null)
            stash.MaterialsChanged += () => OnPropertyChanged(nameof(HoldingText));
        settings.TradePricesChanged += () => OnPropertyChanged(nameof(HoldingText));

        // The marked item belongs to the save that was open; a reload or a close takes it away.
        session.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(CharacterSession.IsLoaded) && !session.IsLoaded)
                ClearSell();
        };
    }

    private readonly PlayRules _rules;

    /// <summary>The vault, whose ledger says what was bought before.</summary>
    private readonly VaultService? _vaults;

    /// <summary>Where a purchase is written down, with the save.</summary>
    private readonly ItemHistory? _history;

    /// <summary>The vault's side of a save: what a trade beside the vault brings into it and takes out.</summary>
    private readonly VaultMoves? _moves;

    /// <summary>Where a purchase lands beside the inventory: the page a pane shows, then the first room.</summary>
    private readonly ItemLanding _landing;

    /// <summary>
    /// Whether the left page shows the vault (the main view model, as the bookmarks change). Trading works with
    /// whichever is beside it, with no bouncing back to the inventory (until then Trade put the inventory back on the left, and the vault
    /// taken there closed Trade): beside the vault a purchase goes into the folder it shows, as the Forge's item
    /// does, and the Sell tab offers the card selected there. The materials tab pays and is paid either way.
    /// </summary>
    [ObservableProperty] private bool _withVault;

    partial void OnWithVaultChanged(bool value) => FollowLeft();

    private readonly SharedStashSession? _stash;

    private readonly EditorSettings _settings;

    // ---- buying or selling -------------------------------------------------------------

    /// <summary>Which side of a trade the page is on: a listing's link to buy, or an item marked to sell.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelling))]
    [NotifyPropertyChangedFor(nameof(ShowsItem))]
    [NotifyPropertyChangedFor(nameof(ShowsLinkHint))]
    [NotifyPropertyChangedFor(nameof(ShowsLoading))]
    [NotifyPropertyChangedFor(nameof(ShowsSellItem))]
    [NotifyPropertyChangedFor(nameof(ShowsSellHint))]
    [NotifyPropertyChangedFor(nameof(ShowsNotes))]
    private bool _isBuying = true;

    public bool IsSelling => !IsBuying;

    /// <summary>The listing's item, in the frame while buying.</summary>
    public bool ShowsItem => IsBuying && HasItem;

    /// <summary>The empty frame's line while buying, pointing at the link box.</summary>
    public bool ShowsLinkHint => IsBuying && !HasItem && !IsLoading;

    /// <summary>The frame's line while a listing is fetched, in place of the link's.</summary>
    public bool ShowsLoading => IsBuying && !HasItem && IsLoading;

    [RelayCommand]
    private void ShowBuy() => IsBuying = true;

    [RelayCommand]
    private void ShowSell() => IsBuying = false;

    /// <summary>The trade site itself, in the browser - the way the landing page's links open.</summary>
    [RelayCommand]
    private void OpenTradeSite() => HyperText.Avalonia.Extensions.OpenUrlExtension.OpenUrl("https://www.projectdiablo2.com/market");

    // ---- the price table ---------------------------------------------------------------

    /// <summary>
    /// What a trade can be paid in: Worldstone Shards, Larzuk's Puzzleboxes and the runes from
    /// Lem (#20) to Zod (#33) - the currencies PD2's market prices in.
    /// </summary>
    internal static readonly string[] Currencies =
        ["wss", "lbox", .. Enumerable.Range(20, 14).Select(number => $"r{number}")];

    // ---- confirming and paying ---------------------------------------------------------

    /// <summary>The payment, opened from Confirm.</summary>
    public PaymentViewModel Payment { get; }

    [ObservableProperty] private bool _isPaymentPresent;

    [ObservableProperty] private bool _isPaymentOpen;

    partial void OnIsPaymentOpenChanged(bool value) =>
        Presence.Keep(value, () => IsPaymentOpen, on => IsPaymentPresent = on);

    /// <summary>Buy and Clear stay under the frame, off until there is something to act on.</summary>
    private bool CanConfirm => HasItem;

    private bool CanClear => HasItem || Link.Length > 0;

    /// <summary>
    /// Opens the payment for the listing's item, starting from its seller's price where one could
    /// be read ("18 HR", "5wss"); the player sets what they actually pay.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        if (Ready() is { } refused)
        {
            Session.RightMessages.Post(refused);
            return;
        }

        Payment.Start(TradePayment.Guess(ListingPrice, _settings.TradePrices.GetValueOrDefault("wss")), PriceText);
        IsPaymentOpen = true;
    }

    [RelayCommand]
    private void ClosePayment() => IsPaymentOpen = false;

    /// <summary>
    /// The item to hand over, built for the character's own save - its sockets as a save keeps
    /// them, a rare named - and proven to write and read back before a rune is asked for. Null
    /// when it can be bought, otherwise why not.
    /// </summary>
    private string? Ready()
    {
        _buying = null;

        if (_listing is null || Item is null)
            return L("Require listing loaded.");

        // Bought before is said, not refused (see BoughtBefore): looked at again here, the play mode
        // or the ledger having perhaps changed since the listing was shown.
        BoughtBefore = Known(_listing);

        // The listing is the network's: an answer the rebuild cannot read is one more item it cannot recreate.
        Forged made;
        try
        {
            made = TradeItems.Build(_listing.Item, Session.ItemEra is { } era ? PD2SPCompanion.Core.Saves.ItemSeason.ForNew(era) : null);
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            return L("Item cannot be recreated exactly.");
        }

        if (!made.Sound)
            return L("Item cannot be recreated exactly.");

        Session.UseMyLayout();
        if (!ItemBlob.TryCapture(made.Item, D2SLib.Globals.stat_id_bits, out _, out _, out string? unwritable))
            return L($"Item cannot be written: {unwritable}");

        _buying = made.Item;
        return null;
    }

    /// <summary>The item Confirm built, waiting for the payment.</summary>
    private Item? _buying;

    /// <summary>
    /// Why the listing on show is one bought before, where the play mode buys once (its Repeat buys switch off,
    /// Legit's): the same listing, the same item relisted, or the same item posted twice - bought and saved, or
    /// bought and waiting for the save. <b>A warning beside the price, not a refusal</b>, even in Legit: a ! sign in the
    /// trade panel - it refused at Confirm until then. Trading is an honor system: the editor says, the player decides.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WasBoughtBefore))]
    private string? _boughtBefore;

    public bool WasBoughtBefore => BoughtBefore is not null;

    private string? Known(TradeListings.Listing listing)
    {
        if (_rules.BuysTwice)
            return null;

        if (_history?.IsBuying(listing.Id, listing.ItemId, listing.ItemHash) == true)
            return L("Already bought.");

        return _vaults?.Store?.BoughtBefore(listing.Id, listing.ItemId, listing.ItemHash) is { } before
            ? L($"Bought before: {before.Character}, {before.At.ToLocalTime():dd/MM/yyyy}.")
            : null;
    }

    /// <summary>
    /// The trade: the item into the first container with room - inventory, cube, stash, then the
    /// shared stash - or, beside the vault, into the folder it shows; and the materials tab moved by what
    /// was paid and given back. Everything is checked before anything moves, the item placed before the
    /// currency, so a refusal leaves both files as they were. In memory: Save writes them, with backups,
    /// as any change - and the vault with them (VaultMoves.Bought).
    /// </summary>
    private string? Settle(TradePayment.Plan plan)
    {
        if (Payment.IsReceiving)
            return SettleSale(plan);

        if (_buying is not { } item)
            return L("Nothing to buy.");

        if (_stash is not { IsLoaded: true } stash)
            return L("No shared stash open.");

        // Beside the vault: proven to go into it before a rune moves.
        bool toVault = WithVault;
        if (toVault)
        {
            if (_moves is null || _vaults?.Store is not { } store)
                return _vaults?.Trouble is { Length: > 0 } trouble ? trouble : L("No vault open.");

            Session.UseMyLayout();
            if (store.Capture(item, out string? unkept) is null)
                return L($"Item cannot be written: {unkept}");
        }

        var moves = new List<(int Slot, int Count)>();
        foreach (string code in Currencies)
        {
            int net = plan.NetOf(code);
            if (net == 0 || Materials.SlotFor(code) is not { } slot)
                continue;

            int now = stash.MaterialCount(slot);
            if (now + net < 0)
                return L($"Materials tab has only {now} {Materials.NameOf(slot)}.");
            if (now + net > Materials.FieldMaximum)
                return L($"Materials tab cannot hold that many {Materials.NameOf(slot)}.");

            moves.Add((slot, now + net));
        }

        if (!toVault && Place(item) is { } noRoom)
            return noRoom;

        foreach (var (slot, count) in moves)
            stash.SetMaterial(slot, count);

        string name = ItemPresenter.Describe(item).Name;

        if (_listing is { } listing)
        {
            _history?.Traded(new TradeRecord(
                DateTimeOffset.Now, Session.CharacterName, "bought",
                listing.Id, listing.ItemId, listing.ItemHash, null,
                name, ListingPrice.Trim(), PaidText(plan), _settings.PlayMode), item);
        }
        // After the ledger and the currency: the mark asks for a save, which writes all three.
        if (toVault)
            _moves!.Bought(item, _vaults!.CurrentFolder);

        IsPaymentOpen = false;
        Clear();

        // The item lands on the left page, off this one: its strip says so.
        Session.Messages.Post(toVault ? L($"Bought {name} to vault.") : L($"Bought {name}."));
        return null;
    }

    private static string NotShown(string what) => L($"Not shown: {what}");

    /// <summary>What moved, in words for the ledger: "2 Ist Rune, 1 Vex Rune; back 1 Lem Rune".</summary>
    private static string PaidText(TradePayment.Plan plan)
    {
        string paid = CountsText(plan.Paid), back = CountsText(plan.Change);
        return (paid.Length > 0 ? paid : "nothing") + (back.Length > 0 ? $"; back {back}" : "");
    }

    /// <summary>"2 Ist Rune, 1 Vex Rune", in the price table's order.</summary>
    private static string CountsText(IReadOnlyDictionary<string, int> counts) =>
        string.Join(", ", Currencies.Where(code => counts.GetValueOrDefault(code) > 0)
                                    .Select(code => $"{counts[code]} {ItemPresenter.BaseName(code)}"));

    // ---- the records ("Recorded and never shown") -------------------

    /// <summary>
    /// One line of the trade records: the item, which way it went and at what price, what moved for it, and
    /// under it when, who and in which play mode.
    /// </summary>
    public sealed record TradeLine(string Name, string Deal, string Moved, string About, bool IsSale)
    {
        /// <summary>
        /// The row whole, a line a part: what its tooltip says. A price is the seller's own words off the trade
        /// site, as long as they wrote them, and the row cuts what does not fit.
        /// </summary>
        public string Tip => string.Join(Environment.NewLine, new[] { Name, Deal, Moved, About }.Where(part => part.Length > 0));
    }

    /// <summary>
    /// Every trade the vault's ledger holds (Vault.Trades), newest first: read as the list opens. The ledger
    /// is the mode's - softcore's trades and hardcore's are apart, as their vaults are - and every character's.
    /// </summary>
    public System.Collections.ObjectModel.ObservableCollection<TradeLine> Records { get; } = [];

    public bool HasRecords => Records.Count > 0;

    /// <summary>How many, beside the list's title.</summary>
    public string RecordsCount => Plural(Records.Count, "1 trade", $"{Records.Count} trades");

    [ObservableProperty] private bool _isRecordsOpen;

    [ObservableProperty] private bool _isRecordsPresent;

    partial void OnIsRecordsOpenChanged(bool value) =>
        Presence.Keep(value, () => IsRecordsOpen, on => IsRecordsPresent = on);

    [RelayCommand]
    private void ShowRecords()
    {
        Records.Clear();
        foreach (var trade in (_vaults?.Store?.Trades() ?? []).Reverse())
            Records.Add(LineOf(trade));

        OnPropertyChanged(nameof(HasRecords));
        OnPropertyChanged(nameof(RecordsCount));
        IsRecordsOpen = true;
    }

    [RelayCommand]
    private void CloseRecords() => IsRecordsOpen = false;

    /// <summary>
    /// A record as the list says it. What moved was written down in words when the trade was made
    /// (<see cref="PaidText"/>: "2 Ist Rune; back 1 Gul Rune", or a sale's "received 2 Ist Rune"), and is
    /// said as it was written - the names of that day, whatever the tables call them now.
    /// </summary>
    private static TradeLine LineOf(TradeRecord trade)
    {
        bool sale = trade.Kind.Equals("sold", StringComparison.OrdinalIgnoreCase);

        string moved = trade.Paid.Trim();
        const string received = "received ";
        string said = sale
            ? moved.StartsWith(received, StringComparison.OrdinalIgnoreCase) ? L($"Received {moved[received.Length..]}")
                : L("Nothing received")
            : moved.Length == 0 || moved.Equals("nothing", StringComparison.OrdinalIgnoreCase) ? L("Nothing paid")
                : L($"Paid {moved}");

        string price = trade.Price.Trim();
        string deal = sale
            ? price.Length > 0 ? L($"Sold · {price}") : L("Sold")
            : price.Length > 0 ? L($"Bought · {price}") : L("Bought");

        string when = trade.At.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        string about = string.Join(" · ", new[] { when, trade.Character, trade.Mode }.Where(part => part.Length > 0));

        return new TradeLine(trade.Name, deal, said, about, sale);
    }

    // ---- selling -----------------------------------------------------------------------

    /// <summary>
    /// The item up for sale: one of the character's or the shared stash's own, or beside the vault one of
    /// its cards', which leaves when the sale goes through - the one selected on the left page (see
    /// <see cref="Follow"/>, <see cref="FollowLeft"/>).
    /// </summary>
    private Item? _selling;

    /// <summary>The vault row up for sale, where it is a card's; null for an item of the character's.</summary>
    private long? _sellingRow;

    /// <summary>The marked item, drawn as a tile is; null before one is marked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSellItem))]
    [NotifyPropertyChangedFor(nameof(ShowsSellItem))]
    [NotifyPropertyChangedFor(nameof(ShowsSellHint))]
    [NotifyCanExecuteChangedFor(nameof(OpenSaleCommand))]
    private ItemTileViewModel? _sellItem;

    public bool HasSellItem => SellItem is not null;

    public bool ShowsSellItem => IsSelling && HasSellItem;

    /// <summary>The empty frame's line while selling: nothing, or several, selected on the inventory.</summary>
    public bool ShowsSellHint => IsSelling && !HasSellItem;

    /// <summary>The materials tab's worth in HR, at the page's head: "Materials: 12.4 HR", as the payment's panel says it.</summary>
    public string HoldingText => Payment.HoldingText;

    /// <summary>
    /// The inventory's selection changed: the Sell tab offers the one item selected, its search
    /// ready, or nothing with none or several selected. The tab is left as it
    /// is - selecting an item never takes the page off Buy. Kept, not offered, while the vault is
    /// beside the page (<see cref="WithVault"/>).
    /// </summary>
    public void Follow(Item? item)
    {
        _inventoryChoice = item;
        if (!WithVault)
            FollowLeft();
    }

    /// <summary>
    /// The left page's choice taken up: the inventory's one item, or beside the vault the one card selected
    /// there - nothing while several are picked, as on the inventory.
    /// </summary>
    private void FollowLeft()
    {
        var card = WithVault ? _vaults?.Chosen : null;
        (_followed, _followedRow) = WithVault ? (card?.Item, card?.Key) : (_inventoryChoice, null);

        // Hidden, the page only notes the choice: building the sale's search cost 6-8 ms of every
        // item selected on the Items page (measured on the published build).
        _follow.Ask();
    }

    /// <summary>The inventory's own choice, kept while the vault is beside the page: it is the choice again when the inventory is back.</summary>
    private Item? _inventoryChoice;

    /// <summary>The left page's choice, offered once the page is shown (<see cref="Shown"/>); and its row, where it is a vault card's.</summary>
    private Item? _followed;

    private long? _followedRow;

    private readonly WhenShown _follow;

    /// <summary>In front, or put away (the view, through PageDeck).</summary>
    public void Shown() => _follow.Shown();

    public void Hidden() => _follow.Hidden();

    private void Offer()
    {
        var (item, row) = (_followed, _followedRow);

        // A vault's card is made again whenever its list is: the same row is the same thing for sale.
        if (ReferenceEquals(item, _selling) || (item is not null && row is not null && row == _sellingRow))
        {
            _selling = item ?? _selling;
            return;
        }

        if (item is null)
        {
            ClearSell();
            return;
        }

        Search?.Stop();
        IsSaleOpen = false;
        _selling = item;
        _sellingRow = row;
        SellItem = new ItemTileViewModel(item, 0);
        Search = new SellSearchViewModel(item, Session.IsHardcore);
    }

    /// <summary>Nothing up for sale: none or several selected on the inventory, or the sale made.</summary>
    private void ClearSell()
    {
        _followed = null;
        _followedRow = null;
        Search?.Stop();
        IsSaleOpen = false;
        _selling = null;
        _sellingRow = null;
        SellItem = null;
        Search = null;
    }

    /// <summary>The search for listings like the marked item - the first half of a sale.</summary>
    [ObservableProperty] private SellSearchViewModel? _search;

    /// <summary>
    /// A sale under way: the search and the price in one panel, so the listings stay in sight while
    /// the price is set.
    /// </summary>
    [ObservableProperty] private bool _isSaleOpen;

    [ObservableProperty] private bool _isSalePresent;

    partial void OnIsSaleOpenChanged(bool value)
    {
        // The search's property picker is the sale's, and goes with it.
        if (!value && Search is { } search)
            search.Form.Properties.IsOpen = false;

        Presence.Keep(value, () => IsSaleOpen, on => IsSalePresent = on);
    }

    [RelayCommand]
    private void CloseSale() => IsSaleOpen = false;

    /// <summary>The sale panel's two folds, open to begin with.</summary>
    [ObservableProperty] private bool _isSearchSectionOpen = PD2SPCompanion.Shell.Services.Settings.FoldMemory.IsOpen(nameof(IsSearchSectionOpen));

    partial void OnIsSearchSectionOpenChanged(bool value) => PD2SPCompanion.Shell.Services.Settings.FoldMemory.Keep(nameof(IsSearchSectionOpen), value);

    [ObservableProperty] private bool _isSaleSectionOpen = PD2SPCompanion.Shell.Services.Settings.FoldMemory.IsOpen(nameof(IsSaleSectionOpen));

    partial void OnIsSaleSectionOpenChanged(bool value) => PD2SPCompanion.Shell.Services.Settings.FoldMemory.Keep(nameof(IsSaleSectionOpen), value);

    [RelayCommand]
    private void ToggleSearchSection() => IsSearchSectionOpen = !IsSearchSectionOpen;

    [RelayCommand]
    private void ToggleSaleSection() => IsSaleSectionOpen = !IsSaleSectionOpen;

    /// <summary>Whether the marked item is still one of the character's or the stash's - or, a vault card's, still a row there and not on its way out.</summary>
    private bool StillHeld(Item item) => _sellingRow is { } key
        ? _vaults?.Store?.RowsOf([key]).Count == 1 && _moves?.IsWithdrawing(key) != true
        : Session.Holds(item) || _stash?.Holds(item) == true;

    /// <summary>Sell's $ stays under the frame, off with nothing selected.</summary>
    private bool CanOpenSale => HasSellItem;

    /// <summary>
    /// Opens the sale: the search beside the payment the other way round - the player names the
    /// price, the currency comes in.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpenSale))]
    private void OpenSale()
    {
        if (_selling is not { } item)
            return;

        if (!StillHeld(item))
        {
            Session.RightMessages.Post(L("Item no longer there."));
            return;
        }

        Payment.Start(null, L($"Selling {ItemPresenter.Describe(item).Name}"), receiving: true);
        IsSaleOpen = true;
    }

    /// <summary>
    /// The sale: the currency into the materials tab and the item out of wherever it is - the
    /// character, the shared stash, or beside the vault its row (VaultMoves.Sold) - checked before
    /// anything moves, so a refusal leaves both files as they were. In memory until Save, and
    /// written into the ledger with it.
    /// </summary>
    private string? SettleSale(TradePayment.Plan plan)
    {
        if (_selling is not { } item)
            return L("Nothing to sell.");

        if (_stash is not { IsLoaded: true } stash)
            return L("No shared stash open.");

        bool fromVault = _sellingRow is not null, inStash = false;
        if (fromVault)
        {
            if (_moves is null || _vaults?.Store is null)
                return _vaults?.Trouble is { Length: > 0 } trouble ? trouble : L("No vault open.");
            if (!StillHeld(item))
                return L("Item no longer there.");
        }
        else
        {
            // Worn sells as carried does - the character's or the mercenary's - but for a belt still
            // holding potions it has no room to leave behind.
            if (Session.CannotTakeOff(item) is { } stuck)
                return stuck;

            inStash = !Session.Holds(item);
            if (inStash && !stash.Holds(item))
                return L("Item no longer there.");
        }

        var moves = new List<(int Slot, int Count)>();
        foreach (string code in Currencies)
        {
            int net = plan.NetOf(code);
            if (net == 0 || Materials.SlotFor(code) is not { } slot)
                continue;

            int now = stash.MaterialCount(slot);
            if (now + net > Materials.FieldMaximum)
                return L($"Materials tab cannot hold that many {Materials.NameOf(slot)}.");

            moves.Add((slot, now + net));
        }

        string name = ItemPresenter.Describe(item).Name;

        if (!fromVault && (inStash ? stash.Release(item) : Session.Release(item)) is null)
            return L("Item not taken out.");

        foreach (var (slot, count) in moves)
            stash.SetMaterial(slot, count);

        string received = CountsText(plan.Change);
        _history?.Traded(new TradeRecord(
            DateTimeOffset.Now, Session.CharacterName, "sold",
            null, null, null, null,
            name, $"{Payment.Price.Trim()} HR", received.Length > 0 ? $"received {received}" : "nothing",
            _settings.PlayMode), item);

        // After the ledger and the currency: the mark asks for a save, which writes all three.
        if (fromVault)
            _moves!.Sold(_sellingRow!.Value);

        IsSaleOpen = false;
        ClearSell();

        // The item leaves the left page, off this one: its strip says so.
        Session.Messages.Post(fromVault ? L($"Sold {name} from vault.") : L($"Sold {name}."));
        return null;
    }

    /// <summary>
    /// The item onto the page a pane shows - a shared one too - then into the first container with room: the
    /// character's, then any page of the shared stash (ItemLanding).
    /// </summary>
    private string? Place(Item item)
    {
        var (placed, _, _, refused) = _landing.Welcome([item]);
        if (placed == 1)
            return null;
        if (refused is not null)
            return refused;

        foreach (var page in _stash?.Pages ?? [])
        {
            var room = new ItemMover.Occupancy(new GridSize(page.Columns, page.Rows), page.Items.Select(tile => tile.Item));
            if (ItemMover.FirstFreeCell(item, room) is { } cell)
                return _stash!.Adopt(item, page.Page, cell.X, cell.Y);
        }

        return L("No free space.");
    }

    /// <summary>Forgets the listing: the link, its item, its price.</summary>
    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        _loading?.Cancel();
        _shownId = null;
        _listing = null;
        _buying = null;
        BoughtBefore = null;
        Link = "";
        Item = null;
        Notes = [];
        ListingPrice = "";
        IsLinkInvalid = false;
        IsLoading = false;
    }

    /// <summary>The seller's price, as they typed it - free text, shown as it is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PriceText))]
    private string _listingPrice = "";

    public string PriceText => ListingPrice.Trim().Length > 0 ? L($"Price: {ListingPrice.Trim()}") : L("No price");

    /// <summary>What was pasted. A whole listing link shows its item at once - no button to press.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    private string _link = "";

    /// <summary>The item the link sells, drawn as a tile is; null before one is shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItem))]
    [NotifyPropertyChangedFor(nameof(PriceOpacity))]
    [NotifyPropertyChangedFor(nameof(ShowsItem))]
    [NotifyPropertyChangedFor(nameof(ShowsLinkHint))]
    [NotifyPropertyChangedFor(nameof(ShowsLoading))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    private ItemTileViewModel? _item;

    public bool HasItem => Item is not null;

    /// <summary>The price line dims with the buttons beside it while there is no listing.</summary>
    public double PriceOpacity => HasItem ? 1 : 0.25;

    /// <summary>
    /// The box holds something that is not a listing's link: its frame red, as a bad number's is - a
    /// state of the box, said on it, not a message per keystroke.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LinkProblem))]
    private bool _isLinkInvalid;

    /// <summary>The red box's tooltip.</summary>
    public string? LinkProblem => IsLinkInvalid ? L("Not a trade listing link") : null;

    /// <summary>A listing on its way: the empty frame says so.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsLinkHint))]
    [NotifyPropertyChangedFor(nameof(ShowsLoading))]
    private bool _isLoading;

    /// <summary>What the listing had that could not be carried over, one line each.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotes))]
    [NotifyPropertyChangedFor(nameof(ShowsNotes))]
    private IReadOnlyList<string> _notes = [];

    public bool HasNotes => Notes.Count > 0;

    /// <summary>The notes are the listing's: shown while buying only.</summary>
    public bool ShowsNotes => IsBuying && HasNotes;

    private string? _shownId;

    /// <summary>The listing on show, kept to build the item a save holds when it is bought.</summary>
    private TradeListings.Listing? _listing;
    private CancellationTokenSource? _loading;

    partial void OnLinkChanged(string value) => _ = Show(value);

    private async Task Show(string link)
    {
        _loading?.Cancel();

        IsLoading = false;
        IsLinkInvalid = false;

        if (string.IsNullOrWhiteSpace(link))
            return;

        if (TradeListings.IdIn(link) is not { } id)
        {
            IsLinkInvalid = true;
            return;
        }

        if (id == _shownId)
            return;

        var loading = _loading = new CancellationTokenSource();
        IsLoading = true;

        try
        {
            var listing = await TradeListings.Get(id, loading.Token);
            if (loading.IsCancellationRequested)
                return;

            // As the game's tooltip shows it: what the runes and jewels grant counted in.
            var made = TradeItems.Build(listing.Item, shown: true);

            Item = new ItemTileViewModel(made.Item, 0);
            ListingPrice = listing.Price;
            _listing = listing;
            BoughtBefore = Known(listing);
            Notes = [.. made.Notes.Where(note => note.StartsWith("left off", StringComparison.Ordinal))
                                  .Select(note => NotShown(note["left off: ".Length..]))];
            _shownId = id;
        }
        catch (OperationCanceledException) when (loading.IsCancellationRequested)
        {
            // Another link came in behind it.
        }
        catch (TradeListings.TradeListingException problem)
        {
            Session.RightMessages.Post(problem.Message);
        }
        catch (Exception)
        {
            Session.RightMessages.Post(L("Rebuild failed."));
        }
        finally
        {
            // Only the newest fetch says it is done; one cancelled behind it leaves that to it.
            if (ReferenceEquals(_loading, loading))
                IsLoading = false;
        }
    }
}

/// <summary>
/// One listing like the item being sold: the item as the site shows it, its asking price as typed,
/// and how long ago it was posted.
/// </summary>
public sealed class SimilarListing
{
    public SimilarListing(TradeListings.Listing listing, Item item)
    {
        Tile = new ItemTileViewModel(item, 0);
        Price = listing.Price.Trim() is { Length: > 0 } asked ? asked : L("Offers");
        Age = listing.Record.TryGetProperty("created_at", out var created) && created.ValueKind == System.Text.Json.JsonValueKind.String
              && DateTimeOffset.TryParse(created.GetString(), CultureInfo.InvariantCulture,
                                         DateTimeStyles.AssumeUniversal, out var at)
            ? AgoText(DateTimeOffset.UtcNow - at)
            : "";
    }

    public ItemTileViewModel Tile { get; }

    public string Price { get; }

    public string Age { get; }

    private static string AgoText(TimeSpan ago) =>
        ago.TotalMinutes < 60 ? L($"{Math.Max(1, (int)ago.TotalMinutes)} min ago")
        : ago.TotalHours < 24 ? L($"{(int)ago.TotalHours} h ago")
        : L($"{(int)ago.TotalDays} d ago");
}

/// <summary>
/// One currency in the price table: its picture, its name, and what the player says it is worth
/// in HR. Kept as typed; saved whenever it reads as a number, cleared when emptied.
/// </summary>
public sealed partial class TradePriceRow : ObservableObject
{
    private readonly EditorSettings _settings;

    public TradePriceRow(string code, EditorSettings settings)
    {
        _settings = settings;
        Code = code;
        Tile = new ItemTileViewModel(ItemForge.Material(code, 1, ""), 0);
        // A rune by its own name, "Ber", as the market says it.
        Name = Runewords.RuneNumber(code) > 0 ? Runewords.RuneName(code) : ItemPresenter.BaseName(code);
        _worth = settings.TradePrices.TryGetValue(code, out decimal worth)
            ? worth.ToString(CultureInfo.InvariantCulture)
            : "";
    }

    public string Code { get; }

    public string Name { get; }

    /// <summary>The currency's own picture, as a tile draws it.</summary>
    public ItemTileViewModel Tile { get; }

    /// <summary>The worth as typed. A comma is read as a decimal point: "2,5" is 2.5, as sellers write it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInvalid))]
    private string _worth;

    /// <summary>Typed, and not a number the table can keep.</summary>
    public bool IsInvalid => Worth.Trim().Length > 0 && Parsed(Worth) is null;

    partial void OnWorthChanged(string value)
    {
        if (value.Trim().Length == 0)
            _settings.SetTradePrice(Code, null);
        else if (Parsed(value) is { } worth)
            _settings.SetTradePrice(Code, worth);
    }

    private static decimal? Parsed(string text) =>
        decimal.TryParse(text.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint,
                         CultureInfo.InvariantCulture, out decimal worth)
            ? worth
            : null;
}
