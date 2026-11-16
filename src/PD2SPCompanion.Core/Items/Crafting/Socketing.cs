using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// Putting a gem, a rune or a jewel into an item's empty socket, as the game does when one is set down on a
/// socketed item: the socketable dragged onto the item goes in, and makes a runeword this way too. Until then the editor only took them out, or made a whole runeword at once
/// (the builder).
/// <para>
/// <b>What fits</b> is the table's: an item whose type comes down to <c>sock</c>, "Socket Filler" - a gem, a
/// rune, a jewel. PD2 keeps gems and runes in stacks, and a stack is another base (<c>r01s</c>, type
/// <c>runs</c>) which is no filler: one of it is socketed as the single it stands for (<c>r01</c>), the name
/// less its last letter, where that is a filler.
/// </para>
/// <para>
/// <b>A runeword</b> forms on the rune that fills the last socket, as in the game: a normal or superior base,
/// every socket holding a rune, the runes in a word's order, the base of a type the word takes and none it
/// leaves out, with exactly as many sockets as the word has runes. Its properties are rolled inside their
/// ranges, each number as likely as the next - the game's own roll.
/// </para>
/// </summary>
public static class Socketing
{
    /// <summary>The type every socket filler comes down to.</summary>
    private const string Filler = "sock";

    /// <summary>
    /// The code an item of this code is socketed as - itself for a gem, a rune or a jewel, the single for a
    /// stack of them - or null for what goes in no socket.
    /// </summary>
    public static string? FillerCode(string? code)
    {
        if (code?.Trim() is not { Length: > 0 } own)
            return null;

        if (ItemTypes.LineageOfBase(own).Contains(Filler))
            return own;

        return own.Length > 1 && Stacks.IsStackable(own) && char.ToLowerInvariant(own[^1]) == 's'
               && ItemTypes.LineageOfBase(own[..^1]).Contains(Filler)
            ? own[..^1]
            : null;
    }

    /// <summary>Whether an item has a socket with nothing in it.</summary>
    public static bool HasRoom(Item host) =>
        !host.IsCompact && host.TotalNumberOfSockets > host.SocketedItems.Count;

    /// <summary>
    /// The word <paramref name="host"/> would become with one more filler of <paramref name="fillerCode"/> in
    /// it, or null: the game's conditions, as the summary has them.
    /// </summary>
    public static Runewords.Word? WordWith(Item host, string fillerCode)
    {
        if (!HasRoom(host) || host.IsRuneword
            || host.Quality is not (ItemQuality.Normal or ItemQuality.Superior)
            || host.SocketedItems.Count + 1 != host.TotalNumberOfSockets)
        {
            return null;
        }

        string[] runes = [.. host.SocketedItems.Select(each => each.Code.Trim()), fillerCode.Trim()];
        var lineage = ItemTypes.LineageOfBase(host.Code);

        return Runewords.All.FirstOrDefault(word =>
            word.Runes.Length == runes.Length
            && word.Runes.SequenceEqual(runes, StringComparer.OrdinalIgnoreCase)
            && word.Types.Any(lineage.Contains)
            && !word.ExcludedTypes.Any(lineage.Contains));
    }

    /// <summary>
    /// What one socket takes of an item lying loose, nothing yet changed: what goes in, and how many of it stay behind.
    /// Whoever holds <c>lying</c> then does as this says, which is what keeps a stack from going in whole on one page
    /// and not on another.
    /// </summary>
    /// <param name="Filler">The item itself, or a single made for one of a stack. Null where <paramref name="Refused"/> says why not.</param>
    /// <param name="Left">Above nought, the count the stack stays at. Nought, it leaves its file: gone in, or gone with its last.</param>
    /// <param name="Refused">Why it cannot go in, said for showing.</param>
    public readonly record struct Taking(Item? Filler, int Left, string? Refused);

    /// <summary>
    /// What one socket of the item named <paramref name="hostName"/>, stamped <paramref name="stamp"/>, takes of
    /// <paramref name="lying"/>, a gem, a rune or a jewel: the thing itself where it is one, and one of a stack where it is
    /// a stack. Season 14's plain runes stack as themselves, so its code alone does not tell. Changes nothing but what it
    /// makes, stamped as the item it goes into, in whose file it will be written.
    /// </summary>
    /// <param name="mix">Whether two stamps may lie in one file (ItemSeason.Mix, a layer above this one).</param>
    public static Taking Take(Item lying, string hostName, string stamp, Func<string, string, bool> mix, Random random)
    {
        string code = FillerCode(lying.Code)!;

        if (!string.Equals(lying.Code.Trim(), code, StringComparison.OrdinalIgnoreCase)
            || (Stacks.IsStackable(code) && Stacks.Shown(lying.Quantity) > 1))
        {
            return new Taking(Single(code, stamp, random), Stacks.Shown(lying.Quantity) - 1, null);
        }

        return lying.Version is { Length: > 0 } own && stamp.Length > 0 && !mix(stamp, own)
            ? new Taking(null, 0, L($"Require same version: item v{own}, {hostName} v{stamp}."))
            : new Taking(lying, 0, null);
    }

    /// <summary>A new single of a filler's code, stamped for the file it goes into, under a new identifier.</summary>
    public static Item Single(string code, string stamp, Random random)
    {
        var filler = ItemForge.Material(code, 1, stamp);
        filler.Id = (uint)random.NextInt64(1, uint.MaxValue);
        return filler;
    }

    /// <summary>
    /// Puts <paramref name="filler"/> - a single, not a stack, and in no list of its own any more - in
    /// <paramref name="host"/>'s next socket; the rune that completes a word makes the host that runeword, its
    /// properties rolled. The word made, or null.
    /// </summary>
    public static Runewords.Word? Put(Item host, Item filler, Random random)
    {
        if (!HasRoom(host))
            throw new InvalidOperationException(L("No empty socket."));

        var word = WordWith(host, filler.Code);
        ItemForge.Socket(host, filler);

        if (word is not null)
            ItemForge.MakeRuneword(host, RunewordBuilder.Roll(word, random));

        return word;
    }
}
