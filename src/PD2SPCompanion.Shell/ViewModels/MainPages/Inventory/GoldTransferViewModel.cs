using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PD2SPCompanion.Shell.Services.Character;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// Gold moved between two purses side by side in the gold panel - the character's carried gold and the shared stash's
/// (carried, shared and the vault in one panel, each next to the other). Season 14 keeps no
/// gold in a character's own stash, and the game moves carried gold to the shared stash by its button; this is that
/// button, both ways. Each purse writes its own file as it changes, and both are saved together.
/// </summary>
public sealed partial class GoldTransferViewModel : ObservableObject
{
    private readonly GoldViewModel _upper;
    private readonly GoldViewModel _lower;
    private readonly GameSync? _sync;
    private readonly Action<string>? _say;

    /// <param name="upper">The purse above the arrows - the carried gold.</param>
    /// <param name="lower">The purse below - the shared stash's.</param>
    public GoldTransferViewModel(GoldViewModel upper, GoldViewModel lower, GameSync? sync, Action<string>? say)
    {
        _upper = upper;
        _lower = lower;
        _sync = sync;
        _say = say;

        upper.PropertyChanged += Followed;
        lower.PropertyChanged += Followed;
    }

    /// <summary>How much the arrows move: the panel's one amount, which the shared purse keeps for its vault arrows too.</summary>
    private int Typed => _lower.Moved;

    /// <summary>The game is running: both files are written at the next save, which the game would write over.</summary>
    private bool GameBlocked => _sync?.IsWriteBlocked ?? false;

    public bool CanMoveDown => _upper.Held > 0 && _lower.Held < _lower.Maximum && !GameBlocked;

    public bool CanMoveUp => _lower.Held > 0 && _upper.Held < _upper.Maximum && !GameBlocked;

    public string DownHint => GameBlocked
        ? (_sync?.BlockedFor(L("move gold")) ?? GameSync.Blocked(L("move gold")))
        : L($"To shared stash{Environment.NewLine}") + L($"LMB: the amount{Environment.NewLine}") + L("Shift + LMB: all of it");

    public string UpHint => GameBlocked
        ? (_sync?.BlockedFor(L("move gold")) ?? GameSync.Blocked(L("move gold")))
        : L($"From shared stash{Environment.NewLine}") + L($"LMB: the amount{Environment.NewLine}") + L($"Shift + LMB: fill to {_upper.Maximum:N0}");

    [RelayCommand]
    private void MoveDown() => Move(_upper, _lower, Typed, all: false);

    [RelayCommand]
    private void MoveUp() => Move(_lower, _upper, Typed, all: false);

    /// <summary>Shift on the downward arrow: everything the upper purse holds, as far as the lower has room.</summary>
    public void MoveAllDown() => Move(_upper, _lower, _upper.Held, all: true);

    /// <summary>Shift on the upward arrow: the upper purse filled, as far as the lower holds.</summary>
    public void MoveAllUp() => Move(_lower, _upper, _upper.Maximum - _upper.Held, all: true);

    /// <summary>
    /// Up to <paramref name="want"/> from one purse to the other - no more than the one holds and the other has room
    /// for, said where it was less. Allowed in every play mode: moving gold makes none.
    /// </summary>
    private void Move(GoldViewModel from, GoldViewModel to, long want, bool all)
    {
        if (_sync?.RefusedWhileRunning(L("move gold")) == true)
            return;
        if (!all && want <= 0)
        {
            _say?.Invoke(L("Require amount."));
            return;
        }
        if (from.Held <= 0)
        {
            _say?.Invoke(L($"{from.Label}: empty."));
            return;
        }

        int amount = (int)Math.Min(want, Math.Min(from.Held, to.Maximum - to.Held));
        if (amount <= 0)
        {
            _say?.Invoke(L($"{to.Label}: full at {to.Maximum:N0}."));
            return;
        }

        from.Value = (from.Held - amount).ToString();
        to.Value = (to.Held + amount).ToString();
        if (!all && want > amount)
            _say?.Invoke(L($"Moved {amount:N0}."));
    }

    private void Followed(object? sender, PropertyChangedEventArgs changed)
    {
        // A purse changed, or a game started or closed (SayVault tells the purse's arrows).
        if (changed.PropertyName is not (nameof(GoldViewModel.Value) or nameof(GoldViewModel.CanDeposit)))
            return;
        OnPropertyChanged(nameof(CanMoveDown));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(DownHint));
        OnPropertyChanged(nameof(UpHint));
    }
}
