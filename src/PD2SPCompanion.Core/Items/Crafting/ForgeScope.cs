using System.Text.RegularExpressions;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// Which bases the Forge makes, and which of them exist only as the unique named on them. Read off the tables in use, rule by rule - each rule
/// below says what it was measured on in the Season 13 tables.
/// <list type="bullet">
/// <item><b>Made:</b> gear and what rolls affixes - maps among them, which the type table lets be normal,
/// magic or rare - and PD2's own loose materials that the materials tab does not keep.</item>
/// <item><b>Never:</b> a quest item, and a row the game has no use for (<see cref="IsLeftover"/>).</item>
/// <item><b>Only as its unique:</b> a base that does not spawn with a unique named on it - the three Rathma
/// bases - and a map that does not stack, which is a unique map (<see cref="OnlyNamed"/>).</item>
/// </list>
/// </summary>
public static partial class ForgeScope
{
    /// <summary>Bases an enabled unique or a set piece is made on.</summary>
    private static HashSet<string> _named = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every code a cube recipe names, as an input or as what comes out.</summary>
    private static HashSet<string> _cubed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>An older row of a base a newer row has replaced (<see cref="IsLeftover"/>).</summary>
    private static HashSet<string> _replaced = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The qualities the cube makes a type in, by the type an output names: <c>t4me,rar</c>.</summary>
    private static Dictionary<string, HashSet<string>> _cubedAs = new(StringComparer.OrdinalIgnoreCase);

    private static string? Cell(string? code, string column) =>
        code?.Trim() is { Length: > 0 } id ? D2SLib.Core.TXT.ItemsTXT.GetByCode(id)?.Get(column)?.Value?.Trim() : null;

    /// <summary>The tables' <c>spawnable</c>: whether a drop or a shop ever rolls this base by itself.</summary>
    public static bool Spawns(string? code) => Cell(code, "spawnable") == "1";

    private static bool Rolls(string code)
    {
        string? type = ItemTypes.TypeOf(code);
        return ItemTypes.IsGear(type) || ItemTypes.CanRollAffixes(type);
    }

    /// <summary>
    /// Whether the Forge makes this base at all.
    /// </summary>
    public static bool Makes(string code)
    {
        if (ItemBases.IsQuestItem(code))
            return false;

        return Rolls(code) ? !IsLeftover(code) : IsLooseMaterial(code);
    }

    /// <summary>
    /// One of PD2's own materials that lies loose - an uber or map material, an "uber unique" - and is not
    /// kept on the materials tab, which makes its own: uber and map materials and uber
    /// uniques can be made, but not those already on the tab.
    /// <para>
    /// Told from the old game's consumables by the type table: <b>a type that is only ever normal and
    /// descends from nothing</b>. PD2 gave each of its materials a type of its own (<c>ubr</c>,
    /// <c>ubru</c>, <c>lmal</c>, <c>pvpm</c>...), where a potion, a scroll, a key, a gem and a rune all
    /// descend from <c>misc</c>. In S13 that is 24 bases off the tab: the Pandemonium Talisman, Vision of
    /// Terror, Voidstone, Relic of the Ancients, Shadow of Hatred, Ancient Bone Fragments, the seven
    /// Corrupted Ears, the Cathedral of Light map, the two duel arenas, the Infused Demonic Cube, Larzuk's
    /// Malus, the Token of Absolution, and the five of type "UberUnique" - Lilith's Mirror, Vial of
    /// Lightsong, Horadric Almanac, Horadric Navigator, Skeleton Key.
    /// </para>
    /// </summary>
    public static bool IsLooseMaterial(string code)
    {
        if (ItemBases.IsQuestItem(code) || Rolls(code))
            return false;

        string? type = ItemTypes.TypeOf(code);
        if (!ItemTypes.QualityOf(type).Normal || !ItemTypes.IsRoot(type) || ItemTypes.QualitySaidBy(type) is null)
            return false;

        return Materials.SlotFor(code) is null && Materials.SlotFor(code.Trim() + "s") is null;
    }

    /// <summary>
    /// A row of gear, or of what rolls affixes, that the game has no use for. Three kinds, each found in
    /// the S13 tables by a list that showed the same name twice:
    /// <list type="bullet">
    /// <item><b>It does not spawn, nothing is named on it and no cube recipe knows it</b>: the "2H Phase
    /// Blade" (<c>7cr2</c>), the three PvP charms, the Mesa and Test maps, the Herb. Wirt's Leg does not
    /// spawn either, and stays - the cow portal's recipe takes it.</item>
    /// <item><b>An older row of a base a newer one replaced</b>: the six throwing potions of the old game
    /// (<c>opl</c>...<c>gps</c>, version 0) under the same names and type as PD2's own twelve (version 101).
    /// The treasure classes drop the new ones ("Misc 0": <c>tpfs</c>, <c>tpgs</c>, <c>tpcs</c>,
    /// <c>tpls</c>) and name none of the old.</item>
    /// <item><b>Its type says nothing of its quality and only the root of the tree answers for it</b>: the
    /// Elixir, with the Herb - the old game's unused potions, magic or rare by the word of <c>misc</c>
    /// alone. No treasure class drops them, no vendor sells them, and 2,001 cached market pages list
    /// none.</item>
    /// </list>
    /// </summary>
    public static bool IsLeftover(string code)
    {
        string id = code.Trim();
        if (_replaced.Contains(id))
            return true;

        if (!Spawns(id) && !_named.Contains(id) && !_cubed.Contains(id))
            return true;

        // Gear is never judged so: a belt's own type is silent too, and its parent answers for it.
        string? type = ItemTypes.TypeOf(id);
        return !ItemTypes.IsGear(type)
               && ItemTypes.QualitySaidBy(type) is { } said
               && !string.Equals(said, type, StringComparison.OrdinalIgnoreCase)
               && ItemTypes.IsRoot(said);
    }

    /// <summary>
    /// A base that exists only as the unique named on it, so the Forge offers it in no other rarity and no
    /// runeword is made in it:
    /// <list type="bullet">
    /// <item><b>gear that does not spawn</b> with a unique on it - "Boneweave [S]" (Cage of the
    /// Unsullied, six sockets), "Troll Belt [S]" (Band of Skulls, one socket) and "Amulet [S]" (The Third
    /// Eye), Rathma's three;</item>
    /// <item><b>a map that does not stack</b>: the plain maps stack to 50 and the eight unique maps do
    /// not, and the cube makes those as <c>t51,uni</c>.</item>
    /// </list>
    /// </summary>
    public static bool OnlyNamed(string code)
    {
        string id = code.Trim();
        if (!_named.Contains(id) || !Rolls(id) || ItemBases.IsQuestItem(id))
            return false;

        return !Spawns(id) || (ItemOrder.IsMap(id) && !Stacks.IsStackable(id));
    }

    /// <summary>
    /// The only qualities a map exists in, where the cube says so; null where it does not narrow them.
    /// <para>
    /// <b>A dungeon map is rare and nothing else.</b> No treasure class drops one - "Map Tier 1" to "Map
    /// Tier 3" list the 26 plain maps and none of the three dungeons - and the cube makes one only as
    /// <c>t4me,rar</c>: a tier-3 map with a Horadrim Scarab, or three dungeons for a new one. Nothing
    /// scours it back to white, where the first three tiers have the Orb of Destruction's recipes. Told
    /// from the cube alone: the first three tiers come out of it normal, magic and rare, so the qualities
    /// it is seen to make a map's type in are every one that type has.
    /// </para>
    /// </summary>
    public static IReadOnlyCollection<D2SLib.Model.Save.ItemQuality>? MapQualities(string code)
    {
        if (!ItemOrder.IsMap(code.Trim()))
            return null;

        var made = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? type in new[] { Cell(code, "type"), Cell(code, "type2") })
        {
            if (type is { Length: > 0 } && _cubedAs.TryGetValue(type, out var qualities))
                made.UnionWith(qualities);
        }

        if (made.Count == 0 || made.Contains("nor"))
            return null;

        var only = new List<D2SLib.Model.Save.ItemQuality>();
        if (made.Contains("mag"))
            only.Add(D2SLib.Model.Save.ItemQuality.Magic);
        if (made.Contains("rar"))
            only.Add(D2SLib.Model.Save.ItemQuality.Rare);
        return only;
    }

    [GeneratedRegex(@"[\[\(]\s*([^\]\)]+?)\s*[\]\)]\s*$")]
    private static partial Regex Bracketed();

    /// <summary>
    /// The mark the table's own name gives a base that shares its name with another - "Boneweave [S]" is
    /// drawn "Boneweave" by the game, as the Boneweave is - or nothing. Only for a base that exists as its
    /// unique alone: the old gloves are "Gloves(L)" in the table and need no telling apart.
    /// </summary>
    public static string Mark(string code) =>
        OnlyNamed(code) && Cell(code, "name") is { } name && Bracketed().Match(name) is { Success: true } found
            ? found.Groups[1].Value
            : "";

    /// <summary>A base's name as the Forge lists it: the game's, with its mark where it has one - "Boneweave (S)".</summary>
    public static string Label(string code)
    {
        string name = ItemPresenter.BaseName(code.Trim());
        return Mark(code) is { Length: > 0 } mark ? $"{name} ({mark})" : name;
    }

    public static void Load(GameData game)
    {
        var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cubed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var replaced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cubedAs = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var row in D2SLib.Core.TXT.ItemsTXT.UniqueTXT?.Rows ?? [])
            {
                if (row.Get("enabled")?.Value?.Trim() == "1" && row.Get("code")?.Value?.Trim() is { Length: > 0 } code)
                    named.Add(code);
            }

            foreach (var row in D2SLib.Core.TXT.ItemsTXT.SetItemsTXT?.Rows ?? [])
            {
                if (row.Get("item")?.Value?.Trim() is { Length: > 0 } code)
                    named.Add(code);
            }

            if (game.Lines("cubemain") is { Length: > 0 } lines)
            {
                string[] header = lines[0].Split('\t');
                int[] cells = [.. Enumerable.Range(0, header.Length)
                    .Where(at => header[at].StartsWith("input ", StringComparison.OrdinalIgnoreCase)
                                 || header[at].StartsWith("output", StringComparison.OrdinalIgnoreCase))];
                int live = Array.IndexOf(header, "enabled");

                foreach (string line in lines.Skip(1))
                {
                    string[] row = line.Split('\t');
                    foreach (int at in cells.Where(at => at < row.Length))
                    {
                        // "t51,uni", "gg4r,qty=1", "leg": the code is what stands before the first comma.
                        string[] parts = row[at].Trim().Trim('"').Split(',');
                        string token = parts[0].Trim();
                        if (token.Length == 0)
                            continue;

                        cubed.Add(token);

                        // What comes out, and in which quality - of a recipe that is switched on.
                        if (!header[at].StartsWith("output", StringComparison.OrdinalIgnoreCase)
                            || (live >= 0 && live < row.Length && row[live].Trim() != "1"))
                        {
                            continue;
                        }

                        foreach (string quality in parts.Skip(1).Select(part => part.Trim().ToLowerInvariant())
                                     .Where(part => part is "nor" or "mag" or "rar"))
                        {
                            if (!cubedAs.TryGetValue(token, out var qualities))
                                cubedAs[token] = qualities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            qualities.Add(quality);
                        }
                    }
                }
            }

            // Two rows drawn under one name, of one type: the one of the older version gives way. Not where
            // either exists only as its unique - "Boneweave [S]" wears the Boneweave's name on purpose.
            var twins = ItemBases.Codes
                .Where(code => !ItemBases.IsQuestItem(code))
                .Select(code => (Code: code, Name: Cell(code, "namestr") ?? "", Type: ItemTypes.TypeOf(code) ?? "",
                                 Version: int.TryParse(Cell(code, "version"), out int version) ? version : 0))
                .Where(each => each.Name.Length > 0 && each.Type.Length > 0)
                .GroupBy(each => (each.Name.ToLowerInvariant(), each.Type.ToLowerInvariant()))
                .Where(group => group.Count() > 1);

            foreach (var group in twins)
            {
                int newest = group.Max(each => each.Version);
                foreach (var each in group.Where(each => each.Version < newest && !named.Contains(each.Code)))
                    replaced.Add(each.Code);
            }
        }
        catch (Exception problem)
        {
            // With nothing read, every base is judged by its type alone, as before this class.
            Log.Caught(problem);
        }

        _named = named;
        _cubed = cubed;
        _replaced = replaced;
        _cubedAs = cubedAs;
    }
}
