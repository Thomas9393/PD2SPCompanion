using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace PD2SPCompanion.Shell.Services.Settings;

/// <summary>
/// The system's folder picker, asked of the main window.
/// <para>
/// Shared because two places ask for the same folder - the first run and the settings page
/// - and a picker opened against the wrong window is a picker that opens behind it.
/// </para>
/// </summary>
public static class FolderPicker
{
    /// <summary>
    /// Asks for a folder. Null where nothing was chosen, or where the picker could not be
    /// opened at all - which leaves whatever asked exactly as it was.
    /// </summary>
    public static async Task<string?> Pick(string title)
    {
        var top = TopLevel.GetTopLevel(Window());
        if (top is null)
            return null;

        try
        {
            var picked = await top.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { Title = title, AllowMultiple = false });

            return picked.Count > 0 && picked[0].TryGetLocalPath() is { Length: > 0 } path
                ? GameLocation.Tidy(path)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static Window? Window() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
}
