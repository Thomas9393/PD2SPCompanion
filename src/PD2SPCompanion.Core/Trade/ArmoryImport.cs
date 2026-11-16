using System.Text.Json;

using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Trade;

/// <summary>
/// A character from the PD2 armory (<c>projectdiablo2.com/character/Name</c>) made into a save of
/// this editor's own.
/// <para>
/// The armory gives the character's name, class, level, experience, attributes, skills, gold, its
/// mercenary, and its items - equipped, inventory and the mercenary's - in the trade market's item
/// shape, which <see cref="TradeItems.Build"/> already rebuilds (39 of 39 whole on a level 99 Barbarian). It
/// gives no stash, cube, belt, quests or waypoints. The save is built on a template - another save,
/// whose unmodelled parts it keeps - with everything that is the character's replaced.
/// </para>
/// </summary>
public static class ArmoryImport
{
    /// <summary>The armory's answer, kept whole; its parts read as asked.</summary>
    public sealed record Armory(JsonElement Root)
    {
        private JsonElement Character => Root.GetProperty("character");

        public string Name => Character.GetProperty("name").GetString() ?? "";

        public byte ClassId => (byte)Character.GetProperty("class").GetProperty("id").GetInt32();

        public string ClassName => Character.GetProperty("class").GetProperty("name").GetString() ?? "";

        public int Level => Character.GetProperty("level").GetInt32();

        /// <summary>A uint32 the site prints signed: -774482041 is 3,520,485,255.</summary>
        public uint Experience => unchecked((uint)Character.GetProperty("experience").GetInt64());

        public bool IsHardcore => Status("is_hardcore");

        public bool IsExpansion => Status("is_expansion");

        /// <summary>A hardcore character that has died, as the armory says.</summary>
        public bool IsDead => Status("is_dead");

        /// <summary>When the server last wrote the character's file - the nearest the armory has to "played".</summary>
        public DateTime? UpdatedAt =>
            Root.TryGetProperty("file", out var file) && file.TryGetProperty("updated_at", out var at) && at.TryGetInt64(out long seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime
                : null;

        private bool Status(string name) =>
            Character.TryGetProperty("status", out var status) && status.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.True;

        public int Attribute(string name) => Character.GetProperty("attributes").GetProperty(name).GetInt32();

        public int StatPoints => Points("stat");

        public int SkillPoints => Points("skill");

        private int Points(string name) =>
            Character.TryGetProperty("points", out var points) && points.TryGetProperty(name, out var value) ? value.GetInt32() : 0;

        public int Gold => GoldOf("character");

        public int GoldInStash => GoldOf("stash");

        private int GoldOf(string name) =>
            Character.TryGetProperty("gold", out var gold) && gold.TryGetProperty(name, out var value) ? value.GetInt32() : 0;

        /// <summary>Each skill by its id, with the points in it.</summary>
        public IReadOnlyDictionary<int, int> Skills =>
            Character.GetProperty("skills").EnumerateArray()
                     .ToDictionary(skill => skill.GetProperty("id").GetInt32(), skill => skill.GetProperty("level").GetInt32());

        public IReadOnlyList<JsonElement> Items =>
            Root.TryGetProperty("items", out var items) ? [.. items.EnumerateArray()] : [];

        public JsonElement? Mercenary =>
            Root.TryGetProperty("mercenary", out var mercenary) && mercenary.ValueKind == JsonValueKind.Object ? mercenary : null;

        public IReadOnlyList<JsonElement> MercenaryItems =>
            Mercenary is { } mercenary && mercenary.TryGetProperty("items", out var items) ? [.. items.EnumerateArray()] : [];
    }

    public sealed class ArmoryException(string message, Exception? inner = null) : Exception(message, inner);

    /// <summary>
    /// What was typed, sorted: a character's name, a link to its armory page, or neither. A link is
    /// recognised by its shape - a scheme, a slash or a dot - and must be the PD2 site's; anything
    /// else is a <c>Problem</c>, said before anything is fetched.
    /// </summary>
    public static (string? Name, bool IsLink, string? Problem) Recognise(string? typed)
    {
        string text = (typed ?? "").Trim();
        if (text.Length == 0)
            return (null, false, null);

        bool link = text.Contains("://") || text.Contains('/') || text.Contains('.');
        if (!link)
            return NameIn(text) is { } bare ? (bare, false, null) : (null, false, L("Not a character name."));

        string address = text.Contains("://") ? text : "https://" + text;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return (null, true, L("Not a valid link."));

        string host = uri.Host.ToLowerInvariant();
        if (host != "projectdiablo2.com" && !host.EndsWith(".projectdiablo2.com", StringComparison.Ordinal))
            return (null, true, L("Not a PD2 armory link."));

        return NameIn(uri.AbsolutePath) is { } name && uri.AbsolutePath.Contains("/character/", StringComparison.OrdinalIgnoreCase)
            ? (name, true, null)
            : (null, true, L("No character in that link."));
    }

    /// <summary>The character's name out of what was typed: the name itself, or a link to its armory page.</summary>
    public static string? NameIn(string? typed)
    {
        string text = (typed ?? "").Trim().TrimEnd('/');
        int at = text.LastIndexOf("/character/", StringComparison.OrdinalIgnoreCase);
        if (at >= 0)
            text = text[(at + "/character/".Length)..];

        int query = text.IndexOfAny(['?', '#']);
        if (query >= 0)
            text = text[..query];

        return text.Length is >= 2 and <= 15 && text.All(each => char.IsAsciiLetter(each) || each is '-' or '_') ? text : null;
    }

    /// <summary>The armory's record of a character, through the site's gate as every request is.</summary>
    public static async Task<Armory> Fetch(string name, CancellationToken cancel = default)
    {
        string body;
        try
        {
            body = await TradeGate.Get($"https://api.projectdiablo2.com/game/character/{Uri.EscapeDataString(name)}", keep: false, cancel,
                                       missing: answer => answer.Contains("Failed to parse character", StringComparison.OrdinalIgnoreCase));
        }
        catch (TradeGate.TradeGateException refused)
        {
            // The gate's words are the trade site's; this is the armory.
            throw new ArmoryException(refused.IsMissing ? L($"{name} not found in the armory.")
                                      : refused.InnerException is not null ? L("Armory not reachable.")
                                      : refused.Message.Replace("trade site", "PD2 site", StringComparison.OrdinalIgnoreCase),
                                      refused);
        }

        return ArmoryIn(body, name);
    }

    /// <summary>The armory's answer as a character. The answer is the network's: one of any other shape is refused in words.</summary>
    /// <exception cref="ArmoryException">It is no record of a character.</exception>
    public static Armory ArmoryIn(string body, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement.Clone();
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("character", out var character) || character.ValueKind != JsonValueKind.Object)
                throw new ArmoryException(L($"{name} not found in the armory."));
            return new Armory(root);
        }
        catch (JsonException exception)
        {
            throw new ArmoryException(L("Armory answer unreadable."), exception);
        }
    }

    /// <summary>
    /// Life, mana and stamina as the save stores them - before gear - for this character: what
    /// <see cref="Build"/> writes, and what its card shows. The armory's own figures are the
    /// character's current ones (a life of 1 on a level 99 Barbarian), not these. +20 life for each Golden
    /// Bird: the import writes every quest done and every reward taken.
    /// </summary>
    public static (int Life, int Mana, int Stamina) BaseVitals(GameData game, Armory armory) =>
        NewCharacter.Vitals(game, armory.ClassId, Math.Clamp(armory.Level, 1, 99), armory.Attribute("vitality"), armory.Attribute("energy"),
                            NewCharacter.QuestGifts.All.Life);

    /// <summary>
    /// The save for this armory character, built on <paramref name="template"/> (read afresh from
    /// disk, so nothing of the open character leaks in) and named <paramref name="name"/>.
    /// Returns the bytes, verified to read back, and what was left out.
    /// </summary>
    public static (byte[] Save, IReadOnlyList<string> Notes) Build(GameData game, string templatePath, Armory armory, string name)
    {
        var notes = new List<string>();
        var (save, _) = SaveVerifier.Load(templatePath);
        string era = NewCharacter.ItemEra(save);

        // ---- the header: the armory's, and a new character's state for the rest -----------------
        // Every quest done and every reward taken: the armory's attributes and skills already hold
        // what the player took.
        NewCharacter.Begin(save, name, armory.ClassId, armory.Level, armory.IsHardcore, armory.IsExpansion, finished: true);

        // ---- attributes ---------------------------------------------------------------------------
        int vitality = armory.Attribute("vitality"), energy = armory.Attribute("energy");

        var (life, mana, stamina) = BaseVitals(game, armory);

        var values = new Dictionary<string, long>
        {
            ["strength"] = armory.Attribute("strength"),
            ["energy"] = energy,
            ["dexterity"] = armory.Attribute("dexterity"),
            ["vitality"] = vitality,
            ["statpts"] = armory.StatPoints,
            ["newskills"] = armory.SkillPoints,
        };
        foreach (var (stat, value) in NewCharacter.Scaled(life, mana, stamina))
            values[stat] = value;
        values["level"] = save.Level;
        values["experience"] = armory.Experience;
        values["gold"] = armory.Gold;
        values["goldbank"] = armory.GoldInStash;
        NewCharacter.SetAttributes(save, values);

        // ---- skills, by id: the save's slots are the class's, in SkillTree's order ----------------
        var tree = SkillTree.For(game, armory.ClassId);
        foreach (var (id, points) in armory.Skills)
        {
            if (points <= 0)
                continue;
            if (tree.BySlot.Values.FirstOrDefault(entry => entry.Id == id) is { } entry && entry.Slot < save.ClassSkills.Skills.Count)
                save.ClassSkills.Skills[entry.Slot].Points = (byte)Math.Clamp(points, 0, 255);
            else
                notes.Add(L($"skill {id} has no slot in this season's tree"));
        }

        // ---- items --------------------------------------------------------------------------------
        var list = save.PlayerItemList;
        foreach (var json in armory.Items)
        {
            if (Place(json, era, notes) is { } item)
                list.Items.Add(item);
        }
        list.Count = (ushort)list.Items.Count;

        // ---- the mercenary ------------------------------------------------------------------------
        if (armory.Mercenary is { } mercenary)
        {
            save.Mercenary.IsDead = 0;
            save.Mercenary.Id = unchecked((uint)mercenary.GetProperty("id").GetInt64());
            save.Mercenary.NameId = (ushort)mercenary.GetProperty("name_id").GetInt32();
            save.Mercenary.TypeId = (ushort)mercenary.GetProperty("type").GetInt32();
            save.Mercenary.Experience = unchecked((uint)mercenary.GetProperty("experience").GetInt64());

            // A save holds the list of what a mercenary wears only where there is a mercenary, so a template
            // that never hired one has none - and the built-in template is such a one. Written without it, a
            // character with a mercenary could not be written at all (building ladder
            // characters for the character sheet, every one with a mercenary failed).
            if (save.Mercenary.Id != 0)
            {
                save.MercenaryItemList ??= new MercenaryItemList();
                save.MercenaryItemList.ItemList ??= new ItemList();
            }
        }

        if (save.MercenaryItemList?.ItemList is { } hired)
        {
            foreach (var json in armory.MercenaryItems)
            {
                if (Place(json, era, notes) is { } item)
                    hired.Items.Add(item);
            }
            hired.Count = (ushort)hired.Items.Count;
        }
        else if (armory.MercenaryItems.Count > 0)
        {
            notes.Add(L("the template save has no mercenary item list; the mercenary's items were left out"));
        }

        return (SaveWriter.Prepared(save), notes);
    }

    /// <summary>
    /// One armory item, rebuilt and put where the armory says: worn in its body slot - the armory's
    /// <c>equipment_id</c> is the save's own numbering, Head 1 to the weapon swap's 12 - or at its
    /// inventory cell.
    /// </summary>
    private static Item? Place(JsonElement json, string era, List<string> notes)
    {
        Item item;
        try
        {
            var forged = TradeItems.Build(json);

            // A unique no table row names (Kadala's Heirloom, built by PD2's code); written, it
            // would be another unique.
            if (TradeItems.NoRow(forged) is { } missing)
            {
                notes.Add(L($"left out, {missing}"));
                return null;
            }

            notes.AddRange(forged.Notes.Where(note => note.StartsWith(TradeItems.TakenByBase, StringComparison.Ordinal)));
            item = forged.Item;
        }
        catch (Exception problem)
        {
            notes.Add(L($"an item could not be rebuilt: {problem.Message}"));
            return null;
        }

        Stamp(item, era);

        var location = json.GetProperty("location");
        bool worn = location.TryGetProperty("zone", out var zone) && zone.GetString() == "Equipped";
        if (worn)
        {
            int slot = location.GetProperty("equipment_id").GetInt32();
            item.Mode = ItemMode.Equipped;
            item.Location = (ItemLocation)slot;
            item.Page = 0;
            item.X = (byte)slot;
            item.Y = 0;
        }
        else
        {
            var position = json.GetProperty("position");
            ItemMover.Place(item, InventoryLayout.InventoryPage, position.GetProperty("column").GetInt32(), position.GetProperty("row").GetInt32());
        }

        return item;
    }

    /// <summary>The save's item era on the item and everything in its sockets: a save holds one.</summary>
    private static void Stamp(Item item, string era)
    {
        item.Version = era;
        foreach (var inside in item.SocketedItems)
            Stamp(inside, era);
    }

}
