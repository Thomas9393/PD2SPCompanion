using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// Paying for a Virtual Trading purchase out of the shared stash's materials tab: a row per
/// priced currency, counted out the way a cash machine counts notes (see
/// <see cref="TradePayment"/>), and the HR the player chooses to pay underneath.
/// <para>
/// A sale is the same panel the other way round - see <see cref="IsReceiving"/>: the price the
/// player names counted <b>in</b>, limited only by what the tab can hold.
/// </para>
/// </summary>
public sealed partial class PaymentViewModel : ObservableObject
{
    private readonly EditorSettings _settings;
    private readonly SharedStashSession? _stash;
    private readonly Func<TradePayment.Plan, string?> _settle;

    private TradePayment.Plan? _plan;

    /// <summary>Counts the player set by hand; every other row is counted around them.</summary>
    private readonly Dictionary<string, int> _held = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="settle">
    /// Makes the trade once the player pays: the item handed over and the currency moved. Null
    /// when it went through, otherwise why not.
    /// </param>
    public PaymentViewModel(EditorSettings settings, SharedStashSession? stash, Func<TradePayment.Plan, string?> settle,
                            GameSync? sync = null)
    {
        _sync = sync;

        // A running game turns Pay off: a trade writes the character and the stash at the next
        // save - see GameSync.
        if (sync is not null)
        {
            sync.PropertyChanged += (_, changed) =>
            {
                if (changed.PropertyName is not (nameof(GameSync.IsGameRunning) or nameof(GameSync.IsWriteBlocked)))
                    return;

                OnPropertyChanged(nameof(PayBlocked));
                OnPropertyChanged(nameof(ActionTip));
                PayCommand.NotifyCanExecuteChanged();
            };
        }

        _settings = settings;
        _stash = stash;
        _settle = settle;
    }

    /// <summary>
    /// A sale rather than a purchase: the currency comes in. No minimum - selling for nothing only
    /// throws the item away - and no stock to run out of, only the room the tab has.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    [NotifyPropertyChangedFor(nameof(PriceLabel))]
    [NotifyPropertyChangedFor(nameof(ActionTip))]
    [NotifyPropertyChangedFor(nameof(ActionWord))]
    private bool _isReceiving;

    public string Title => IsReceiving ? L("Sale") : L("Payment");

    public string Subtitle => IsReceiving ? L("Currency into materials tab") : L("Currency from materials tab");

    public string PriceLabel => IsReceiving ? "Price:" : "Offer:";

    /// <summary>The button's word: PAY for a purchase, SELL for a sale.</summary>
    public string ActionWord => IsReceiving ? L("SELL") : L("PAY");

    public string ActionTip => PayBlocked ? (_sync?.BlockedFor(IsReceiving ? "sell" : "pay") ?? GameSync.Blocked(IsReceiving ? "sell" : "pay")) : IsReceiving ? L("Sell") : L("Pay");

    private readonly GameSync? _sync;

    /// <summary>The game is running: a trade would write its files at the next save. See GameSync.</summary>
    public bool PayBlocked => _sync?.IsWriteBlocked ?? false;

    /// <summary>What Pay's tick waits on: a payment that settles, and the game closed.</summary>
    private bool CanPayNow => CanPay && !PayBlocked;

    /// <summary>
    /// Picked by hand, whatever is to be offered: nothing is counted
    /// out. Each currency is the player's to set, the offer is what they come to, and that is what
    /// is paid - whatever the listing asks, in every play mode. The other mode, the one a payment
    /// opens in until this is chosen, counts the currencies out for an offer typed; the choice is
    /// kept while the editor runs. A purchase's only: a sale names its price.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOfferTyped))]
    [NotifyPropertyChangedFor(nameof(PickTip))]
    private bool _picksByHand;

    /// <summary>The offer's box takes a number: not while the offer is what was picked.</summary>
    public bool IsOfferTyped => IsReceiving || !PicksByHand;

    public string PickTip => PicksByHand ? L("Count for me") : L("Pick by hand");

    [RelayCommand]
    private void TogglePicking() => PicksByHand = !PicksByHand;

    /// <summary>
    /// Turned on, the counts stay where they were counted and are the player's from there; turned
    /// off, the offer they came to is counted out afresh.
    /// </summary>
    partial void OnPicksByHandChanged(bool value)
    {
        _held.Clear();
        if (value && !IsReceiving)
        {
            foreach (var row in Rows)
                _held[row.Code] = row.Paid;
        }

        Recount();
    }

    /// <summary>Each priced currency, in the price table's order.</summary>
    [ObservableProperty] private IReadOnlyList<PaymentRow> _rows = [];

    /// <summary>The HR the player pays, as typed. Theirs to set: an honor system.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPriceInvalid))]
    private string _price = "";

    public bool IsPriceInvalid => Price.Trim().Length > 0 && Parsed(Price) is null;

    /// <summary>The seller's price as they typed it, above the offer.</summary>
    [ObservableProperty] private string _priceText = "";

    /// <summary>What is paid and what comes back, or what is missing.</summary>
    [ObservableProperty] private string _summary = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PayCommand))]
    private bool _canPay;

    [ObservableProperty] private bool _isShort;

    /// <summary>
    /// Opens for a price: every row read afresh from the materials tab, nothing held, and the
    /// listing's own figure - where one could be read - to start from.
    /// </summary>
    public void Start(decimal? price, string priceText, bool receiving = false)
    {
        _held.Clear();
        IsReceiving = receiving;
        PriceText = priceText;

        // A Vex before a Puzzlebox where both are worth 0.5: runes are what the market pays in.
        Rows =
        [
            .. VP_ViewModel_Trade.Currencies
                .Where(code => _settings.TradePrices.TryGetValue(code, out decimal worth) && worth > 0)
                .Select(code => new PaymentRow(code, _settings.TradePrices[code], StockOf(code),
                                               receiving ? Materials.FieldMaximum - StockOf(code) : StockOf(code), this)),
        ];

        // The field on purpose (MVVMTK0034): the rows above are new, and Recount - all the change
        // handler does - runs once below, after both are in place.
#pragma warning disable MVVMTK0034
        _price = price is { } figure ? figure.ToString(CultureInfo.InvariantCulture) : "";
#pragma warning restore MVVMTK0034
        OnPropertyChanged(nameof(Price));
        OnPropertyChanged(nameof(IsPriceInvalid));
        OnPropertyChanged(nameof(HoldingText));
        OnPropertyChanged(nameof(IsOfferTyped));
        Recount();

        // Picking by hand starts from what the listing's price counts out to, not from nothing.
        if (PicksByHand && !receiving)
        {
            foreach (var row in Rows)
                _held[row.Code] = row.Paid;
            Recount();
        }
    }

    /// <summary>
    /// What the materials tab holds in HR, at the price table's worths - all the currency a trade
    /// can pay with or be paid into. Nothing outside the tab counts.
    /// </summary>
    public decimal Holding =>
        VP_ViewModel_Trade.Currencies.Sum(code =>
            _settings.TradePrices.TryGetValue(code, out decimal worth) && worth > 0 ? worth * StockOf(code) : 0m);

    /// <summary>
    /// The same, said over the currencies: "Materials tab: 4.19 HR" - what there is to pay with, in the
    /// panel where the paying is done (the page's header alone said it).
    /// </summary>
    public string HoldingText => L($"Materials: {Hr(Holding)} HR");

    private int StockOf(string code) =>
        _stash is { IsLoaded: true } && Materials.SlotFor(code) is { } slot ? _stash.MaterialCount(slot) : 0;

    partial void OnPriceChanged(string value) => Recount();

    /// <summary>A row set by hand: held at that count from now on.</summary>
    internal void Hold(PaymentRow row, int count)
    {
        _held[row.Code] = Math.Clamp(count, 0, row.Limit);
        Recount();
    }

    private void Recount()
    {
        if (PicksByHand && !IsReceiving && _held.Count > 0)
        {
            RecountPicked();
            return;
        }

        decimal price = Parsed(Price) ?? 0;

        var spendOrder = Rows
            .OrderByDescending(row => Runewords.RuneNumber(row.Code))
            .Select(row => new TradePayment.Currency(row.Code, row.Worth, row.Limit))
            .ToList();

        var plan = _plan = IsReceiving
            ? TradePayment.Receive(spendOrder, price, _held)
            : TradePayment.Make(spendOrder, price, _held);

        // A row's count is what changes hands in its currency: paid, or received.
        foreach (var row in Rows)
        {
            int moving = IsReceiving ? plan.Change.GetValueOrDefault(row.Code) : plan.Paid.GetValueOrDefault(row.Code);
            row.Show(moving, plan.NetOf(row.Code));
        }

        if (IsReceiving)
        {
            IsShort = false;
            CanPay = Parsed(Price) is not null && (_stash?.IsLoaded ?? false);
            Summary = !(_stash?.IsLoaded ?? false) ? L("No shared stash open.")
                : plan.ChangeWorth > 0
                    ? L($"Receiving {Hr(plan.ChangeWorth)} HR.") + (plan.Lost > 0 ? " " + L($"{Hr(plan.Lost)} HR too small to pay.") : "")
                : Parsed(Price) is not null ? L("Receiving nothing.")
                : "";
            return;
        }

        // Any offer, nothing included, in every play mode: a free grab is always allowed (Legit asked for one
        // Worldstone Shard at least).
        IsShort = plan.Short > 0;
        CanPay = !IsShort && Parsed(Price) is not null && (_stash?.IsLoaded ?? false);

        Summary = !(_stash?.IsLoaded ?? false) ? L("No shared stash open.")
            : plan.Short > 0 ? L($"{Hr(plan.Short)} HR short.")
            : plan.ChangeWorth > 0 ? L($"Paying {Hr(plan.PaidWorth)} HR, {Hr(plan.ChangeWorth)} HR back.")
            : price > 0 ? L($"Paying {Hr(plan.PaidWorth)} HR.")
            : Parsed(Price) is not null ? L("Paying nothing.")
            : "";
    }

    /// <summary>Picked by hand: each row is what the player set, and the offer is their sum.</summary>
    private void RecountPicked()
    {
        var paid = Rows.ToDictionary(row => row.Code, row => Math.Clamp(_held.GetValueOrDefault(row.Code), 0, row.Limit),
                                     StringComparer.OrdinalIgnoreCase);
        decimal worth = Rows.Sum(row => row.Worth * paid[row.Code]);

        _plan = new TradePayment.Plan(paid, new Dictionary<string, int>(), worth, 0, 0, 0);
        foreach (var row in Rows)
            row.Show(paid[row.Code], -paid[row.Code]);

        // The offer's box says what was picked. The field on purpose (MVVMTK0034): setting the
        // property would count again.
#pragma warning disable MVVMTK0034
        _price = Hr(worth);
#pragma warning restore MVVMTK0034
        OnPropertyChanged(nameof(Price));
        OnPropertyChanged(nameof(IsPriceInvalid));

        IsShort = false;
        CanPay = _stash?.IsLoaded ?? false;
        Summary = !(_stash?.IsLoaded ?? false) ? L("No shared stash open.")
            : worth > 0 ? L($"Paying {Hr(worth)} HR.")
            : L("Paying nothing.");
    }

    /// <summary>
    /// Pays: the item goes to the character and the currency moves in the materials tab - in
    /// memory, both files then waiting for Save like any other change.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPayNow))]
    private void Pay()
    {
        // Asked of the process at the click, not only of the last look.
        if (_sync?.RefusedWhileRunning(IsReceiving ? "sell" : "pay") == true)
            return;

        if (_plan is not null && _settle(_plan) is { } refused)
        {
            IsShort = true;
            Summary = refused;
        }
    }

    /// <summary>The panel's own close, for whichever page shows it to act on.</summary>
    public event Action? CloseRequested;

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();

    private static string Hr(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    internal static decimal? Parsed(string text) =>
        decimal.TryParse(text.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint,
                         CultureInfo.InvariantCulture, out decimal value)
            ? value
            : null;
}

/// <summary>
/// One currency in the payment: what it is worth, how many the tab holds, how many are paid - set
/// with - and + or typed - and what the tab's count moves by, less in red, back in green.
/// </summary>
public sealed partial class PaymentRow : ObservableObject
{
    private readonly PaymentViewModel _payment;
    private bool _showing;

    public PaymentRow(string code, decimal worth, int stock, int limit, PaymentViewModel payment)
    {
        _payment = payment;
        Code = code;
        Worth = worth;
        Stock = stock;
        Limit = limit;
        Tile = new ItemTileViewModel(ItemForge.Material(code, 1, ""), 0);
        Name = Runewords.RuneNumber(code) > 0 ? Runewords.RuneName(code) : ItemPresenter.BaseName(code);
    }

    public string Code { get; }

    public string Name { get; }

    public decimal Worth { get; }

    public string WorthText => $"{Worth.ToString("0.##", CultureInfo.InvariantCulture)} HR";

    /// <summary>How many the materials tab holds.</summary>
    public int Stock { get; }

    /// <summary>The line under the name: its worth and how many the tab holds - "0.01 HR · 16 in tab".</summary>
    public string Detail => L($"{WorthText} · {Stock} in tab");

    /// <summary>The most that can change hands: what the tab holds when paying, what it has room for when receiving.</summary>
    public int Limit { get; }

    public ItemTileViewModel Tile { get; }

    /// <summary>How many change hands - paid, or received in a sale - as shown and as typed.</summary>
    [ObservableProperty] private string _count = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NetText))]
    [NotifyPropertyChangedFor(nameof(IsTaken))]
    [NotifyPropertyChangedFor(nameof(IsGiven))]
    private int _net;

    public string NetText => Net > 0 ? $"+{Net}" : Net < 0 ? $"{Net}" : "";

    public bool IsTaken => Net < 0;

    public bool IsGiven => Net > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LessCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoreCommand))]
    private int _paid;

    private bool CanLess => Paid > 0;

    private bool CanMore => Paid < Limit;

    [RelayCommand(CanExecute = nameof(CanLess))]
    private void Less() => _payment.Hold(this, Paid - 1);

    [RelayCommand(CanExecute = nameof(CanMore))]
    private void More() => _payment.Hold(this, Paid + 1);

    /// <summary>Typed: held at the number, once it is one.</summary>
    partial void OnCountChanged(string value)
    {
        if (!_showing && int.TryParse(value.Trim(), out int count))
            _payment.Hold(this, count);
    }

    /// <summary>The count worked out - shown without being taken for one typed.</summary>
    internal void Show(int paid, int net)
    {
        _showing = true;
        Paid = paid;
        Count = paid.ToString(CultureInfo.InvariantCulture);
        Net = net;
        _showing = false;
    }
}
