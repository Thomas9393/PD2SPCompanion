using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// The name on a personalized item - Anya's reward for the Prison of Ice: the item reads "Name's Item" from then
/// on. The save keeps a flag and the name, seven bits a letter, with an item's own part; an item written in the
/// short form (a gem, a potion) has nowhere for one, and an ear's name is whose ear it is.
/// <para>
/// Read, written and worded here; it was only in the command line's dump.
/// </para>
/// </summary>
public static class Personalization
{
    /// <summary>Whether an item can carry a name at all.</summary>
    public static bool Takes(Item item) => !item.IsCompact && !item.IsEar;

    /// <summary>The name on the item, or "" where it has none.</summary>
    public static string NameOn(Item item) =>
        Takes(item) && item.IsPersonalized ? item.PlayerName?.Trim() ?? "" : "";

    /// <summary>An item's name as the game heads it: "Name's Item" where it is personalized.</summary>
    public static string Titled(Item item, string name) =>
        NameOn(item) is { Length: > 0 } whose ? $"{whose}'s {name}" : name;

    /// <summary>
    /// Puts a name on the item, or with "" takes it off: the flag and the name together. Which names the game
    /// takes is the caller's to ask - a character's name, by the rule for those (Characters.CharacterRename).
    /// </summary>
    public static void Set(Item item, string name)
    {
        name = name.Trim();
        item.IsPersonalized = name.Length > 0;
        item.PlayerName = name.Length > 0 ? name : null;
    }
}
