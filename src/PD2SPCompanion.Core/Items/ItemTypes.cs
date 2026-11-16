using System.Text;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// The item type tree from <c>itemtypes.txt</c>, and what belongs to what.
/// <para>
/// A type names its parents in <c>Equiv1</c> and <c>Equiv2</c>, and the tables lean on that
/// everywhere: a rare name allowed on <c>weap</c> is allowed on a Throwing Knife because
/// <c>tkni</c> reaches <c>weap</c> through <c>comb</c>. Anything that matches a type against
/// a family has to walk that chain, which is why it is in one place rather than copied into
/// each caller.
/// </para>
/// </summary>
public static class ItemTypes
{
    private static Dictionary<string, string[]> _parents = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, string> _named = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The type each base item belongs to, across all three item tables.</summary>
    private static Dictionary<string, string> _typeOfCode = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A base's second type, <c>type2</c>, where it has one. See <see cref="LineageOfBase"/>.</summary>
    private static Dictionary<string, string> _secondTypeOfCode = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The graphics a type varies between, in the order the game numbers them.
    /// <para>
    /// A type with <c>VarInvGfx</c> names each file outright in <c>InvGfx1..6</c> rather
    /// than leaving them to be composed from the base's own <c>invfile</c>. A jewel names
    /// <c>invjw1</c> to <c>invjw6</c> while its base says <c>invgswe</c>, so composing from
    /// the base draws a gem or nothing at all; and the charms interleave -
    /// <c>invch1/4/7</c>, <c>invch2/5/8</c>, <c>invch3/6/9</c> - which is a pattern worth
    /// reading rather than working out.
    /// </para>
    /// </summary>
    private static Dictionary<string, string[]> _variants = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the type is gear: something with durability that a smith can mend.</summary>
    private static Dictionary<string, bool> _repairable = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The class a type is made for, by its <c>Class</c> column: <c>ama</c>, <c>ass</c> and so on.</summary>
    private static Dictionary<string, string> _classOf = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The class a base is made for - "ama" for an amazon bow, "pal" for an auric shield - by
    /// its own type's <c>Class</c> column; null for a base every class can use.
    /// <para>
    /// Its own type only, never one above it: PD2 hangs knives under "Assassin Mastery"
    /// (<c>asm</c>, class <c>ass</c>) to give them assassin staff mods, and read through the
    /// lineage every dagger became an assassin weapon (measured: the smart sort's
    /// forged set had no daggers). Every real class base names its class on its own type.
    /// </para>
    /// </summary>
    public static string? ClassOfBase(string? code) => ClassOfType(TypeOf(code));

    /// <summary>The class a type is made for, by its own <c>Class</c> column; null for a type every class uses.</summary>
    public static string? ClassOfType(string? type) =>
        type is not null && _classOf.TryGetValue(type, out string? found) ? found : null;

    /// <summary>
    /// A question of a type answered once and kept, until the types are read again: each was a walk up the tree with
    /// a set and a queue of its own, asked of every item a list or a judge goes through.
    /// </summary>
    private static T Kept<T>(System.Collections.Concurrent.ConcurrentDictionary<string, T> kept, string? type, Func<string?, T> work)
    {
        // Read without a lock: a search's worker asks these too, and a lock had the UI thread waiting on it.
        string key = type?.Trim() ?? "";
        if (kept.TryGetValue(key, out var known))
            return known;

        var made = work(type);
        kept[key] = made;
        return made;
    }

    /// <summary>
    /// Whether this is gear - a weapon or a piece of armour - as against a ring, a charm, a
    /// quiver or anything else carried rather than worn out.
    /// <para>
    /// <c>itemtypes.txt</c> answers it in <c>Repair</c>, the flag for a type a smith can
    /// mend, walked up the <c>Equiv</c> chain. It is the game's own line between gear and
    /// everything else, and it falls in exactly the right place: every base in
    /// <c>armor.txt</c> and <c>weapons.txt</c> is inside it and every base in
    /// <c>misc.txt</c> is outside, 461 against 307.
    /// </para>
    /// <para>
    /// <b>Not</b> the <c>nodurability</c> column, which does not mean what its name says:
    /// a Short Bow has durability 20 and the flag set.
    /// </para>
    /// </summary>
    public static bool IsGear(string? type) =>
        Kept(_keptIsGear, type, IsGearWorked);

    private static System.Collections.Concurrent.ConcurrentDictionary<string, bool> _keptIsGear = new(StringComparer.OrdinalIgnoreCase);

    private static bool IsGearWorked(string? type)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();

        if (type?.Trim() is { Length: > 0 } start)
            queue.Enqueue(start);

        while (queue.Count > 0)
        {
            string code = queue.Dequeue();
            if (!seen.Add(code))
                continue;

            if (_repairable.TryGetValue(code, out bool mendable) && mendable)
                return true;

            foreach (string above in _parents.TryGetValue(code, out var up) ? up : [])
                queue.Enqueue(above);
        }

        return false;
    }

    /// <summary>Whether a type can be magic, can be rare, or can only ever be normal.</summary>
    private static Dictionary<string, (bool Magic, bool Rare, bool Normal)> _quality = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the game would ever roll an affix onto an item of this type.
    /// <para>
    /// <c>itemtypes.txt</c> says so outright, in <c>Magic</c>, <c>Rare</c> and
    /// <c>Normal</c>. <b>The item's own type answers first</b>, and only a type that says
    /// nothing at all defers to its parent - which is the whole point here. A Chilling
    /// Potion is a <c>tpot</c>, whose parent chain runs through <c>thro</c> to <c>weap</c>
    /// and says Rare; <c>tpot</c> itself says <c>Normal</c>, and that is the answer.
    /// </para>
    /// </summary>
    public static bool CanRollAffixes(string? type) =>
        Kept(_keptCanRollAffixes, type, CanRollAffixesWorked);

    private static System.Collections.Concurrent.ConcurrentDictionary<string, bool> _keptCanRollAffixes = new(StringComparer.OrdinalIgnoreCase);

    private static bool CanRollAffixesWorked(string? type)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();

        if (type?.Trim() is { Length: > 0 } start)
            queue.Enqueue(start);

        while (queue.Count > 0)
        {
            string code = queue.Dequeue();
            if (!seen.Add(code) || !_quality.TryGetValue(code, out var says))
                continue;

            if (says.Normal)
                return false;
            if (says.Magic || says.Rare)
                return true;

            foreach (string above in _parents.TryGetValue(code, out var up) ? up : [])
                queue.Enqueue(above);
        }

        return false;
    }

    /// <summary>Every base of an item type - one for a Grand Charm's, dozens for a helm's.</summary>
    public static IReadOnlyList<string> BasesOf(string type) =>
        [.. _typeOfCode.Where(each => string.Equals(each.Value, type, StringComparison.OrdinalIgnoreCase))
                       .Select(each => each.Key)];

    /// <summary>The item type a base belongs to, or null when no table names it.</summary>
    public static string? TypeOf(string? code) =>
        code?.Trim() is { Length: > 0 } id && _typeOfCode.TryGetValue(id, out string? type)
            ? type
            : null;

    /// <summary>
    /// How many inventory graphics this base can wear. One for most things; a charm, a ring,
    /// an amulet or a jewel has several, and which one it shows is a number on the item.
    /// </summary>
    public static int VariantCount(string? code) =>
        code?.Trim() is { Length: > 0 } id
        && _typeOfCode.TryGetValue(id, out string? type)
        && _variants.TryGetValue(type, out string[]? files)
            ? files.Length
            : 0;

    /// <summary>
    /// The graphic this item shows, for a base whose type varies them. Null where the type
    /// names none, which leaves the base's own <c>invfile</c> to answer.
    /// </summary>
    public static string? VariantGraphic(string? code, int graphicId)
    {
        if (code?.Trim() is not { Length: > 0 } id
            || !_typeOfCode.TryGetValue(id, out string? type)
            || !_variants.TryGetValue(type, out string[]? files)
            || files.Length == 0)
        {
            return null;
        }

        // Past the end is the game's own business; the first is the safe answer.
        return graphicId >= 0 && graphicId < files.Length ? files[graphicId] : files[0];
    }

    /// <summary>
    /// What <c>itemtypes.txt</c> says of a type's quality - always magic, may be rare, only ever normal -
    /// read as <see cref="CanRollAffixes"/> reads it: the type's own word first, a type that says nothing
    /// deferring to its parent. A ring is magic and may be rare; a charm is magic and never rare; armour may
    /// be rare, and so may be anything under it.
    /// </summary>
    public static (bool Magic, bool Rare, bool Normal) QualityOf(string? type) =>
        Kept(_keptQualityOf, type, QualityOfWorked);

    private static System.Collections.Concurrent.ConcurrentDictionary<string, (bool Magic, bool Rare, bool Normal)> _keptQualityOf = new(StringComparer.OrdinalIgnoreCase);

    private static (bool Magic, bool Rare, bool Normal) QualityOfWorked(string? type)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();

        if (type?.Trim() is { Length: > 0 } start)
            queue.Enqueue(start);

        while (queue.Count > 0)
        {
            string code = queue.Dequeue();
            if (!seen.Add(code) || !_quality.TryGetValue(code, out var says))
                continue;

            if (says.Normal || says.Magic || says.Rare)
                return says;

            foreach (string above in _parents.TryGetValue(code, out var up) ? up : [])
                queue.Enqueue(above);
        }

        return default;
    }

    /// <summary>
    /// The type whose own row answers <see cref="QualityOf"/> for this one: itself, or the ancestor it
    /// defers to. Null where nothing on the way up says anything.
    /// </summary>
    public static string? QualitySaidBy(string? type) =>
        Kept(_keptQualitySaidBy, type, QualitySaidByWorked);

    private static System.Collections.Concurrent.ConcurrentDictionary<string, string?> _keptQualitySaidBy = new(StringComparer.OrdinalIgnoreCase);

    private static string? QualitySaidByWorked(string? type)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();

        if (type?.Trim() is { Length: > 0 } start)
            queue.Enqueue(start);

        while (queue.Count > 0)
        {
            string code = queue.Dequeue();
            if (!seen.Add(code) || !_quality.TryGetValue(code, out var says))
                continue;

            if (says.Normal || says.Magic || says.Rare)
                return code;

            foreach (string above in _parents.TryGetValue(code, out var up) ? up : [])
                queue.Enqueue(above);
        }

        return null;
    }

    /// <summary>Whether a type stands at the root of the tree: it names no parent.</summary>
    public static bool IsRoot(string? type) =>
        type?.Trim() is { Length: > 0 } code && !(_parents.TryGetValue(code, out var up) && up.Length > 0);

    /// <summary>
    /// What a type is called on screen, as the game displays it. <c>itemtypes.txt</c> names its types
    /// for the engine, and a few of those names no player would know: the Assassin's claws are "Hand to Hand"
    /// and "Hand to Hand 2", orbs "Sorceress Item", Paladin shields "Paladin Item", quivers "Missile", body
    /// armor "Armor". Those are said as the game and this program's other pages say them; every other type
    /// by the table's own name. For showing only - a type is matched and ordered by <see cref="NameOf"/>.
    /// </summary>
    public static string ShownNameOf(string code) => code.Trim().ToLowerInvariant() switch
    {
        "h2h" or "h2h2" => L("Claw"),
        "sorc" => L("Orb"),
        "pala" => L("Paladin Shield"),
        "shld" => L("Shield"),
        "tors" => L("Chest"),
        "misl" => L("Quiver"),
        "2hsw" => L("Two-Handed Sword"),
        "sc9" => L("Scythe"),
        "thro" => L("Throwing Weapon"),
        _ => NameOf(code),
    };

    /// <summary>What a type is called - "Throwing Knife" for <c>tkni</c>.</summary>
    public static string NameOf(string code) =>
        _named.TryGetValue(code, out string? name) && name.Length > 0 ? name : code;

    public static void Load(GameData game)
    {
        var found = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var variants = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var quality = new Dictionary<string, (bool Magic, bool Rare, bool Normal)>(StringComparer.OrdinalIgnoreCase);
        var repairable = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var classOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (game.Lines("itemtypes") is not { } lines)
                return;
            if (lines.Length == 0)
                return;

            string[] header = lines[0].Split('\t');
            int code = Array.IndexOf(header, "Code");
            int label = Array.IndexOf(header, "ItemType");
            int magic = Array.IndexOf(header, "Magic");
            int rare = Array.IndexOf(header, "Rare");
            int normal = Array.IndexOf(header, "Normal");
            int mends = Array.IndexOf(header, "Repair");
            int forClass = Array.IndexOf(header, "Class");
            int[] graphics = [.. Enumerable.Range(1, 6)
                .Select(n => Array.IndexOf(header, $"InvGfx{n}"))];
            int first = Array.IndexOf(header, "Equiv1");
            int second = Array.IndexOf(header, "Equiv2");

            if (code < 0)
                return;

            foreach (string line in lines.Skip(1))
            {
                string[] cells = line.Split('\t');
                if (cells.Length <= code || cells[code].Trim().Length == 0)
                    continue;

                if (label >= 0 && cells.Length > label)
                    named[cells[code].Trim()] = cells[label].Trim();

                bool Set(int column) =>
                    column >= 0 && cells.Length > column && cells[column].Trim() is { Length: > 0 } flag
                    && flag != "0";

                quality[cells[code].Trim()] = (Set(magic), Set(rare), Set(normal));
                repairable[cells[code].Trim()] = Set(mends);

                if (forClass >= 0 && cells.Length > forClass && cells[forClass].Trim() is { Length: > 0 } madeFor)
                    classOf[cells[code].Trim()] = madeFor;

                string[] files = [.. graphics
                    .Where(column => column >= 0 && cells.Length > column)
                    .Select(column => cells[column].Trim())
                    .TakeWhile(file => file.Length > 0)];

                if (files.Length > 0)
                    variants[cells[code].Trim()] = files;

                found[cells[code].Trim()] =
                [
                    .. new[] { first, second }
                        .Where(column => column >= 0 && cells.Length > column)
                        .Select(column => cells[column].Trim())
                        .Where(parent => parent.Length > 0),
                ];
            }
        }
        catch (Exception)
        {
            // An empty tree matches nothing but itself, which narrows what is offered
            // rather than widening it.
        }

        _parents = found;
        _ancestries = new(StringComparer.OrdinalIgnoreCase);
        _keptIsGear = new(StringComparer.OrdinalIgnoreCase);
        _keptCanRollAffixes = new(StringComparer.OrdinalIgnoreCase);
        _keptQualityOf = new(StringComparer.OrdinalIgnoreCase);
        _keptQualitySaidBy = new(StringComparer.OrdinalIgnoreCase);
        _typesSeen.Clear();
        _basesSeen.Clear();
        _named = named;
        _variants = variants;
        _quality = quality;
        _repairable = repairable;
        _classOf = classOf;
        _typeOfCode = ReadBaseTypes(game, "type");
        _secondTypeOfCode = ReadBaseTypes(game, "type2");
    }

    /// <summary>
    /// Which type each base belongs to. Read from all three item tables, since a jewel and
    /// a charm are misc while a sword is not.
    /// </summary>
    private static Dictionary<string, string> ReadBaseTypes(GameData game, string column)
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string table in new[] { "misc", "weapons", "armor" })
        {
            try
            {
                if (game.Lines(table) is not { } lines)
                    continue;
                if (lines.Length == 0)
                    continue;

                string[] header = lines[0].Split('\t');
                int code = Array.IndexOf(header, "code");
                int type = Array.IndexOf(header, column);
                if (code < 0 || type < 0)
                    continue;

                foreach (string line in lines.Skip(1))
                {
                    string[] cells = line.Split('\t');
                    if (cells.Length <= Math.Max(code, type))
                        continue;

                    if (cells[code].Trim() is { Length: > 0 } id
                        && cells[type].Trim() is { Length: > 0 } kind)
                    {
                        found[id] = kind;
                    }
                }
            }
            catch (Exception)
            {
                // A table that will not read leaves those bases without a type, which
                // falls back to the base's own graphic rather than guessing one.
            }
        }

        return found;
    }

    /// <summary>
    /// Every type a base belongs to, nearest first: its <c>type</c> and that type's parents, then
    /// its <c>type2</c> and that one's. The game asks both when it checks a base against a type -
    /// PD2 gives 18 two-handed swords <c>type2 2hsw</c>, and Asylum names <c>2hsw</c> and no sword:
    /// asked by the first type alone, a level 99 Barbarian's Asylum Colossus Blades matched no runeword and all
    /// their properties read unaccounted. The same second type makes one-handed and
    /// two-handed weapons (<c>1han</c>, <c>2han</c>), small and big shields and crystal swords.
    /// </summary>
    public static IReadOnlyList<string> LineageOfBase(string? code)
    {
        // Kept per base: the grail's runeword list asked it of every base for every word, 440 ms on
        // each open (measured). The tables do not change until the next Load.
        // Read without a lock: asked of every base for every runeword and crafting card, the
        // lock alone was 85 ms of a start-up (a trace) - taken and let go some hundred thousand times.
        string key = code?.Trim() ?? "";
        if (_basesSeen.TryGetValue(key, out var kept))
            return kept;

        return _basesSeen[key] = BaseLineage(code);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string[]> _basesSeen = new(StringComparer.OrdinalIgnoreCase);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string[]> _typesSeen = new(StringComparer.OrdinalIgnoreCase);

    private static string[] BaseLineage(string? code)
    {
        var order = new List<string>(Lineage(TypeOf(code)));
        if (code?.Trim() is { Length: > 0 } id && _secondTypeOfCode.TryGetValue(id, out string? second))
        {
            foreach (string step in Lineage(second))
            {
                if (!order.Contains(step, StringComparer.OrdinalIgnoreCase))
                    order.Add(step);
            }
        }

        return [.. order];
    }

    /// <summary>
    /// The chain <see cref="Ancestry"/> gives, nearest first: a type, then its parents, then theirs. Used to pick
    /// the closest family an item belongs to out of a list that holds several of them.
    /// </summary>
    public static IReadOnlyList<string> Lineage(string? type)
    {
        string key = type?.Trim() ?? "";
        if (_typesSeen.TryGetValue(key, out var kept))
            return kept;

        return _typesSeen[key] = TypeLineage(type);
    }

    private static string[] TypeLineage(string? type)
    {
        var order = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();

        if (type is { Length: > 0 })
            queue.Enqueue(type.Trim());

        while (queue.Count > 0)
        {
            string code = queue.Dequeue();
            if (code.Length == 0 || !seen.Add(code))
                continue;

            order.Add(code);

            if (_parents.TryGetValue(code, out var above))
            {
                foreach (string parent in above)
                    queue.Enqueue(parent);
            }
        }

        return [.. order];
    }

    /// <summary>
    /// A type and everything it belongs to, itself included. <c>tkni</c> gives
    /// <c>tkni, comb, knif, weap</c> and so on up.
    /// </summary>
    public static IReadOnlyCollection<string> Ancestry(string? type)
    {
        // Kept a type, made again when the types are read again: asked for every item at every judge of what the
        // character can use, each time a new set and a closure.
        return Kept(_ancestries, type, Climbed);
    }

    private static System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyCollection<string>> _ancestries = new(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyCollection<string> Climbed(string? type)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Climb(string code)
        {
            if (code.Length == 0 || !found.Add(code) || !_parents.TryGetValue(code, out var above))
                return;

            foreach (string parent in above)
                Climb(parent);
        }

        if (type is { Length: > 0 })
            Climb(type.Trim());

        return found;
    }
}
