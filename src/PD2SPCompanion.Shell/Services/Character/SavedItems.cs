using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using D2SLib.Model.Save;

using PD2SPCompanion.Core.Saves;

namespace PD2SPCompanion.Shell.Services.Character;

/// <summary>
/// What the characters on disk hold, read without opening them: for the home page's find across characters and
/// the grail's count of copies on the other characters. <b>Read only</b>: knowing where a thing is, never acting on
/// it - to act, the character is opened.
/// <para>
/// A save is read once and kept until its file changes. Read on the UI thread, where the parser's one layout
/// lives: the caller hands in what puts the layout back, as the cards' figures do (FigureBitmaps.OfFileAsync).
/// </para>
/// </summary>
public static class SavedItems
{
    /// <summary>
    /// One character's items: its own and its mercenary's, not what sits in their sockets - and what it wears as
    /// its card's figure is drawn from (<paramref name="Look"/>; null where that could not be worked out). The
    /// figure and the search each read every save after start-up, on the UI thread, some 20 ms a file: one read
    /// serves both.
    /// </summary>
    public sealed record Held(SaveFormat Format, bool IsHardcore, IReadOnlyList<Item> Own, IReadOnlyList<Item> Mercenary,
                              PD2SPCompanion.Core.Graphics.FigureLook? Look = null)
    {
        public IEnumerable<Item> All => Own.Concat(Mercenary);
    }

    private static readonly Dictionary<string, (DateTime Written, long Length, Held? Held)> Kept =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The items of the save at <paramref name="path"/>, or null where it cannot be read - a stub never played,
    /// another season's file, one being written. <paramref name="restoreLayout"/> puts the parser back for
    /// whoever it belonged to.
    /// </summary>
    public static Held? Of(string path, Action? restoreLayout)
    {
        DateTime written;
        long length;
        try
        {
            var file = new FileInfo(path);
            written = file.LastWriteTimeUtc;
            length = file.Length;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (Kept.TryGetValue(path, out var kept) && kept.Written == written && kept.Length == length)
            return kept.Held;

        Held? held = null;
        try
        {
            var (save, result) = SaveVerifier.Load(path);
            held = new Held(
                result.Format, save.Status.IsHardcore,
                [.. save.PlayerItemList?.Items ?? []],
                [.. save.MercenaryItemList?.ItemList?.Items ?? []],
                LookOf(save));
        }
        catch (Exception problem) when (problem is InvalidDataException or IOException or NotSupportedException
                                            or UnauthorizedAccessException)
        {
            // Not a character that can be read: it holds nothing that can be found.
        }
        finally
        {
            restoreLayout?.Invoke();
        }

        Kept[path] = (written, length, held);
        return held;
    }

    /// <summary>What the character wears, to draw it by; null where it cannot be said - its card keeps the class picture.</summary>
    private static PD2SPCompanion.Core.Graphics.FigureLook? LookOf(D2S save)
    {
        try
        {
            return PD2SPCompanion.Core.Graphics.CharacterFigure.LookOf(save);
        }
        catch (Exception problem)
        {
            PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
            return null;
        }
    }

    /// <summary>
    /// Where on its character an item lies, in a word: equipped (the pages' own word, the one word for it
    /// everywhere), on the mercenary, in the belt, the inventory, the cube
    /// or the stash.
    /// </summary>
    public static string PlaceOf(Held held, Item item) =>
        held.Mercenary.Contains(item, ReferenceEqualityComparer.Instance) ? L("mercenary")
        : item.Mode switch
        {
            ItemMode.Equipped => L("equipped"),
            ItemMode.Belt => L("belt"),
            _ => item.Page switch
            {
                InventoryLayout.InventoryPage => L("inventory"),
                InventoryLayout.CubePage => L("cube"),
                _ => L("stash"),
            },
        };
}
