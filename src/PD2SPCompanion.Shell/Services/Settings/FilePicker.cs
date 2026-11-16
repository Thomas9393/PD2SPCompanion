using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace PD2SPCompanion.Shell.Services.Settings;

/// <summary>
/// The system's file pickers for vault files - a vault or a folder of one to import, or where
/// to export one - asked of the main window, as <see cref="FolderPicker"/> is.
/// </summary>
public static class FilePicker
{
    /// <summary>
    /// What a picker lists: vault files by every name one was written under - a vault, and what a folder
    /// written out, a collection and the file of records were called in older versions - and the
    /// <c>.pd2lib</c> a backup or an early export is. One kind: the import asks what to make of the file.
    /// </summary>
    private static FilePickerFileType Type => new(L("Vault"))
    {
        Patterns =
        [
            "*" + VaultLibrary.VaultExtension, "*" + VaultLibrary.FolderExtension, "*" + VaultLibrary.OldFolderExtension,
            "*" + VaultLibrary.OldVaultExtension, "*" + VaultLibrary.StoredExtension,
        ],
    };

    /// <summary>
    /// Asks for a vault file to read, starting in <paramref name="startIn"/> where one is given.
    /// Null where nothing was chosen or the picker could not open.
    /// </summary>
    public static async Task<string?> Open(string title, string? startIn = null)
    {
        // A test's answer, in place of the system's picker, which UI Automation cannot drive (tests/ui).
        if (Environment.GetEnvironmentVariable("PD2SPC_PICK_OPEN") is { Length: > 0 } given)
            return given;

        if (TopLevel.GetTopLevel(FolderPicker.Window()) is not { } top)
            return null;

        try
        {
            if (startIn is { Length: > 0 })
                System.IO.Directory.CreateDirectory(startIn);

            var start = startIn is { Length: > 0 }
                ? await top.StorageProvider.TryGetFolderFromPathAsync(startIn)
                : await top.StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Documents);

            var picked = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = [Type],
                SuggestedStartLocation = start,
            });

            return picked.FirstOrDefault()?.TryGetLocalPath();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Asks where to write a vault file, suggesting <paramref name="name"/>, starting in
    /// <paramref name="startIn"/> where one is given.
    /// </summary>
    public static async Task<string?> Save(string title, string name, string? startIn = null)
    {
        // A test's answer, as for Open: a folder, the suggested name written into it.
        if (Environment.GetEnvironmentVariable("PD2SPC_PICK_SAVE") is { Length: > 0 } folder)
            return System.IO.Path.Combine(folder, name);

        if (TopLevel.GetTopLevel(FolderPicker.Window()) is not { } top)
            return null;

        try
        {
            // Documents, unless told otherwise: with no start the system can fall back to the
            // drive's root, where a file cannot be written (found driving the dialog).
            if (startIn is { Length: > 0 })
                System.IO.Directory.CreateDirectory(startIn);

            var start = startIn is { Length: > 0 }
                ? await top.StorageProvider.TryGetFolderFromPathAsync(startIn)
                : await top.StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Documents);

            var picked = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedStartLocation = start,
                SuggestedFileName = name,
                DefaultExtension = VaultLibrary.VaultExtension.TrimStart('.'),
                FileTypeChoices = [new(L("Vault")) { Patterns = ["*" + VaultLibrary.VaultExtension] }],
                ShowOverwritePrompt = true,
            });

            return picked?.TryGetLocalPath();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
