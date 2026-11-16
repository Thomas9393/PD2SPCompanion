using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// Rerolling an item's numbers, after PD2's Demonic Cube.
/// <para>
/// The recipe is <c>imrn</c> in <c>cubemain.txt</c> - sixteen rows, every one described
/// "reroll stats" - and it keeps the item: its base, its name, its affixes and the list of
/// properties they grant. Only the values change, and they may come back better or worse.
/// </para>
/// <para>
/// <b>The socket count stays.</b> Fourteen uniques roll theirs as a <c>sock</c> range - Blade of Ali Baba
/// 2-3, Witchwild String 2-4 - and the game's cube does not roll it again: fifty rerolls of a Blade of
/// Ali Baba in game left its sockets as they were. The recipe's rows name no output
/// (op 31, done in PD2's code), so the tables could not say. Here the count is the item's own field, not
/// a property, and the reroll never reaches it. The game's cube takes an item with something socketed, and
/// rolls the item alone: a jewel inside comes out of it unchanged (in game). The same
/// here - a socketed item is an item of its own, and only this one's lists are walked.
/// The Forge's roll as a new drop rolls the count as well, where it is the item's own range (<see cref="SocketRolls"/>);
/// the Edit page's cube, this function and the CLI's reroll do as the game.
/// </para>
/// <para>
/// In game the recipe is written for unique and set items, and takes no corrupted one (seen in game). The
/// Edit page holds to that outside Free; in Free it offers it on anything with a number worth rolling, a corruption's
/// numbers with the rest.
/// </para>
/// </summary>
public static class DemonicCube
{
    /// <summary>One property the cube moved.</summary>
    public sealed record Rerolled(string Stat, int Was, int Now);

    /// <summary>
    /// Whether there is anything here to roll. An item whose every property is fixed - a
    /// set item's flat bonuses, a plain base - would come back exactly as it went in.
    /// </summary>
    public static bool CanTake(Item item) =>
        item.StatLists.FirstOrDefault() is not null
        && AffixRanges.For(item).Any(range => range.Value.Max > range.Value.Min);

    /// <summary>
    /// What a reroll would land on the item, without touching it. A panel that stages its
    /// edits needs the numbers before anything is committed to them.
    /// </summary>
    public static Rolled Roll(Item item, Random random) =>
        new(AffixRanges.Reroll(item, random, withCorruption: true, AffixRanges.RollScope.Own).Values,
            item.IsRuneword
                ? AffixRanges.Reroll(item, random, withCorruption: false, AffixRanges.RollScope.Runeword).Values
                : new Dictionary<string, int>());

    /// <summary>
    /// The cube's numbers, a list's for each: the item's own list from its own sources, a runeword's list from the
    /// word's (AffixRanges.RollScope). A set's bonuses are fixed and take none. Each keyed by
    /// <see cref="AffixRanges.KeyOf(ItemStat)"/>.
    /// </summary>
    public sealed record Rolled(IReadOnlyDictionary<string, int> Own, IReadOnlyDictionary<string, int> Runeword)
    {
        /// <summary>The numbers for the list a stat is in: <paramref name="list"/> the item's own (0), its runeword's, or a set's (none).</summary>
        public IReadOnlyDictionary<string, int>? For(Item item, int list) =>
            list == 0 ? Own
            : item.IsRuneword && list == item.StatLists.Count - 1 ? Runeword
            : null;
    }

    /// <summary>
    /// Rolls every property on the item again inside its own range.
    /// <para>
    /// Nothing is added and nothing is removed - this is not a reforge, and an item comes
    /// out of it with the same properties it went in with. A corruption is kept and rolled
    /// with the rest, which is what makes a shared property still read as the sum of both.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Rerolled> Reroll(Item item, Random random)
    {
        var moved = new List<Rerolled>();

        if (item.StatLists.Count == 0)
            return moved;

        var rolls = Roll(item, random);

        // Every list, not the first: a runeword keeps its properties in the second one and
        // the cube would have found nothing to roll. Each from its own sources: rolled over
        // all of them, a superior Faith's two Enhanced Damage lines each took the sum.
        for (int index = 0; index < item.StatLists.Count; index++)
        {
            if (rolls.For(item, index) is not { } fresh)
                continue;

            foreach (var stat in item.StatLists[index].Stats)
            {
                if (stat.Stat is not { Length: > 0 } name)
                    continue;

                // By the stat **and** what its parameter names, the way Reroll keys its answer.
                // Asked by the bare name, Halaberd's Reign - "+1 Battle Cry" and "+1 Battle
                // Orders", two lines of item_singleskill - had both lines set to the sum.
                if (!fresh.TryGetValue(AffixRanges.KeyOf(stat), out int rolled))
                    continue;

                // The roll is the sum of every line of the key, and a row can write two: The Iron Jang Bong's faster cast
                // rate, stored as 50 and 10. The first line takes what the others leave of it; given the whole to each,
                // the item read twice its worth.
                var sharing = item.StatLists[index].Stats.Where(other => AffixRanges.KeyOf(other) == AffixRanges.KeyOf(stat)).ToList();
                if (sharing.Count > 1)
                {
                    if (!ReferenceEquals(sharing[0], stat))
                        continue;
                    rolled = Math.Max(0, rolled - sharing.Skip(1).Sum(other => other.Value));
                }

                if (rolled == stat.Value)
                    continue;

                moved.Add(new Rerolled(name, stat.Value, rolled));
                stat.Value = rolled;
            }
        }

        RandomSkillCast.Reroll(item, random);
        return moved;
    }
}
