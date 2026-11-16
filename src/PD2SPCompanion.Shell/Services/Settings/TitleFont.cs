using System;
using System.IO;
using System.Linq;

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace PD2SPCompanion.Shell.Services.Settings;

/// <summary>
/// The face the titles and headings are set in (the <c>DiabloFont</c> resource): Cinzel, shipped with the editor,
/// or a font the player has put in the <c>Fonts</c> folder beside it.
/// <para>
/// The editor ships only what it may: Cinzel is under the SIL Open Font License. The game's own lettering is
/// Exocet, a commercial face, and its free lookalike AvQest asks to be passed on only as its untouched zip - so
/// neither is shipped, and a player who has one puts the file in the folder. Read once, as the editor starts: a font put there shows on the next start.
/// </para>
/// </summary>
public static class TitleFont
{
    /// <summary>The resource every title's FontFamily names (App.axaml sets it to Cinzel).</summary>
    public const string ResourceKey = "DiabloFont";

    /// <summary>The family the editor ships.</summary>
    public const string Shipped = "Cinzel";

    /// <summary>
    /// The folder a player's own font goes in: <c>Fonts</c>, beside the editor. A Debug build takes PD2SPC_FONTS
    /// instead when it is set, as the language packs' folder does: the UI tests name a folder of the pad, so
    /// they run in Cinzel whatever the developer keeps in the build's own.
    /// </summary>
    public static string Folder
    {
        get
        {
#if DEBUG
            if (Environment.GetEnvironmentVariable("PD2SPC_FONTS") is { Length: > 0 } chosen)
                return chosen;
#endif
            return Path.Combine(AppContext.BaseDirectory, "Fonts");
        }
    }

    /// <summary>The family in use: <see cref="Shipped"/>, or the folder's.</summary>
    public static string Family { get; private set; } = Shipped;

    /// <summary>True where the family came from the folder.</summary>
    public static bool FromFolder { get; private set; }

    /// <summary>
    /// Takes the folder's font where there is one: every font file in it is registered, and the family of the
    /// first, by name, becomes the titles' - so a family's weights (Regular, Bold) go in together. A file that
    /// is not a font is passed over and logged; with none that reads, the titles stay in Cinzel. Called before
    /// the first window is built, so every title is laid out in it.
    /// </summary>
    public static void Use(Application app)
    {
        try
        {
            if (!Directory.Exists(Folder))
                return;

            var files = Directory.EnumerateFiles(Folder)
                .Where(file => Path.GetExtension(file).ToLowerInvariant() is ".ttf" or ".otf")
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (files.Count == 0)
                return;

            var fonts = new FolderFonts();
            string? family = null;
            foreach (string file in files)
            {
                // Kept in memory for as long as the editor runs: the typeface reads its tables from the stream.
                var stream = new MemoryStream(File.ReadAllBytes(file));
                if (fonts.TryAddGlyphTypeface(stream, out var typeface))
                    family ??= typeface.FamilyName;
                else
                    PD2SPCompanion.Core.Diagnostics.Log.Warning("Fonts", $"Not a font the editor can read: {Path.GetFileName(file)}");
            }
            if (family is null)
                return;

            FontManager.Current.AddFontCollection(fonts);
            app.Resources[ResourceKey] = new FontFamily($"{FolderFonts.Scheme}#{family}");
            Family = family;
            FromFolder = true;
            PD2SPCompanion.Core.Diagnostics.Log.Info("Fonts", $"Titles in {family}, from the Fonts folder.");
        }
        catch (Exception problem)
        {
            // The titles stay in the shipped face.
            PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
        }
    }

    /// <summary>
    /// The face a bold title is drawn in, said in the editor's log: its weight, and whether it is the family's own
    /// bold cut or its regular one thickened by the renderer (a simulation) - which a family put in the folder as
    /// one file is. Cinzel ships both cuts (Assets/Fonts).
    /// </summary>
    public static void LogBold(Application app)
    {
        try
        {
            if (app.Resources[ResourceKey] is FontFamily family
                && FontManager.Current.TryGetGlyphTypeface(new Typeface(family, FontStyle.Normal, FontWeight.Bold), out var bold))
                PD2SPCompanion.Core.Diagnostics.Log.Info("Fonts", $"Bold titles: {bold.FamilyName}, weight {(int)bold.Weight}, simulated {bold.FontSimulations}");
        }
        catch (Exception problem)
        {
            PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
        }
    }

    /// <summary>The fonts read from the folder, as a collection the font manager can be asked for by name.</summary>
    private sealed class FolderFonts : FontCollectionBase
    {
        public const string Scheme = "fonts:PD2SPCompanionFolder";

        public override Uri Key { get; } = new(Scheme, UriKind.Absolute);
    }
}
