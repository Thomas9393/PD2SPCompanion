using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace PD2SPCompanion.Shell.Services.Generic;

/// <summary>
/// The one question the app stops to ask, shown as an overlay over the whole window.
/// <para>
/// It is a service rather than state on a page because more than one place needs it and
/// they are not related: the landing page's close button, and the window itself on the way
/// out. Both hand over what to do if the answer is yes, and nothing happens otherwise.
/// </para>
/// <para>
/// There is no dialog window behind this. A second window would need the same custom frame
/// as the main one, and an overlay blocks the window just as well.
/// </para>
/// </summary>
public sealed partial class ConfirmService : ObservableObject
{
    [ObservableProperty] private bool _isAsking;

    /// <summary>
    /// In the window while it is asking and for the time its fade out takes after (Presence.Keep), as every picker
    /// is: it faded in and was gone at once.
    /// </summary>
    [ObservableProperty] private bool _isPresent;

    partial void OnIsAskingChanged(bool value) =>
        PD2SPCompanion.Shell.ViewModels.Presence.Keep(value, () => IsAsking, on => IsPresent = on);
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _confirmLabel = L("CONTINUE");

    /// <summary>
    /// A delete being confirmed - DELETE, DELETE ALL, DELETE FOLDER: its button wears the bin, the
    /// others a tick. Said by the asker (<c>deletes</c>), not read off the label's
    /// English word, so a language pack can word these labels.
    /// </summary>
    [ObservableProperty] private bool _isDelete;
    [ObservableProperty] private string _cancelLabel = L("CANCEL");

    /// <summary>
    /// A second yes, for a question where two different things could reasonably be done.
    /// Empty when there is only one, and the button is not shown.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAlternative))]
    private string _alternativeLabel = "";

    public bool HasAlternative => AlternativeLabel.Length > 0;

    /// <summary>
    /// What a button does, as its tooltip, where the question alone does not say it. Null for
    /// none - a button whose label is the whole answer needs nothing more.
    /// </summary>
    [ObservableProperty] private string? _confirmHint;

    [ObservableProperty] private string? _alternativeHint;

    /// <summary>
    /// A question with two answers and no "no": neither Enter nor Escape answers it - a key pressed for
    /// something else, as it came up, must not pick one (the vault's play mode).
    /// </summary>
    [ObservableProperty] private bool _needsClick;

    private Action? _onConfirm;
    private Action? _onAlternative;
    private Action? _onCancel;

    /// <summary>
    /// Asks, and runs <paramref name="onConfirm"/> only if the answer is yes. A question
    /// already on screen is left alone rather than replaced. <paramref name="onCancel"/> runs on
    /// the no, for a question whose no is an answer worth keeping too - LATER on the new-season
    /// notice - and <paramref name="cancelLabel"/> names it.
    /// </summary>
    public void Ask(string title, string message, string confirmLabel, Action onConfirm,
                    string? alternativeLabel = null, Action? onAlternative = null,
                    string? confirmHint = null, string? alternativeHint = null,
                    string? cancelLabel = null, Action? onCancel = null, bool needsClick = false, bool deletes = false)
    {
        if (IsAsking)
            return;

        NeedsClick = needsClick;
        IsDelete = deletes;

        Title = title;
        Message = message;
        ConfirmLabel = confirmLabel;
        AlternativeLabel = alternativeLabel ?? "";
        ConfirmHint = confirmHint;
        AlternativeHint = alternativeHint;
        _onConfirm = onConfirm;
        _onAlternative = onAlternative;
        CancelLabel = cancelLabel ?? L("CANCEL");
        _onCancel = onCancel;
        IsAsking = true;
    }

    [RelayCommand]
    private void Accept()
    {
        var act = _onConfirm;

        // Cleared before it runs, so whatever happens next can ask something of its own.
        Dismiss();
        act?.Invoke();
    }

    [RelayCommand]
    private void Alternative()
    {
        var act = _onAlternative;

        Dismiss();
        act?.Invoke();
    }

    [RelayCommand]
    private void Reject()
    {
        var act = _onCancel;

        Dismiss();
        act?.Invoke();
    }

    private void Dismiss()
    {
        IsAsking = false;
        NeedsClick = false;
        AlternativeLabel = "";
        _onConfirm = null;
        _onAlternative = null;
        _onCancel = null;
    }
}
