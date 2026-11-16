using System.Collections.Generic;
using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using PD2SPCompanion.Core;
using PD2SPCompanion.Shell.Services.Generic;

namespace PD2SPCompanion.Shell.Services.Settings;

/// <summary>
/// "What's new": the release notes in a popup over the whole window, as the confirm question is.
/// It opens by itself once after an update - the editor remembers the version it last ran
/// (<see cref="EditorSettings.LastRunVersion"/>) and, started at a newer one, shows what changed since - and
/// from Settings' About card at any time. A first run, the same version, an older one and a dev rebuild
/// say nothing.
/// </summary>
public sealed partial class WhatsNewService : ObservableObject
{
    private readonly EditorSettings _settings;
    private readonly ConfirmService _confirm;

    public WhatsNewService(EditorSettings settings, ConfirmService confirm)
    {
        _settings = settings;
        _confirm = confirm;
    }

    [ObservableProperty] private bool _isOpen;

    /// <summary>
    /// In the window while it is open and for the time its fade out takes after (Presence.Keep), as every picker
    /// is: it faded in and was gone at once.
    /// </summary>
    [ObservableProperty] private bool _isPresent;

    partial void OnIsOpenChanged(bool value) =>
        PD2SPCompanion.Shell.ViewModels.Presence.Keep(value, () => IsOpen, on => IsPresent = on);

    /// <summary>"What's new", or "What's new in 0.2.0" after an update.</summary>
    [ObservableProperty] private string _title = L("What's new");

    /// <summary>The releases shown, newest first: those since the last run after an update, else all.</summary>
    [ObservableProperty] private IReadOnlyList<ChangelogRelease> _releases = [];

    /// <summary>Every release, as the About card's button asks.</summary>
    public void Show()
    {
        Title = L("What's new");
        Releases = Changelog.Bundled;
        IsOpen = true;
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    /// <summary>
    /// The check at start, once the window is up. It waits its turn behind a question already on screen
    /// (the new-season one), so the two never cover each other.
    /// </summary>
    public void CheckAtStart()
    {
        string last = _settings.LastRunVersion;
        string now = AppVersion.Number;

        if (last.Length == 0 || !AppVersion.IsNewerThan(last))
        {
            // First run, the same version, or one run before this: nothing to tell.
            _settings.LastRunVersion = now;
            return;
        }

        if (_confirm.IsAsking)
        {
            void Asked(object? sender, PropertyChangedEventArgs changed)
            {
                if (changed.PropertyName != nameof(ConfirmService.IsAsking) || _confirm.IsAsking)
                    return;

                _confirm.PropertyChanged -= Asked;
                Avalonia.Threading.Dispatcher.UIThread.Post(CheckAtStart, Avalonia.Threading.DispatcherPriority.Background);
            }

            _confirm.PropertyChanged += Asked;
            return;
        }

        // Shown once: the new number is kept as it opens, however it is closed.
        _settings.LastRunVersion = now;
        var since = Changelog.Since(Changelog.Bundled, last, now);
        Title = L($"What's new in {now}");
        Releases = since.Count > 0 ? since : Changelog.Bundled;
        IsOpen = true;
    }
}
