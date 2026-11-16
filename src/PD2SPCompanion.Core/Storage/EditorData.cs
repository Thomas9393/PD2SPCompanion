using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PD2SPCompanion.Core.Storage;

/// <summary>
/// Where the editor keeps what is its own: one <c>Data</c> folder beside the program - settings,
/// vaults, the trade cache, the logs. Nothing in AppData and nothing in the registry, so the
/// editor is a folder: copy it to move it, delete it to uninstall it, like those green versions.
/// <para>
/// What the editor writes into the game folder stays there by design: the save backups beside
/// the saves they copy, and the Experimental tables the game itself reads.
/// </para>
/// <para>
/// <c>PD2SPC_DATA</c> puts the folder elsewhere, in a development (Debug) build only. That build runs
/// from <c>bin</c>, which a clean build empties, so the developer's own data is kept outside it this
/// way. A release build ignores it and always uses the folder beside itself, as a player's copy
/// does - so testing a release on the developer's machine is testing what a player gets.
/// </para>
/// </summary>
public static class EditorData
{
    /// <summary>The folder beside the program, when nothing points elsewhere.</summary>
    public const string FolderName = "Data";

    /// <summary>The root: <c>Data</c> beside the program, or <c>PD2SPC_DATA</c> in a Debug build.</summary>
    public static string Root =>
#if DEBUG
        Environment.GetEnvironmentVariable("PD2SPC_DATA") is { Length: > 0 } elsewhere
            ? elsewhere
            :
#endif
            Path.Combine(AppContext.BaseDirectory, FolderName);

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static string Vaults => Path.Combine(Root, "Vaults");

    public static string TradeCache => Path.Combine(Root, "TradeCache");

    public static string Logs => Path.Combine(Root, "Logs");

    /// <summary>The bug reports made from Settings, a zip each (<see cref="Diagnostics.ProblemReport"/>).</summary>
    public static string Reports => Path.Combine(Root, "Reports");

    /// <summary>Each season's tables as it was played, kept before a patch replaces them (<see cref="SeasonSnapshot"/>).</summary>
    public static string Seasons => Path.Combine(Root, "Seasons");

    /// <summary>
    /// Makes sure the root exists and can be written, or says why not. A green program lives
    /// wherever it was unpacked; under Program Files it may not write beside itself, and it
    /// refuses to start there rather than spill into AppData.
    /// </summary>
    public static string? Prepare()
    {
        string root = Root;
        try
        {
            Directory.CreateDirectory(root);
            string probe = Path.Combine(root, $".write-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return null;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            return $"Project Diablo II Single Player Companion cannot write its data folder:\n{root}\n\n"
                   + "Require a writable folder for the editor (not Program Files).";
        }
    }
}
