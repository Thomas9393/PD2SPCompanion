using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Vault;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// One purse of gold - what the character carries, what is in its own stash, or the shared
/// stash's - and the vault's gold beside it.
/// <para>
/// Unlike an item's properties, the purse is written as it is edited rather than staged. Gold is
/// a single number with nothing to be half-finished about, and the attributes page treats its own
/// numbers the same way. Gold moved to or from the vault changes the purse the same way; the
/// vault's side waits for the save (<see cref="VaultMoves.StageGold"/>).
/// </para>
/// </summary>
public sealed partial class GoldViewModel : ObservableObject
{
    /// <summary>
    /// What a press of the steppers moves, plain and with control held. Gold runs to seven
    /// figures, so a step of one would be a control nobody could use; the round figures are
    /// the point of the button, and shift covers the two ends.
    /// </summary>
    public const int Step = 10_000;

    public const int Leap = 100_000;

    /// <summary>What the vault's amount box starts at.</summary>
    public const int VaultStep = 100_000;

    /// <summary>The most the amount box takes: a shared purse's worth, the largest there is.</summary>
    public const int MostMoved = SharedStash.GoldCap;

    private readonly Func<int> _read;
    private readonly Action<int> _write;
    private readonly VaultMoves? _moves;
    private readonly GameSync? _sync;

    /// <summary>Where a move's outcome is said: the message strip.</summary>
    private readonly Action<string>? _say;

    /// <summary>
    /// A character's own purse, through its attributes: the carried gold. Season 14 keeps no gold in a character's
    /// stash, and the carried gold goes to the shared stash
    /// beside it in the gold panel (<see cref="GoldTransferViewModel"/>).
    /// </summary>
    /// <remarks>Read through the session each time: a section kept over a reload keeps its purses.</remarks>
    public GoldViewModel(CharacterSession session, AttributeEditor attributes, string stat, string label,
                         VaultMoves? moves = null, GameSync? sync = null)
        : this(label, stat == AttributeEditor.Carried ? "inventory" : "stash", () => session.Attributes?.Gold(stat) ?? 0,
               value =>
               {
                   if (session.Attributes is not { } now)
                       return;
                   now.SetGold(stat, value);
                   session.MarkEdited();
                   session.RaiseAttributesChanged();
               },
               attributes.GoldCeiling(stat), moves, sync, message => session.Status = message) { }

    /// <summary>A purse, read and written by the caller: the shared stash's.</summary>
    public GoldViewModel(string label, string purse, Func<int> read, Action<int> write, int maximum,
                         VaultMoves? moves = null, GameSync? sync = null, Action<string>? say = null)
    {
        _say = say;
        Label = label;
        Purse = purse;
        Maximum = maximum;
        _read = read;
        _write = write;
        _moves = moves;
        _sync = sync;
        _value = read().ToString();
    }

    public string Label { get; }

    /// <summary>Which purse, as the vault's gold log names it: inventory, stash or shared.</summary>
    public string Purse { get; }

    public int Minimum => 0;

    public int Maximum { get; }

    /// <summary>What the purse holds at most, said the way the game says amounts.</summary>
    public string Limits => L($"max {Maximum:N0}");

    /// <summary>
    /// The house shape for a button that reads modifier keys: what it does, then a line a
    /// modifier. The attributes and waypoint pages say it this way too.
    /// </summary>
    public string RaiseHint =>
        L($"Add gold{Environment.NewLine}")
        + L($"LMB: {Step:N0}{Environment.NewLine}")
        + L($"Ctrl + LMB: {Leap:N0}{Environment.NewLine}")
        + L($"Shift + LMB: fill to {Maximum:N0}");

    public string LowerHint =>
        L($"Take gold{Environment.NewLine}")
        + L($"LMB: {Step:N0}{Environment.NewLine}")
        + L($"Ctrl + LMB: {Leap:N0}{Environment.NewLine}")
        + L("Shift + LMB: empty the purse");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Words))]
    private string _value;

    /// <summary>What the purse holds, as a header says it: "1,457,350 gold".</summary>
    public string Words => L($"{Current:N0} gold");

    partial void OnValueChanged(string value)
    {
        // An empty box on the way to a number is not yet a number.
        if (!int.TryParse(value, out int parsed))
            return;

        int held = Math.Clamp(parsed, Minimum, Maximum);
        if (held != parsed)
        {
            Value = held.ToString();
            return;
        }

        if (_read() == held)
            return;

        _write(held);

        OnPropertyChanged(nameof(CanRaise));
        OnPropertyChanged(nameof(CanLower));
        SayVault();
    }

    private int Current => int.TryParse(Value, out int parsed) ? parsed : _read();

    /// <summary>What the purse holds now - for a move between two purses.</summary>
    public int Held => Current;

    /// <summary>The file's gold again, where it may have changed under the box: a reload, an undo.</summary>
    public void Reread() => Value = _read().ToString();

    public bool CanRaise => Current < Maximum;
    public bool CanLower => Current > Minimum;

    /// <summary>A plain click, which is what the buttons themselves are bound to.</summary>
    [RelayCommand]
    private void Raise() => Move(+Step);

    [RelayCommand]
    private void Lower() => Move(-Step);

    /// <summary>Control held: the bigger step.</summary>
    public void LeapBy(bool up) => Move(up ? +Leap : -Leap);

    /// <summary>Shift-click on the plus: as much as the game will hold.</summary>
    public void Fill() => Value = Maximum.ToString();

    /// <summary>Shift-click on the minus: all of it gone.</summary>
    public void Empty() => Value = Minimum.ToString();

    private void Move(int by) => Value = Math.Clamp(Current + by, Minimum, Maximum).ToString();

    // ---- the vault's gold --------------------------------------------------------------------

    /// <summary>Whether this purse can trade with the vault at all: it has one to trade with.</summary>
    public bool HasVault => _moves is not null;

    /// <summary>The vault's gold as it will be once saved - what a move can draw on.</summary>
    public long VaultBalance => _moves?.GoldBalance ?? 0;

    /// <summary>The vault's balance alone, under "Vault gold" in its row.</summary>
    public string VaultAmount => $"{VaultBalance:N0}";

    /// <summary>
    /// The game is running: the purse is written with its file at the next save, which the game
    /// would write over, so moves wait - as Withdraw does (see GameSync).
    /// </summary>
    private bool GameBlocked => _sync?.IsWriteBlocked ?? false;

    public bool CanDeposit => HasVault && Current > 0 && !GameBlocked;

    public bool CanWithdrawGold => HasVault && VaultBalance > 0 && Current < Maximum && !GameBlocked;

    /// <summary>
    /// How much the arrows move, typed as the purse's amount is typed in its own. The
    /// shared purse's is the gold panel's one amount, for every arrow in it. The vault's balance
    /// itself is never typed: it is the sum of its moves.
    /// </summary>
    [ObservableProperty] private string _moveAmount = VaultStep.ToString();

    public int Moved => int.TryParse(MoveAmount, out int typed) ? Math.Clamp(typed, 0, MostMoved) : 0;

    public string DepositHint => GameBlocked
        ? (_sync?.BlockedFor(L("move gold")) ?? GameSync.Blocked(L("move gold")))
        : L($"To vault{Environment.NewLine}")
          + L($"LMB: the amount{Environment.NewLine}")
          + L("Shift + LMB: all of it");

    public string WithdrawHint => GameBlocked
        ? (_sync?.BlockedFor(L("move gold")) ?? GameSync.Blocked(L("move gold")))
        : L($"From vault{Environment.NewLine}")
          + L($"LMB: the amount{Environment.NewLine}")
          + L($"Shift + LMB: fill to {Maximum:N0}");

    [RelayCommand]
    private void Deposit() => DepositBy(Moved);

    [RelayCommand]
    private void WithdrawGold() => WithdrawBy(Moved);

    /// <summary>
    /// Moves up to <paramref name="want"/> from the purse into the vault - no more than the purse
    /// holds. Allowed in every play mode: moving gold makes none.
    /// </summary>
    public void DepositBy(long want, bool all = false)
    {
        if (_moves is null || _sync?.RefusedWhileRunning(L("move gold")) == true)
            return;

        // Too low: nothing typed, or nothing to give.
        if (!all && want <= 0)
        {
            _say?.Invoke(L("Require amount."));
            return;
        }

        if (Current <= 0)
        {
            _say?.Invoke(L("Purse is empty."));
            return;
        }

        // Too high: no more than the purse holds - moved all the same, and said.
        int amount = (int)Math.Min(want, Current);
        Value = (Current - amount).ToString();
        _moves.StageGold(Purse, +amount);

        if (!all && want > amount)
            _say?.Invoke(L($"Purse held {amount:N0}. Moved that."));
    }

    /// <summary>
    /// Moves up to <paramref name="want"/> from the vault into the purse - no more than the vault
    /// holds, nor than the purse can take.
    /// </summary>
    public void WithdrawBy(long want, bool all = false)
    {
        if (_moves is null || _sync?.RefusedWhileRunning(L("move gold")) == true)
            return;

        // Too low: nothing typed, nothing in the vault, or no room in the purse.
        if (!all && want <= 0)
        {
            _say?.Invoke(L("Require amount."));
            return;
        }

        long held = VaultBalance;
        if (held <= 0)
        {
            _say?.Invoke(L("Vault is empty."));
            return;
        }

        int room = Maximum - Current;
        if (room <= 0)
        {
            _say?.Invoke(L("Purse is full."));
            return;
        }

        // Too high: no more than the vault holds, nor than the purse can take - moved all the
        // same, and said which stopped it.
        int amount = (int)Math.Min(Math.Min(want, held), room);
        Value = (Current + amount).ToString();
        _moves.StageGold(Purse, -amount);

        if (!all && want > amount)
            _say?.Invoke(held <= room ? L($"Vault held {amount:N0}. Moved that.") : L($"Purse took {amount:N0}, now full."));
    }

    /// <summary>Shift on the arrows: all of the purse in, or the purse filled from the vault.</summary>
    public void DepositAll() => DepositBy(Current, all: true);

    public void WithdrawFill() => WithdrawBy(long.MaxValue, all: true);

    /// <summary>The vault's side has changed - a move staged or saved, a game started or closed.</summary>
    public void SayVault()
    {
        OnPropertyChanged(nameof(VaultBalance));
        OnPropertyChanged(nameof(VaultAmount));
        OnPropertyChanged(nameof(CanDeposit));
        OnPropertyChanged(nameof(CanWithdrawGold));
        OnPropertyChanged(nameof(DepositHint));
        OnPropertyChanged(nameof(WithdrawHint));
    }
}
