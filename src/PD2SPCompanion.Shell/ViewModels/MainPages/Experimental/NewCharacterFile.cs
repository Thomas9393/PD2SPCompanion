using PD2SPCompanion.Shell.Services.Character;


using System;
using System.IO;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// Writing a character the editor made - imported from the armory, or created - as a new file in
/// the save folder: its name by the rename rule, never over one that exists, built on a template
/// save that only lends what the new character has no say in, and never while the game runs.
/// </summary>
internal static class NewCharacterFile
{
    /// <summary>
    /// Where <paramref name="name"/> goes and what it is built on - the editor's own template
    /// (CharacterTemplate) - or null, and why said on the strip. <paramref name="act"/> names the
    /// act for the game's lock ("import", "create").
    /// </summary>
    public static (string Path, string Template, GameData Game)? Prepare(CharacterSession session, GameSync? sync, string name, string act)
    {
        if (sync?.RefusedWhileRunning(act) == true)
            return null;

        if (CharacterRename.Problem(name) is { } problem)
        {
            session.Messages.Post(problem);
            return null;
        }

        string folder = session.SaveFolder;
        string path = Path.Combine(folder, name + ".d2s");
        if (File.Exists(path))
        {
            session.Messages.Post(L($"{name} already exists."));
            return null;
        }

        // The editor's own template, never one of the player's characters: what Begin does not clear came with
        // one - its summons among PD2's sections (CharacterTemplate).
        if (CharacterTemplate.Fresh() is not { } template)
        {
            session.Messages.Post(L("No new-character template for this season's saves."));
            return null;
        }

        if (session.TryGame() is not { } game)
        {
            // A failed open has said why.
            if (!session.IsConfigured)
                session.Messages.Post(L("Require game folder."));
            return null;
        }

        return (path, template, game);
    }

    /// <summary>The new file, written the safe way - a new one: nothing is overwritten.</summary>
    public static void Write(string path, byte[] bytes) => SaveWriter.WriteSafely(path, bytes);
}
