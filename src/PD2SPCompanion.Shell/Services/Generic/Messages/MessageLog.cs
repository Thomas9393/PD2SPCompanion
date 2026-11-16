using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using System;
using System.Collections.Generic;
using System.Linq;

namespace PD2SPCompanion.Shell.Services.Generic;

/// <summary>
/// What just happened, for a strip in the silver bar under a page.
/// <para>
/// Two, owned by the character session, which every page already holds: <c>Session.Messages</c>
/// under the left page, for what reaches another page, and <c>Session.RightMessages</c> under the
/// right, for an action that stays there. Outcomes only: a state - unsaved edits, a pending mark,
/// a limit - is said beside what it describes.
/// </para>
/// </summary>
public sealed partial class MessageLog(string area) : ObservableObject
{
    /// <summary>One message, when it was said, and whether it is a refusal.</summary>
    public sealed record Entry(string Text, DateTime At, bool IsCaution);

    /// <summary>How many the history keeps - what hovering the strip shows.</summary>
    public const int Kept = 10;

    /// <summary>How long a message stays bright before it dims.</summary>
    private static readonly TimeSpan Bright = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long after the last message the strip is put away: hidden when idle for 15 seconds,
    /// and back when the cursor moves onto it, to show its history.
    /// PD2SPC_STRIP_IDLE, in milliseconds, for a test that cannot wait that long.
    /// </summary>
    private static readonly TimeSpan Idle =
        int.TryParse(Environment.GetEnvironmentVariable("PD2SPC_STRIP_IDLE"), out int asked) && asked > 0
            ? TimeSpan.FromMilliseconds(asked)
            : TimeSpan.FromSeconds(15);

    private readonly List<Entry> _entries = [];
    private DispatcherTimer? _dimmer, _hider;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    [NotifyPropertyChangedFor(nameof(Text))]
    [NotifyPropertyChangedFor(nameof(IsCaution))]
    [NotifyPropertyChangedFor(nameof(History))]
    private Entry? _latest;

    /// <summary>Just said: shown bright, and dimmed once <see cref="Bright"/> has passed.</summary>
    [ObservableProperty] private bool _isFresh;

    /// <summary>
    /// Nothing said for <see cref="Idle"/>: the strip is out of sight until the next message - or the pointer
    /// over where it lies, which brings it back with its history.
    /// </summary>
    [ObservableProperty] private bool _isIdle;

    public bool HasMessage => Latest is not null;

    public string Text => Latest?.Text ?? "";

    public bool IsCaution => Latest?.IsCaution == true;

    /// <summary>The last <see cref="Kept"/>, newest first, each with its time.</summary>
    public string History => string.Join(Environment.NewLine,
        Enumerable.Reverse(_entries).Select(entry => $"{entry.At:HH:mm:ss}  {entry.Text}"));

    /// <summary>
    /// Says something. Every call is a message, the same text included - a second "Saved" is a
    /// second save. Empty text says nothing: the owners clear their lines that way.
    /// </summary>
    public void Post(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Post(text));
            return;
        }

        var entry = new Entry(text.Trim(), DateTime.Now, IsRefusal(text.Trim()));
        _entries.Add(entry);
        if (_entries.Count > Kept)
            _entries.RemoveAt(0);

        Latest = entry;
        IsFresh = true;
        IsIdle = false;

        // What the user was told, in the log too: the trail a report is read against.
        if (entry.IsCaution)
            Log.Warning(area, entry.Text);
        else
            Log.Info(area, entry.Text);

        _dimmer ??= new DispatcherTimer { Interval = Bright };
        _dimmer.Stop();
        _dimmer.Tick -= Dim;
        _dimmer.Tick += Dim;
        _dimmer.Start();

        _hider ??= new DispatcherTimer { Interval = Idle };
        _hider.Stop();
        _hider.Tick -= Hide;
        _hider.Tick += Hide;
        _hider.Start();
    }

    private void Hide(object? sender, EventArgs e)
    {
        _hider?.Stop();
        IsIdle = true;
    }

    private void Dim(object? sender, EventArgs e)
    {
        _dimmer?.Stop();
        IsFresh = false;
    }

    /// <summary>
    /// A refusal, told by the house wording (memory <c>pd2spc-ui-wording</c>): a prerequisite
    /// reads "Require X", a failure is a bare condition - "Folder not found.", "No free space ...".
    /// Told from the English, whatever language is shown (Language.EnglishOf).
    /// </summary>
    private static bool IsRefusal(string shown) => IsRefusalWording(Language.EnglishOf(shown));

    private static bool IsRefusalWording(string text) =>
        text.StartsWith("Require", StringComparison.Ordinal)
        || text.StartsWith("Refused", StringComparison.Ordinal)
        || text.StartsWith("No free space", StringComparison.Ordinal)
        || text.StartsWith("Cannot", StringComparison.Ordinal)
        || text.StartsWith("Not ", StringComparison.Ordinal)
        || text.EndsWith("not found.", StringComparison.Ordinal);
}
