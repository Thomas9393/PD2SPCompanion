using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>How sure the editor is that the game could not have made this.</summary>
public enum Certainty
{
    /// <summary>
    /// The game could not hold it at all. Either no recipe in the game can put that affix
    /// on that base, or the save format has nowhere to keep it. The editor never offers a
    /// way to make one of these, so finding one means the item came from elsewhere.
    /// </summary>
    Impossible,

    /// <summary>
    /// The game would not have rolled it, but can hold it perfectly well - too many
    /// affixes, an affix above the item level, a number outside its range. Worth saying and
    /// not worth refusing: an editor that cannot make an item the generator would not is an
    /// editor that cannot do its job.
    /// </summary>
    Unrolled,

    /// <summary>
    /// The editor cannot say where a property came from. <b>This is a statement about the
    /// editor, not about the item</b> - the honest word is unaccounted, and it is kept
    /// apart from the other two for that reason. Worth reporting only because accounting
    /// now reaches every property of a real character; while it did not, this meant nothing
    /// but a table the editor had yet to read.
    /// </summary>
    Unaccounted,
}

/// <summary>One thing found, and what it is about.</summary>
public sealed record Finding(Certainty Certainty, string Subject, string Said);

/// <summary>
/// What an item claims about itself, tested against what the game could have produced.
/// <para>
/// The split between <see cref="Certainty.Impossible"/> and <see cref="Certainty.Unrolled"/>
/// is the whole design. Impossible is reserved for what breaks: an affix no recipe can put
/// on that base, and a slot the file cannot store. Everything else the game merely would not
/// have rolled, which is a remark rather than a refusal.
/// </para>
/// </summary>
public static class Legitimacy
{
    /// <summary>
    /// How many affixes the generator gives each quality, as PD2 has it.
    /// <para>
    /// These are what it <b>rolls</b>, not what the file can <b>store</b> - see
    /// <see cref="MagicAffixes.SlotsFor"/> for that, which is the harder limit and the one
    /// that matters. Affix generation can fail to find an eligible pick, so a count below
    /// the floor is not evidence of anything and is never reported.
    /// </para>
    /// </summary>
    private static bool IsJewel(Item item) => MagicAffixes.IsJewel(item);

    /// <summary>
    /// What each item was last found to be, keyed by the item and checked against its bytes: the
    /// rules read only the item and the season's tables, so the same bytes give the same answer - but for what is
    /// told beside an item and is in none of its bytes, a pinned recipe or desecration: such an item is judged afresh.
    /// The UI judges every item on every page rebuild - 663 checks to open a character and 771
    /// for one drag, about 50 ms each time (measured with pd2spc legitcost). Writing an item
    /// costs a fraction of judging it, and a weak table lets items go when nothing else holds them.
    /// The bytes, not a list of fields, so no field a rule reads can be left out of the key.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Item, Judged> Seen = new();

    private sealed record Judged(byte[] Bytes, IReadOnlyList<Finding> Found);

    /// <summary>
    /// What is wrong with the item, remembered by its bytes. Nothing for a legacy item (<see cref="Saves.ItemSeason"/>):
    /// the season before made it by rows this one may not have, and the game keeps it as it was.
    /// <see cref="Judge"/> still asks the rules of one.
    /// </summary>
    public static IReadOnlyList<Finding> Check(Item item)
    {
        if (Saves.ItemSeason.IsLegacy(item))
            return [];

        // Remembered by its bytes, a verdict under a pin would answer for the same bytes with none.
        if (AffixRanges.HasPinnedRecipe(item) || Desecrations.IsPinned(item))
            return Judge(item);

        byte[]? bytes = null;
        try
        {
            bytes = Item.Write(item, PD2SPCompanion.Core.Saves.ItemBlob.Version);
        }
        catch (Exception)
        {
            // An item that will not write is judged afresh each time rather than not at all.
        }

        if (bytes is not null && Seen.TryGetValue(item, out var judged) && judged.Bytes.AsSpan().SequenceEqual(bytes))
            return judged.Found;

        // The same bytes in another object: a character opened again, an undo, a vault page read again - each makes
        // its items anew from their bytes, and every one was judged afresh.
        if (bytes is not null && ByBytes().TryGetValue(new BytesKey(bytes), out var known))
        {
            Seen.AddOrUpdate(item, new Judged(bytes, known));
            return known;
        }

        var found = JudgedByAnyBuild(item);
        if (bytes is not null)
        {
            Seen.AddOrUpdate(item, new Judged(bytes, found));
            var byBytes = ByBytes();
            if (byBytes.Count >= ByBytesMost)
                byBytes.Clear();
            byBytes[new BytesKey(bytes)] = found;
        }
        return found;
    }

    /// <summary>
    /// Judged by this build's rules, and where they find anything on an item that carries affixes, by each earlier build's
    /// of the season too (MagicAffixes.Judging): an item rolled before a patch that moved where an affix may roll is what
    /// that build allowed. Silent under one of them, it is silent; else this build's findings stand.
    /// </summary>
    private static IReadOnlyList<Finding> JudgedByAnyBuild(Item item)
    {
        var found = Judge(item);
        if (found.Count == 0 || MagicAffixes.EarlierBuilds == 0 || !MagicAffixes.Carries(item))
            return found;

        for (int build = 0; build < MagicAffixes.EarlierBuilds; build++)
        {
            using (MagicAffixes.Judging(build))
            {
                if (Judge(item).Count == 0)
                    return [];
            }
        }

        return found;
    }

    /// <summary>What items were found to be, by their bytes - for the season's tables they were judged under.</summary>
    private static System.Collections.Concurrent.ConcurrentDictionary<BytesKey, IReadOnlyList<Finding>> ByBytes()
    {
        int generation = PD2SPCompanion.Core.GameFiles.InstalledTables.Generation;
        var kept = _byBytes;
        if (kept.Generation == generation)
            return kept.Found;

        _byBytes = kept = (generation, new());
        return kept.Found;
    }

    private static (int Generation, System.Collections.Concurrent.ConcurrentDictionary<BytesKey, IReadOnlyList<Finding>> Found) _byBytes = (-1, new());

    /// <summary>Enough for a season's vault and every character beside it; past it, begun again.</summary>
    private const int ByBytesMost = 30_000;

    /// <summary>An item's bytes as a key: equal where every byte is.</summary>
    private readonly struct BytesKey(byte[] bytes) : IEquatable<BytesKey>
    {
        private readonly byte[] _bytes = bytes;
        private readonly int _hash = Hash(bytes);

        private static int Hash(byte[] bytes)
        {
            var hash = new HashCode();
            hash.AddBytes(bytes);
            return hash.ToHashCode();
        }

        public bool Equals(BytesKey other) => _hash == other._hash && _bytes.AsSpan().SequenceEqual(other._bytes);
        public override bool Equals(object? other) => other is BytesKey key && Equals(key);
        public override int GetHashCode() => _hash;
    }

    /// <summary>The rules themselves, every time - what <see cref="Check"/> remembers.</summary>
    public static IReadOnlyList<Finding> Judge(Item item)
    {
        var found = new List<Finding>();

        if (item.IsCompact)
            return found;

        Affixes(item, found);
        Counts(item, found);
        Sockets(item, found);
        SocketSource(item, found);
        Groups(item, found);
        Values(item, found);
        DamageCopies(item, found);
        ElementWords(item, found);
        OneOnly(item, found);
        Ethereal(item, found);
        SkillCast(item, found);

        return found;
    }

    /// <summary>
    /// Ormus' Robes' random skill and its cast (<see cref="RandomSkillCast"/>): one skill of the pool, granted at the
    /// row's <c>par</c> and cast at the chance and level the game gives that skill - as 126 S13 and 350 S14 ladder robes hold them.
    /// Unrolled: the pool and the rule are measured, not read from the tables.
    /// </summary>
    private static void SkillCast(Item item, List<Finding> found)
    {
        foreach (var (par, min, max) in AffixRanges.RandomSkillCastsOn(item))
        {
            var stats = ItemStats.Every(item).ToList();
            var casts = stats.Where(stat => stat.Stat == "item_skilloncast" && RandomSkillCast.Pool.Contains(stat.SkillId ?? -1)).ToList();
            var granted = stats.Where(stat => stat.Stat == "item_singleskill").ToList();

            bool fits = casts.Any(cast =>
                cast.SkillId is int skill
                && cast.Value == RandomSkillCast.Chance(skill, min) && cast.SkillLevel == RandomSkillCast.Level(skill, max)
                && granted.Any(grant => grant.Param == skill && grant.Value == par));
            if (fits)
                continue;

            found.Add(new Finding(
                Certainty.Unrolled, L("Random skill"),
                casts.FirstOrDefault()?.SkillId is int one && SkillNames.NameOf(one) is { } name
                    ? L($"{name}: the row gives +{par} and casts it at {RandomSkillCast.Chance(one, min)}%, level {RandomSkillCast.Level(one, max)}.")
                    : L($"The row's random Sorceress skill and its cast are not on the item.")));
        }
    }

    /// <summary>
    /// Ethereal where the game never makes it (<see cref="Ethereals.Of"/>): a bow, a crossbow,
    /// a Lightsabre, an indestructible unique, a runeword Phase Blade - or plain where its row
    /// makes it ethereal always. Impossible: no such item drops, and the Horadric Cube refuses a Vial of Lightsong
    /// with one (in game) - no route makes one.
    /// </summary>
    private static void Ethereal(Item item, List<Finding> found)
    {
        if (!ItemTypes.IsGear(ItemTypes.TypeOf(item.Code)))
            return;

        var form = Ethereals.Of(item);
        if (item.IsEthereal && form == Ethereals.Form.Never)
            found.Add(new Finding(Certainty.Impossible, L("Ethereal"), L($"Ethereal {ItemPresenter.Describe(item).Name}: the game makes none.")));
        else if (!item.IsEthereal && form == Ethereals.Form.Always)
            found.Add(new Finding(Certainty.Impossible, L("Ethereal"), L($"{ItemPresenter.Describe(item).Name} is always ethereal.")));
    }

/// <summary>
    /// The item levels the item could hold with nothing more said against it: every level from 1 to 99 judged,
    /// and those with the fewest findings kept - a finding that has nothing to do with the level is there at
    /// every one, so the fewest are the levels its affixes, superior bonus, automagic, staff mod, sockets and
    /// row all allow. What the item level die rolls among, an even roll. The item's level
    /// is moved and put back; hand it a copy where anything watches it.
    /// <para>
    /// <b>A unique's or set piece's row level is no floor here.</b> The engine passes over a row above the level
    /// it is making, but the corpus holds over a thousand under theirs: Annihilus (130) and the Hellfire Torch (120)
    /// are given by script, and real Mang Song's Lessons hold 81 and 84 against its 86 (mid regression).
    /// Made a rule, it flagged 236 items of the S12 pack; it was taken back out.
    /// </para>
    /// </summary>
    public static IReadOnlyList<int> LegalItemLevels(Item item)
    {
        if (item.IsCompact)
            return [];

        byte kept = item.ItemLevel;
        var counts = new int[100];
        try
        {
            for (int level = 1; level <= 99; level++)
            {
                item.ItemLevel = (byte)level;
                counts[level] = Judge(item).Count;
            }
        }
        finally
        {
            item.ItemLevel = kept;
        }

        int fewest = counts.Skip(1).Min();
        return [.. Enumerable.Range(1, 99).Where(level => counts[level] == fewest)];
    }

    /// <summary>
    /// A magic, rare or unique map counted as more than one (<see cref="Stacks.IsOneOnly"/>): the game
    /// makes no such stack, and spends the whole of one at a click.
    /// </summary>
    private static void OneOnly(Item item, List<Finding> found)
    {
        if (!Stacks.IsOneOnly(item) || Stacks.Shown(item.Quantity) <= 1)
            return;

        found.Add(new Finding(
            Certainty.Unrolled, L("Quantity"),
            L($"Quantity {Stacks.Shown(item.Quantity)} on a {Named(item.Quality)} map: only a plain map stacks, and the game uses all of it at once.")));
    }

    /// <summary>
    /// A flat damage whose copies are not the ones the base holds (<see cref="DamageKinds"/>): "+9 to
    /// Maximum Damage" is written for one-handed, two-handed and thrown together on anything that is no
    /// weapon, and on a weapon for the kinds it is. One copy alone on a pair of boots gives its damage to
    /// a one-handed weapon and to no other - which is how the editor's own trade import and forge once built
    /// armour, and nothing said so.
    /// <para>
    /// Unrolled, not impossible: the file holds it, and the game reads what is there.
    /// </para>
    /// </summary>
    private static void DamageCopies(Item item, List<Finding> found)
    {
        static string Kinds(IReadOnlyList<string> copies) => string.Join(", ", copies.Select(copy =>
            copy.StartsWith("secondary", StringComparison.OrdinalIgnoreCase) ? L("two-handed")
            : copy.StartsWith("item_throw", StringComparison.OrdinalIgnoreCase) ? L("thrown")
            : L("one-handed")));

        foreach (var gap in DamageKinds.Apart(item))
        {
            bool least = gap.Held[0].Contains("min", StringComparison.OrdinalIgnoreCase);
            found.Add(new Finding(
                Certainty.Unrolled, least ? L("Minimum Damage") : L("Maximum Damage"),
                L($"Written for {Kinds(gap.Held)}, where the game writes it on this base for {Kinds(gap.Wanted)}.")));
        }
    }

    /// <summary>
    /// An elemental skill bonus whose words are not its grant (ElementSkills): the line the game shows and the
    /// levels it gives are two stats, and the game writes them as one number. Apart, the item says one thing and
    /// does another - as a "+N to Fire Skills" written with parameter 0 does, granting nothing.
    /// </summary>
    private static void ElementWords(Item item, List<Finding> found)
    {
        foreach (var gap in ElementSkills.Apart(item))
        {
            string subject = gap.Named && gap.Element > 0 && gap.Element < StatText.Elements.Length
                ? L($"{StatText.Elements[gap.Element]} Skills")
                : L("Elemental Skills");

            found.Add(new Finding(
                Certainty.Unrolled, subject,
                !gap.Named ? L("Names no element, so it grants nothing.")
                : gap.Shown == 0 ? L($"Grants +{gap.Granted} with no line to show it.")
                : gap.Granted == 0 ? L($"Shows +{gap.Shown} and grants nothing.")
                : L($"Shows +{gap.Shown} and grants +{gap.Granted}.")));
        }
    }

    /// <summary>Whether anything here is something the game could not hold.</summary>
    public static bool AnyImpossible(Item item) =>
        Check(item).Any(each => each.Certainty is Certainty.Impossible);

    /// <summary>
    /// More sockets than the base can take.
    /// <para>
    /// The engine caps sockets at the <b>lower</b> of two limits - the base's own
    /// <c>gemsockets</c>, and its type's <c>MaxSock1</c>/<c>MaxSock25</c>/<c>MaxSock40</c>
    /// chosen by the item's level band (1-24, 25-39, 40+). <see cref="ItemBases.MaximumSockets"/>
    /// is that rule, and this is the first thing to ask it a question about legitimacy.
    /// </para>
    /// <para>
    /// <b>Impossible, not merely unrolled.</b> There is no path in the game to a socket the
    /// base cannot hold: not a drop, not the cube recipe - which is stricter still, capping
    /// unique, set, rare and crafted items at one - and not a corruption, which the editor
    /// already holds to the same ceiling.
    /// </para>
    /// <para>
    /// A base the tables do not know is passed over in silence. <c>MaximumSockets</c> returns
    /// zero both for a base that takes none and for one it has never heard of, and calling an
    /// item forged because we could not find its row would be the worst kind of false
    /// positive.
    /// </para>
    /// </summary>
    private static void Sockets(Item item, List<Finding> found)
    {
        int held = Math.Max(item.TotalNumberOfSockets, item.SocketedItems.Count);
        if (held <= 0 || ItemBases.Of(item.Code) is not { } basis)
            return;

        int most = ItemBases.MaximumSockets(item);

        if (basis.GemSockets <= 0)
        {
            found.Add(new Finding(
                Certainty.Impossible, L("Sockets"),
                L($"{held} socket(s), and this base takes none.")));
            return;
        }

        if (held > most)
        {
            found.Add(new Finding(
                Certainty.Impossible, L("Sockets"),
                L($"{held} sockets, and the most this base takes at item level {item.ItemLevel} is {most}.")));
        }
    }

    /// <summary>
    /// More sockets than anything could have bored into an item of its quality: more sockets than Puzzlebox or piece allow, with no corruption
    /// stats, is illegal. <see cref="Sockets"/> holds every item to its base; this holds the
    /// ones no socket recipe takes to the sources they have.
    /// <para>
    /// White, grey and crude items are not asked: a drop, the rune recipes (Ral+Amn and the
    /// rest, <c>nor</c>/<c>hiq</c>) and a corruption all go to the base's own cap. Everything
    /// else has sockets from exactly one of these - each needs an item with none (<c>nos</c>),
    /// so they never stack - and none leaves a mark of which it was:
    /// </para>
    /// <list type="bullet">
    /// <item>Larzuk's Malus, the socket quest's item: 1.</item>
    /// <item>The Puzzlebox or Puzzlepiece (<see cref="Puzzlebox"/>): 2-4 on a two-hander or bow,
    /// 1-2 on the rest.</item>
    /// <item>A magic weapon remade from three gems (cube): 1-2.</item>
    /// <item>A socket affix it carries - Mechanist's 1-2, Artificer's 3, Jeweler's 4; rare too.</item>
    /// <item>A unique's or set piece's own row (<see cref="AffixRanges.OwnSockets"/>).</item>
    /// <item>A corruption whose outcome was sockets (<see cref="Corruptions.Sockets"/>), which the
    /// item says: 1-3 on armour, 2-4 on a one-hander, 3-6 on a two-hander or bow.</item>
    /// </list>
    /// <para>
    /// Measured against the corpus before it was adopted.
    /// </para>
    /// </summary>
    private static void SocketSource(Item item, List<Finding> found)
    {
        int held = Math.Max(item.TotalNumberOfSockets, item.SocketedItems.Count);
        if (held <= 0 || ItemBases.Of(item.Code) is not { GemSockets: > 0 })
            return;

        if (item.Quality is ItemQuality.Normal or ItemQuality.Superior or ItemQuality.Inferior)
            return;

        var (most, from) = SocketsAllowed(item);
        if (held <= most)
            return;

        found.Add(new Finding(
            Certainty.Impossible, L("Sockets"),
            L($"{held} sockets on a {Named(item.Quality)} item, and the most anything bores into it is {most} ({from}) - with no corruption that gave sockets.")));
    }

    /// <summary>The most sockets any source could have given this item, and which source that is.</summary>
    public static (int Most, string From) SocketsAllowed(Item item)
    {
        var sources = new List<(int Count, string From)> { (1, L("Larzuk's Malus")) };

        if (Puzzlebox.RangeFor(item) is { } box)
            sources.Add((box.Most, L("the Puzzlebox")));

        if (item.Quality == ItemQuality.Magic && ItemTypes.LineageOfBase(item.Code).Contains("weap"))
            sources.Add((2, L("the gem recipe")));

        foreach (bool prefix in new[] { true, false })
        {
            foreach (int id in (prefix ? item.MagicPrefixIds : item.MagicSuffixIds) ?? [])
            {
                if (id != 0 && MagicAffixes.Find(id, prefix) is { } affix)
                {
                    foreach (var grant in affix.Grants.Where(g => g.Code.Equals("sock", StringComparison.OrdinalIgnoreCase)))
                        sources.Add((grant.Param > 0 ? grant.Param : Math.Max(grant.Min, grant.Max), affix.Name));
                }
            }
        }

        if (AffixRanges.OwnSockets(item) is { } own)
            sources.Add((own, L("its own row")));

        if (Corruptions.IdOn(item) == Corruptions.Sockets)
        {
            int bored = Corruptions.Available(item, asIfClean: true)
                .Where(option => option.GivesSockets)
                .Select(option => option.SocketsGiven)
                .DefaultIfEmpty(0)
                .Max();
            sources.Add((bored, L("its corruption")));
        }

        var best = sources.MaxBy(each => each.Count);
        int cap = ItemBases.MaximumSockets(item);
        return (cap > 0 ? Math.Min(best.Count, cap) : best.Count, best.From);
    }

    /// <summary>
    /// Two affixes drawn from the same exclusion group.
    /// <para>
    /// The generator will not pick two affixes from one group, and the group space is shared
    /// across <c>magicprefix</c>, <c>magicsuffix</c> and <c>automagic</c> - see
    /// <see cref="MagicAffixes.GroupClash"/>. Groups exist to keep contradictory affixes
    /// apart, so two from one group is something the game could not have rolled.
    /// </para>
    /// <para>
    /// <b>Measured across both vaults before it was written: 0 items violate it.</b> That is
    /// the point of it - it guards against forgery and against our own affix picker, and a
    /// check that fires on nothing real is exactly what a hard rule should look like.
    /// </para>
    /// </summary>
    private static void Groups(Item item, List<Finding> found)
    {
        if (MagicAffixes.GroupClash(item) is not { } clash)
            return;

        found.Add(new Finding(
            Certainty.Impossible, clash.First,
            L($"{clash.First} and {clash.Second} are both from affix group {clash.Group}, and the game picks only one from a group.")));
    }

    /// <summary>
    /// Every affix, asked whether the game could have put it there.
    /// <para>
    /// The question is <b>pool membership for its own slot</b>, which is one question doing
    /// the work of six: the affix must be spawnable, allowed on one of the types this item
    /// has, refused by none of them, marked for rares where the item is rare or crafted,
    /// inside its own maximum level, and not from a group another slot has already spent.
    /// Asking those separately would be a second rulebook to keep in step with the first.
    /// </para>
    /// <para>
    /// The pool deliberately leaves out the affix lowest level, so that is asked separately
    /// - and it is only <see cref="Certainty.Unrolled"/>, since the item holds it perfectly
    /// well and the game simply would not have rolled it that low.
    /// </para>
    /// </summary>
    private static void Affixes(Item item, List<Finding> found)
    {
        if (!MagicAffixes.Carries(item))
            return;

        int storable = MagicAffixes.SlotsFor(item);
        int reach = MagicAffixes.AffixLevel(item);

        // Every level the affixes may have rolled at: a rare or crafted item upgraded in the cube
        // kept the affixes a lower tier gave it - see AffixLevelsAcrossUpgrades.
        var reaches = MagicAffixes.AffixLevelsAcrossUpgrades(item);

        var seen = new HashSet<(bool Prefix, int Id)>();

        foreach (bool prefix in new[] { true, false })
        {
            var ids = prefix ? item.MagicPrefixIds : item.MagicSuffixIds;
            string side = prefix ? "prefix" : "suffix";

            for (int slot = 0; slot < (ids?.Length ?? 0); slot++)
            {
                int id = ids![slot];
                if (id == 0)
                    continue;

                var option = MagicAffixes.Find(id, prefix);
                bool names = MagicAffixes.Names(option);
                string name = names ? option!.Name : $"{side} {id}";

                // A slot the file cannot keep. The writer emits one id a side for a magic
                // item and three for a rare, so anything past that is dropped on save
                // without a word - which is why this is impossible rather than unrolled.
                if (slot >= storable)
                {
                    found.Add(new Finding(
                        Certainty.Impossible, name,
                        L($"{name} sits in {side} slot {slot + 1}, and a {Named(item.Quality)} item stores {storable}. Saving would lose it.")));

                    continue;
                }

                if (!names)
                {
                    // Two different things wear the same empty slot. A row that is there and
                    // switched off is worth naming - the pack's items carry `Maroon`,
                    // `of Chain Lightning` and their kind, all `spawnable 0` - while an id
                    // that names nothing at all is a blank spacer row or past the table's end.
                    string? disabled = MagicAffixes.DisabledName(id, prefix);

                    found.Add(new Finding(
                        Certainty.Impossible, disabled ?? name,
                        disabled is not null
                            ? L($"{disabled} is in the tables but does not spawn, so nothing rolls it onto an item.")
                            : L($"No {side} in the game has id {id}.")));

                    continue;
                }

                if (!seen.Add((prefix, id)))
                {
                    found.Add(new Finding(
                        Certainty.Impossible, name,
                        L($"{name} is on the item twice. No affix is picked more than once.")));

                    continue;
                }

                // Asked with the affix's own ceiling left out, because an affix the item
                // has risen above is one the game does put on this base - just not at this
                // level - and that is a remark, not a refusal. It is reported below,
                // alongside the floor, which was always treated that way.
                if (!MagicAffixes.Offers(item, prefix, slot, id, outgrown: true))
                {
                    // No affix in the game names this base's type, so the pool is silent
                    // about it rather than restrictive - and a silence is not a refusal.
                    // An uber map is the case: the game shows every affix we called
                    // impossible.
                    if (!MagicAffixes.Judgeable(item))
                    {
                        found.Add(new Finding(
                            Certainty.Unaccounted, name,
                            L($"The tables say nothing about what {ItemTypes.ShownNameOf(ItemTypes.TypeOf(item.Code) ?? "")} items may carry, so {name} cannot be judged.")));

                        continue;
                    }

                    found.Add(MagicAffixes.SameFamilyOtherTier(item, option!)
                        ? new Finding(
                            Certainty.Unrolled, name,
                            L($"{name} belongs to another tier of this base, not to {ItemTypes.ShownNameOf(ItemTypes.TypeOf(item.Code) ?? "")}."))
                        : new Finding(
                            Certainty.Impossible, name,
                            L($"{name} is not an affix this item can carry.")));

                    continue;
                }

                // Fits some tier it could have rolled on - at or under its level, not above a ceiling.
                if (reaches.Count > 1
                    && reaches.Any(level => option!.Level <= level && (option.MaxLevel == 0 || level <= option.MaxLevel)))
                    continue;

                if (option!.Level > reach)
                {
                    found.Add(new Finding(
                        Certainty.Unrolled, name,
                        L($"{name} needs affix level {option.Level}; this item reaches {reach}, which wants item level {MagicAffixes.ItemLevelFor(item, option.Level)}.")));
                }
                else if (option.MaxLevel > 0 && reach > option.MaxLevel)
                {
                    found.Add(new Finding(
                        Certainty.Unrolled, name,
                        L($"{name} stops rolling above affix level {option.MaxLevel}; this item reaches {reach}.")));
                }
            }
        }
    }

    /// <summary>
    /// How many affixes it carries against how many the generator gives.
    /// <para>
    /// Only the ceiling is reported. A count under the floor happens in the game: a pick
    /// can find nothing eligible at the item level and simply not place one, which is how a
    /// rare ends up carrying three.
    /// </para>
    /// </summary>
    private static void Counts(Item item, List<Finding> found)
    {
        if (!MagicAffixes.Carries(item))
            return;

        var (_, most) = MagicAffixes.CountFor(item);
        if (most == 0)
            return;

        int carried = (item.MagicPrefixIds ?? []).Count(id => id > 0)
                      + (item.MagicSuffixIds ?? []).Count(id => id > 0);

        if (carried > most)
        {
            found.Add(new Finding(
                Certainty.Unrolled, L("Affixes"),
                IsJewel(item)
                    ? L($"{carried} affixes, where a {Named(item.Quality)} jewel rolls at most {most}.")
                    : L($"{carried} affixes, where a {Named(item.Quality)} rolls at most {most}.")));
        }
    }

    /// <summary>
    /// Every property, against the range its own sources could have rolled - and against
    /// whether it has a source at all.
    /// <para>
    /// Ranged <b>per property list</b>, not across the item. An item keeps its own
    /// properties in the first list and what a runeword or a set added in the ones after,
    /// and the two must be asked separately: a Leaf staff carries Fire Bolt from its own
    /// staffmod and again from the runeword, and adding those ranges together gives a span
    /// wide enough that either line can sit anywhere inside it. That is how a staffmod of
    /// +6 passed for legitimate, and - asked the other way round - how this check called a
    /// real Leaf a forgery twice over.
    /// </para>
    /// <para>
    /// Properties in the same list that write the same stat are summed before comparing,
    /// since the item keeps one line for the pair of them.
    /// </para>
    /// </summary>
    private static void Values(Item item, List<Finding> found, List<Unfit>? unfit = null)
    {
        var own = AffixRanges.OwnOf(item);
        var added = AffixRanges.AddedTo(item);
        var accounted = AffixRanges.Accounted(item, own.Keys.Concat(added.Keys));

        // The packed properties' fields, worked out once. It walks every source the item has,
        // so asking per stat would walk them again for each line.
        var fields = AffixRanges.PackedOn(item);
        IReadOnlyDictionary<string, IReadOnlyList<AffixRanges.PackedFields>>? fixedFields = null;

        for (int which = 0; which < item.StatLists.Count; which++)
        {
            var list = item.StatLists[which];

            // A set piece's green lists each against their own tier; a runeword's against it.
            var ranges = which == 0 ? own
                : item.Quality == ItemQuality.Set ? AffixRanges.AddedTo(item, which)
                : added;

            foreach (var group in list.Stats
                         .Where(stat => stat.Stat is { Length: > 0 })
                         .Where(stat => stat.Stat is not (Corruptions.IdStat or Corruptions.FlagStat
                                                          or Desecrations.ValueStat or Desecrations.FlagStat))
                         .GroupBy(AffixRanges.KeyOf))
            {
                var first = group.First();
                string name = first.Stat!;
                int held = group.Sum(stat => stat.Value);

                // By the parameter first, where one names what was granted. Describe is not
                // given it, so a class-skill line worded through that route came out as the
                // Amazon's whatever class it really was - which made 66 findings all name the
                // wrong class and sent me looking for a bug in how the class is read.
                // Worded only where a finding says it: most lines have none, and each was worded for nothing.
                string? wordedOnce = null;
                string Worded() => wordedOnce ??= StatText.ByParameter(name, held, first.SkillId ?? first.Param ?? 0)
                                ?? StatText.Describe(
                                    name, held, SkillNames.NameOf(StatText.SkillOf(name, first.SkillId, first.Param)),
                                    first.SkillLevel, first.MaxCharges)
                                ?? name;

                // A stat whose Value is not a magnitude cannot be measured against a range,
                // and three shapes are like that. A chance to cast keeps the chance in the
                // value and the level beside it; a charged skill keeps charges and a level;
                // item_nonclassskill keeps the **skill id** in the value and the level in the
                // parameter. The range a row offers for these is one of those fields, never
                // the value, so comparing the two calls real items fake: Andariel's Visage,
                // "Level 3 Venom (14/20 Charges)", was measured against 20 - its own max
                // charges - and flagged, and 267 findings across the pack were this.
                //
                // Nothing was lost by not checking them before: the parameter never reached
                // the range side, so the key never matched and the comparison never ran. It
                // runs now, so it has to be told to stop here. **Checking these properly
                // means comparing field to field** - chance to `min`, level to `max`, charges
                // to `min` - which the range side cannot express yet, since RangeOf collapses
                // a packed row to one number. That is the next piece of the multi-field audit.
                // A packed stat is three numbers wearing one line, and the row is three
                // columns to match: `par` the skill, `min` the chance (func 11) or the max
                // charges (func 19), `max` the level. Its Value is therefore not a magnitude
                // and a range cannot judge it - measuring one against the other called
                // Andariel's Visage, "Level 3 Venom (14/20 Charges)", outside 20, which is its
                // own max charges. So these are checked field against field instead.
                var shape = StatShapes.Of(name);
                bool packed = shape is StatShape.Chance or StatShape.Charges;

                if (packed && fields.TryGetValue(group.Key, out var wanted))
                {
                    // Each line on its own: a skill's chance is keyed by the skill and not the level, so a Hitpower
                    // recipe's level 4 and an affix's level 3 are one group and two lines. Judged by the first, the
                    // second was never looked at, and a craft's line standing first was held to the affix's numbers.
                    foreach (var line in group)
                    {
                        // Encode 2 keeps the chance in the value; encode 3 keeps the **current**
                        // charges there and the cap beside it, and it is the cap the row states -
                        // current charges count down as the item is used.
                        int level = line.SkillLevel ?? 0;
                        int carried = shape is StatShape.Chance ? line.Value : line.MaxCharges ?? 0;

                        string LineWorded() => StatText.ByParameter(name, line.Value, line.SkillId ?? line.Param ?? 0)
                                               ?? StatText.Describe(
                                                   name, line.Value, SkillNames.NameOf(StatText.SkillOf(name, line.SkillId, line.Param)),
                                                   line.SkillLevel, line.MaxCharges)
                                               ?? name;

                        // Any source fitting is enough: a line is one source's. A craft's fixed row counts here and
                        // only here: a stat no ranged source grants is judged as it was, by the source being accounted.
                        fixedFields ??= AffixRanges.PackedFixedOn(item);
                        if (!wanted.Any(want => want.Level == level && want.First == carried)
                            && !(fixedFields.TryGetValue(group.Key, out var granted)
                                 && granted.Any(want => want.Level == level && want.First == carried)))
                        {
                            var want = wanted[0];
                            string worded = LineWorded();
                            found.Add(new Finding(
                                Certainty.Unrolled, worded,
                                shape is StatShape.Chance
                                    ? L($"{worded} holds {carried}% chance at level {level}; its source grants {want.First}% chance at level {want.Level}.")
                                    : L($"{worded} holds {carried} charges at level {level}; its source grants {want.First} charges at level {want.Level}.")));
                        }

                        // Charges spend down and never up, so a line holding more than its own
                        // maximum is a number the game could not have left there.
                        else if (shape is StatShape.Charges && line.Value > (line.MaxCharges ?? 0))
                        {
                            string worded = LineWorded();
                            found.Add(new Finding(
                                Certainty.Unrolled, worded,
                                L($"{worded} has {line.Value} charges left of a maximum of {line.MaxCharges ?? 0}.")));
                        }
                    }

                    continue;
                }

                // A source that cannot name its subject - a random class, a skill drawn from a
                // range - registers under the bare stat name. Finding it there means the
                // property is **granted**, and that is all it means: the numbers such a row
                // carries are the subject it drew from, not the value. Spirit Keeper's
                // `skill-rand 221-250` is a span of skill ids, and measuring a skill level
                // against it calls every real one impossible. So the bare name accounts for
                // the stat and stops there; only a keyed source ranges it.
                if (!ranges.ContainsKey(group.Key) && ranges.ContainsKey(name))
                    continue;

                if (!packed && ranges.TryGetValue(group.Key, out var range))
                {
                    // What the row asks for and what the field can hold are two different
                    // numbers, and where they disagree the field wins: the game clamps. A
                    // real Eaglehorn off the trade site carries 63 against a row asking
                    // 1250, so measuring 63 against 1250 calls every genuine one a fake -
                    // the same mistake magiclvl caused on circlets.
                    // Both ends: a map's -35 to -25 on a field whose floor is -20 can hold only -20 (AffixRanges.Held).
                    if (StatFields.RangeOf(name) is { } field)
                        range = range with { Min = Math.Clamp(range.Min, field.Min, field.Max), Max = Math.Clamp(range.Max, field.Min, field.Max) };

                    if (!range.Holds(held))
                    {
                        unfit?.Add(new Unfit(which, [.. group], range));
                        string worded = Worded();
                        found.Add(new Finding(
                            Certainty.Unrolled, worded,
                            L($"{worded} is outside {range}, which is what its sources could roll.")));
                    }

                    continue;
                }

                // A parameter that names what was granted is part of the key, so a stat
                // whose name has ranges but none under this parameter is not unaccounted at
                // all: its source is known, and the parameter is not one that source could
                // have granted. An Amazon spear reading "+1 to Lightning Skills (Sorceress
                // Only)" is the case - the property belongs there, the tree does not.
                //
                // Unrolled rather than impossible, because the item holds it perfectly well
                // and the editor offers the choice: the skill-tree picker lists every tree.
                // Impossible is reserved for what the editor never offers.
                var elsewhere = ranges.Keys
                    .Where(key => key.StartsWith(name + "#", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (group.Key != name && elsewhere.Count > 0)
                {
                    unfit?.Add(new Unfit(which, [.. group], null));

                    // Worded from the parameter the item actually holds. The plain
                    // description cannot do it: it is handed the skill id and not the tree,
                    // so it names whichever tree it saw first and contradicts itself.
                    int param = first.SkillId ?? first.Param ?? 0;
                    string mine = StatText.ByParameter(name, held, param) ?? Worded();

                    var theirs = elsewhere
                        .Select(key => int.TryParse(key[(name.Length + 1)..], out int which)
                            ? StatText.ByParameter(name, held, which)
                            : null)
                        .Where(text => text is { Length: > 0 })
                        .Distinct()
                        .ToList();

                    found.Add(new Finding(
                        Certainty.Unrolled, mine,
                        theirs.Count > 0
                            ? L($"{mine} is not one of the {Named(name)} this item's sources grant; they grant {string.Join(", ", theirs)}.")
                            : L($"{mine} is not one of the {Named(name)} this item's sources grant.")));

                    continue;
                }

                // No range is not the same as no source: a property taking its value from a
                // parameter has nothing to roll inside and is still accounted for.
                if (accounted.Contains(name))
                    continue;

                {
                    unfit?.Add(new Unfit(which, [.. group], null));
                    string worded = Worded();
                    found.Add(new Finding(
                        Certainty.Unaccounted, worded,
                        L($"Nothing on this item accounts for {worded}.")));
                }
            }
        }
    }

    /// <summary>
    /// A property line the item's sources do not account for under the installed tables: the list it is in, the stats
    /// writing it, and the range they could roll - null where no source grants it at all.
    /// </summary>
    public sealed record Unfit(int List, IReadOnlyList<ItemStat> Stats, RollRange? Range);

    /// <summary>
    /// The lines <see cref="Check"/> words as outside their range or without a source, as the stats themselves - for a
    /// caller that puts them right (the season carry). Packed lines (a chance to
    /// cast, charges) holding the wrong level are not among them: <see cref="AffixRanges.DerivePacked"/> answers those.
    /// </summary>
    public static IReadOnlyList<Unfit> UnfitOn(Item item)
    {
        var unfit = new List<Unfit>();
        Values(item, [], unfit);
        return unfit;
    }

    /// <summary>What the parameter of this stat picks out, said for a person.</summary>
    private static string Named(string stat) => StatShapes.Of(stat) switch
    {
        StatShape.SkillTree => L("skill trees"),
        StatShape.Skill => L("skills"),
        StatShape.Class => L("classes"),
        StatShape.Monster => L("monsters"),
        StatShape.State => L("states"),
        _ => L("values"),
    };

    private static string Named(ItemQuality quality) => quality switch
    {
        ItemQuality.Magic => L("magic"),
        ItemQuality.Rare => L("rare"),
        ItemQuality.Craft => L("crafted"),
        _ => L(quality.ToString().ToLowerInvariant()),
    };
}
