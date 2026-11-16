namespace PD2SPCompanion.Core.Characters;

/// <summary>
/// Renaming a character.
/// <para>
/// <b>The name is in one place in the save</b> - the header's 16 bytes at 0x14 - and nowhere in
/// the files beside it (<c>.key</c>, <c>.ma0</c>-<c>.ma3</c>, <c>.map</c>), which carry it only in
/// their file names: measured on SpearZon. So a rename writes the save with the new
/// name under the new file name, moves every file named after the character, and takes the old
/// save away - its copy kept first, as every save is. The editor's backups of it move with it.
/// </para>
/// </summary>
public static class CharacterRename
{
    /// <summary>
    /// Why the game would not take this name, or null for one it would: 2 to 15 characters,
    /// letters, and at most one <c>-</c> or <c>_</c>, never first or last. The vanilla rule.
    /// </summary>
    public static string? Problem(string name)
    {
        if (name.Length is < 2 or > 15)
            return L("Require 2 to 15 characters.");

        if (name.Any(each => !IsLetter(each) && each is not ('-' or '_')))
            return L("Require letters only, with one - or _ at most.");

        if (name.Count(each => each is '-' or '_') > 1)
            return L("Require one - or _ at most.");

        if (!IsLetter(name[0]) || !IsLetter(name[^1]))
            return L("Require a letter first and last.");

        return null;
    }

    private static bool IsLetter(char each) => each is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

    /// <summary>
    /// What of <paramref name="typed"/> a name box takes, put beside <paramref name="kept"/> (the box's text less
    /// what the typing replaces): letters, and one <c>-</c> or <c>_</c> where the name has none - as the game's own
    /// box takes them, which ignores a digit, a space or a second dash as it is typed, so naming here is no looser
    /// than in game. The rest of the rule -
    /// its length, a letter first and last - is <see cref="Problem"/>'s, said when the name is used: a name being
    /// typed passes through both.
    /// </summary>
    public static string Typable(string kept, string typed)
    {
        bool joined = kept.Any(each => each is '-' or '_');
        var taken = new System.Text.StringBuilder(typed.Length);

        foreach (char each in typed)
        {
            if (IsLetter(each))
            {
                taken.Append(each);
            }
            else if (each is '-' or '_' && !joined)
            {
                taken.Append(each);
                joined = true;
            }
        }

        return taken.ToString();
    }

    /// <summary>
    /// Renames the character whose save this is, and returns the new save's path. Refuses - with
    /// the reason as the exception's message - on a bad name or one already taken, and puts
    /// everything back if a step fails part way.
    /// </summary>
    public static string Rename(string savePath, string newName)
    {
        newName = newName.Trim();
        if (Problem(newName) is { } problem)
            throw new InvalidOperationException(problem);

        string folder = Path.GetDirectoryName(savePath) ?? ".";
        string oldName = Path.GetFileNameWithoutExtension(savePath);
        if (newName == oldName)
            throw new InvalidOperationException(L("Same name."));

        // "spearzon" to "SpearZon": the same files to Windows, so moved by way of a stand-in.
        bool caseOnly = string.Equals(newName, oldName, StringComparison.OrdinalIgnoreCase);
        string newPath = Path.Combine(folder, newName + ".d2s");

        var companions = Directory.GetFiles(folder, oldName + ".*")
            .Where(file => string.Equals(Path.GetFileNameWithoutExtension(file), oldName, StringComparison.OrdinalIgnoreCase)
                           && !string.Equals(Path.GetExtension(file), ".d2s", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!caseOnly && (File.Exists(newPath)
                          || companions.Any(file => File.Exists(Path.Combine(folder, newName + Path.GetExtension(file))))))
            throw new InvalidOperationException(L($"{newName} already exists."));

        var (save, _) = SaveVerifier.Load(savePath);
        save.Name = newName;
        byte[] written = SaveWriter.Prepared(save);

        SaveWriter.Backup(savePath);

        var moved = new List<(string From, string To)>();
        string? standIn = null;
        bool wroteNew = false;

        try
        {
            if (caseOnly)
            {
                standIn = savePath + ".renaming";
                File.Move(savePath, standIn);
            }

            SaveWriter.WriteSafely(newPath, written);
            wroteNew = true;

            foreach (string file in companions)
            {
                string to = Path.Combine(folder, newName + Path.GetExtension(file));
                MoveExact(file, to);
                moved.Add((file, to));
            }

            if (standIn is not null)
                File.Delete(standIn);
            else
                File.Delete(savePath);
        }
        catch
        {
            foreach (var (from, to) in Enumerable.Reverse(moved))
                TryMove(to, from);

            if (wroteNew && !caseOnly)
                TryDelete(newPath);

            if (standIn is not null && File.Exists(standIn))
            {
                TryDelete(newPath);
                TryMove(standIn, savePath);
            }

            throw;
        }

        MoveBackups(savePath, newPath, oldName, newName);
        return newPath;
    }

    /// <summary>
    /// A copy of the character under <paramref name="newName"/>: its save with the new name in it, and every file beside it named after it. Its
    /// items keep their ids - they are copies, and the duplicate badge says so. Nothing of the
    /// original changes. Returns the copy's save path; refuses on a bad or taken name, and takes back
    /// whatever it had written if a step fails.
    /// </summary>
    public static string Clone(string savePath, string newName)
    {
        newName = newName.Trim();
        if (Problem(newName) is { } problem)
            throw new InvalidOperationException(problem);

        string folder = Path.GetDirectoryName(savePath) ?? ".";
        string oldName = Path.GetFileNameWithoutExtension(savePath);
        string newPath = Path.Combine(folder, newName + ".d2s");
        var companions = Companions(folder, oldName);

        if (File.Exists(newPath)
            || companions.Any(file => File.Exists(Path.Combine(folder, newName + Path.GetExtension(file)))))
            throw new InvalidOperationException(L($"{newName} already exists."));

        var (save, _) = SaveVerifier.Load(savePath);
        save.Name = newName;
        byte[] written = SaveWriter.Prepared(save);

        var made = new List<string>();
        try
        {
            SaveWriter.WriteSafely(newPath, written);
            made.Add(newPath);

            foreach (string file in companions)
            {
                string to = Path.Combine(folder, newName + Path.GetExtension(file));
                File.Copy(file, to);
                made.Add(to);
            }
        }
        catch
        {
            foreach (string file in made)
                TryDelete(file);
            throw;
        }

        return newPath;
    }

    /// <summary>
    /// Deletes the character, and nothing is kept: every file named after it, the save last so a
    /// failure part way leaves it listed, then its backup folder - the earlier copies too: no backup need stay, since the delete is
    /// explicit and already warned about.
    /// A later character given the name starts with no earlier copies.
    /// </summary>
    public static void Delete(string savePath)
    {
        string folder = Path.GetDirectoryName(savePath) ?? ".";
        string name = Path.GetFileNameWithoutExtension(savePath);

        foreach (string file in Companions(folder, name).Append(savePath))
            File.Delete(file);

        string backups = SaveWriter.BackupFolderFor(savePath);
        if (Directory.Exists(backups))
            Directory.Delete(backups, recursive: true);
    }

    /// <summary>
    /// A hardcore character's death taken back: the dead flag cleared and the save written, its
    /// copy kept first as every save is. Returns false for one that is not dead.
    /// </summary>
    public static bool Revive(string savePath)
    {
        var (save, _) = SaveVerifier.Load(savePath);
        if (!save.Status.IsDead)
            return false;

        save.Status.IsDead = false;
        byte[] written = SaveWriter.Prepared(save);
        SaveWriter.Backup(savePath);
        SaveWriter.WriteSafely(savePath, written);
        return true;
    }

    /// <summary>The files beside a save named after its character: <c>.key</c>, <c>.ma0</c>-<c>.ma3</c>, <c>.map</c>.</summary>
    private static List<string> Companions(string folder, string name) =>
        [.. Directory.GetFiles(folder, name + ".*")
            .Where(file => string.Equals(Path.GetFileNameWithoutExtension(file), name, StringComparison.OrdinalIgnoreCase)
                           && !string.Equals(Path.GetExtension(file), ".d2s", StringComparison.OrdinalIgnoreCase))];

    /// <summary>
    /// The character's backups move to the new name's folder and take the new name, so they are
    /// still listed as its own. Their contents keep the name they were written with - opening one
    /// puts the character's current name back in (<c>CharacterSession.LoadFrom</c>).
    /// </summary>
    private static void MoveBackups(string oldPath, string newPath, string oldName, string newName)
    {
        try
        {
            string from = SaveWriter.BackupFolderFor(oldPath);
            string to = SaveWriter.BackupFolderFor(newPath);
            if (!Directory.Exists(from))
                return;

            if (!string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(to))
                    return;
                Directory.Move(from, to);
            }
            else if (from != to)
            {
                // Only the case differs, which a plain move skips on Windows.
                string standIn = from + ".renaming";
                Directory.Move(from, standIn);
                Directory.Move(standIn, to);
            }

            // The rolling copy of the file as last written is the old name's: it goes, and the next save
            // of the renamed character writes its own.
            string stale = Path.Combine(to, Path.GetFileName(oldPath) + SaveWriter.AsWrittenSuffix);
            if (File.Exists(stale))
                File.Delete(stale);

            foreach (string file in Directory.GetFiles(to, oldName + ".d2s.*.bak"))
                MoveExact(file, Path.Combine(to, newName + Path.GetFileName(file)[oldName.Length..]));

            SaveWriter.RenameCaptions(to, oldName + ".d2s.", newName + ".d2s.");
        }
        catch (Exception)
        {
            // The backups stay where they were, under the old name - still there, only not
            // listed with the renamed character. Not a reason to undo the rename.
        }
    }

    /// <summary>A move that also changes only the case of a name, which a plain move skips on Windows.</summary>
    private static void MoveExact(string from, string to)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase) && from != to)
        {
            string standIn = from + ".renaming";
            File.Move(from, standIn);
            File.Move(standIn, to);
            return;
        }

        File.Move(from, to);
    }

    private static void TryMove(string from, string to)
    {
        try { File.Move(from, to); } catch (Exception) { }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception) { }
    }
}
