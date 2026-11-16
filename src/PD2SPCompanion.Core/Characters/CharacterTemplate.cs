namespace PD2SPCompanion.Core.Characters;

/// <summary>
/// The save a new character is built on - by character creation and by the armory import (see
/// <see cref="NewCharacter.Begin"/>): a character made in game and taken into a game once, nothing done, shipped
/// inside the editor (<c>Characters/Templates/new-character.d2s</c>).
/// <para>
/// It is not one of the player's own characters - the one open, else any in the save folder, else a
/// copy of the last one opened - as what Begin does not clear would come with it: PD2's sections after the golem block
/// hold its summons (<c>su</c>) among others, and a new character would arrive with them. Fourteen made for it in S13 - every class, softcore and hardcore,
/// each taken into a game once - hold PD2's sections byte for byte alike but for the seconds played, which Begin
/// zeroes: one template serves every class and both modes.
/// </para>
/// <para>
/// A new season may write saves another way: the template is used only where it reads as the installed season's
/// save, and is made again then.
/// </para>
/// </summary>
public static class CharacterTemplate
{
    /// <summary>The resource's name in the assembly.</summary>
    private const string Resource = "new-character.d2s";

    /// <summary>Where it is put to be read: the save readers take a file.</summary>
    private static string ReadFrom => Path.Combine(Path.GetTempPath(), "pd2spc-" + Resource);

    /// <summary>
    /// The template, as a file to build on, where it reads as the installed season's save; null where it does not -
    /// a season that writes saves another way - or cannot be put down.
    /// </summary>
    public static string? Fresh()
    {
        try
        {
            using var stream = typeof(CharacterTemplate).Assembly.GetManifestResourceStream(Resource);
            if (stream is null)
                return null;

            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            byte[] template = bytes.ToArray();

            if (!File.Exists(ReadFrom) || !File.ReadAllBytes(ReadFrom).AsSpan().SequenceEqual(template))
                File.WriteAllBytes(ReadFrom, template);

            return SaveVerifier.Load(ReadFrom).Result.Format == SaveFormat.Pd2 ? ReadFrom : null;
        }
        catch (Exception problem)
        {
            Diagnostics.Log.Caught(problem);
            return null;
        }
    }
}
