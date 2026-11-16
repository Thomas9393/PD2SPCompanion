using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Graphics;

/// <summary>
/// What a character looks like in its gear: the class's two-letter token, the pose, the weapon class,
/// the art each body part is drawn with (LIT, MED, HVY or an item's own code), its tint, and the parts an
/// ethereal item dresses.
/// </summary>
public sealed record FigureLook(
    string Token, string Mode, string WeaponClass,
    IReadOnlyDictionary<string, string> Parts,
    IReadOnlyDictionary<string, Tint> Tints)
{
    /// <summary>The parts drawn see-through: those an ethereal weapon dresses.</summary>
    public IReadOnlySet<string> Ethereal { get; init; } = new HashSet<string>();
}

/// <summary>One frame of a figure on a canvas every frame shares, so the figure stands still while it moves.</summary>
public sealed record FigureFrame(uint[] Bgra);

/// <summary>
/// A composed animation: frames of one size, the anchor (the feet) inside them, how fast it plays, and
/// the parts it could not find art for. <see cref="BodyTop"/> and <see cref="BodyBottom"/> are the
/// body's rows against the anchor, weapons and shield left out: a page sizes the character by them, so a
/// spear reaches past its frame, as in the game, rather than shrinking the one who holds it.
/// </summary>
public sealed record Figure(int Width, int Height, int AnchorX, int AnchorY, double FramesPerSecond,
                            IReadOnlyList<FigureFrame> Frames, IReadOnlyList<string> Missing)
{
    public int BodyTop { get; init; }
    public int BodyBottom { get; init; }
}

/// <summary>
/// The character drawn the way the select screen draws it, from <c>d2char.mpq</c>: a COF names the
/// pose's layers and their order per frame, and each layer is a DCC file named by the part worn there.
/// <para>
/// Drawn from the worn items, so an edit shows before the game saves: each part's art is the base's
/// <c>alternateGfx</c> (a Matriarchal Spear is drawn as AM3) or the body armour's weight column
/// (<c>Torso</c>, <c>Legs</c>, <c>rArm</c>, <c>lArm</c>, <c>rSPad</c>, <c>lSPad</c>: LIT, MED, HVY), its
/// colour the item's own (<c>ItemTints.Of</c>, worn), the pose's weapon class the hands'. Only what the
/// character can use is drawn.
/// </para>
/// <para>
/// The save's own sixteen appearance bytes agree with it on five game-written saves: an armour slot's graphic is its weight plus one, a tint the base's
/// <c>Transform</c> (three bits) times 32 plus the colour row plus one. The head, weapon and shield
/// graphics are positions in a list the engine builds - not needed here, and not fully known.
/// </para>
/// </summary>
public static class CharacterFigure
{
    /// <summary>Each class's folder under <c>data\global\chars</c>, by class id.</summary>
    private static readonly string[] Tokens = ["AM", "SO", "NE", "PA", "BA", "DZ", "AI"];

    /// <summary>The sixteen composit slots, in the order the save and <c>composit.txt</c> keep them.</summary>
    public static readonly string[] Parts = ["HD", "TR", "LG", "RA", "LA", "RH", "LH", "SH", "S1", "S2", "S3", "S4", "S5", "S6", "S7", "S8"];

    private static readonly string[] Weights = ["LIT", "MED", "HVY"];

    /// <summary>The pose the select screen shows: standing in town.</summary>
    public const string TownNeutral = "TN";

    /// <summary>
    /// A class with nothing on, as a freshly made character stands: every body part light, bare-handed. For a
    /// card no save stands behind - an armory card, a save that will not read - since the editor ships no
    /// class animation of its own (it ships no game art).
    /// </summary>
    public static FigureLook BareLookOf(int classId, string mode = TownNeutral)
    {
        var parts = Body.ToDictionary(entry => entry.Part, _ => "LIT");
        parts["HD"] = "LIT";
        return new FigureLook(classId >= 0 && classId < Tokens.Length ? Tokens[classId] : Tokens[0],
                              mode, "HTH", parts, new Dictionary<string, Tint>());
    }

    /// <summary>
    /// What a save's character wears, as the parts and tints to draw it with - from the worn items, so
    /// an edit shows before the game has saved. Checked against the appearance bytes the game wrote
    /// (PhySpear, FigureTest): the colours agree item for item.
    /// </summary>
    public static FigureLook LookOf(D2S save, string mode = TownNeutral)
    {
        string token = save.ClassId < Tokens.Length ? Tokens[save.ClassId] : Tokens[0];
        var worn = Usable(save);
        var looks = typeof(Appearances).GetProperties()
            .Select(property => (Appearance)property.GetValue(save.Appearances)!)
            .ToList();

        var parts = new Dictionary<string, string>();
        var tints = new Dictionary<string, Tint>();
        var ethereal = new HashSet<string>();
        for (int i = 0; i < Parts.Length && i < looks.Count; i++)
        {
            var look = looks[i];
            // The weight of a part no item below speaks for (S3-S8); colours all come from the items.
            if (look.Graphic is >= 1 and <= 3)
                parts[Parts[i]] = Weights[look.Graphic - 1];
        }

        // The body armour dresses the whole body: each part's weight is its column (0 LIT, 1 MED,
        // 2 HVY), and its colour goes on every one of them - the game wrote PhySpear's torso tint on
        // her legs and arms too. Without body armour the body is bare and untinted.
        worn.TryGetValue(ItemLocation.Torso, out var armour);
        foreach (var (part, column) in Body)
        {
            parts[part] = armour is not null && int.TryParse(Column(BaseRow(armour), column), out int weight) && weight is >= 0 and <= 2
                ? Weights[weight]
                : "LIT";
            Dye(part, armour);
        }

        // A circlet is no head part: its art is the bare head (alternategfx lit, the circlets' alone), and the game
        // writes no part and no tint for it - graphic and tint 255 in ShockSacri, LightFury and SkeleMerc. Its
        // colour on the bare head dyed the skin and hair, where the circlet family shows nothing in game.
        parts["HD"] = worn.TryGetValue(ItemLocation.Head, out var helm) ? Art(helm) : "LIT";
        Dye("HD", parts["HD"] == "LIT" ? null : helm);

        // The hands: the main slots' two (Usable), as the engine reads them (D2Common's COMPOSIT_GetWeaponClassCode,
        // INVENTORY_GetCompositItem, INVENTORY_GetWieldType - read in D2MOO).
        //   - The primary weapon is the one put on first, whichever slot each is in (seen in game:
        //     the same figure with the pair either way round in the slots) - and so the first of
        //     the two in the save, which the game loads in order. The editor keeps that order as items are put
        //     on (CharacterSession.Wear).
        //   - A Barbarian with a weapon in each hand: the pose by the pair (DualClass), the primary drawn in
        //     the right hand's part, the other in the left's. An Assassin with two: HT2, drawn the same way.
        //   - Else the weapon's own class - or its two-handed one where it is held in both hands: alone and
        //     two-handed (a Barbarian's two-handed sword too, with his other hand empty), or two-handed beside
        //     what it shares its hands with (a bow and its quiver).
        worn.TryGetValue(ItemLocation.RightHand, out var right);
        worn.TryGetValue(ItemLocation.LeftHand, out var left);
        var weapons = save.PlayerItemList.Items
            .Where(item => (ReferenceEquals(item, right) || ReferenceEquals(item, left)) && IsWeapon(item))
            .ToList();
        var shield = new[] { right, left }.FirstOrDefault(item => item is not null && !IsWeapon(item));
        parts.Remove("RH");
        parts.Remove("LH");
        parts.Remove("SH");

        const int Barbarian = 4, Assassin = 6;
        string weaponClass = "HTH";
        if (weapons.Count > 0)
        {
            var primary = weapons[0];
            var row = BaseRow(primary);
            weaponClass = (Column(row, "wclass") ?? "hth").ToUpperInvariant();

            bool OneOrTwo(Item item) => save.ClassId == Barbarian && Column(BaseRow(item), "1or2handed") == "1";
            bool TwoHanded(Item item) => Column(BaseRow(item), "2handed") == "1";

            if (weapons.Count == 2 && save.ClassId == Barbarian)
            {
                weaponClass = DualClass(primary, weapons[1]);
            }
            else if (weapons.Count == 2 && save.ClassId == Assassin)
            {
                weaponClass = "HT2";
            }
            else
            {
                bool alone = right is null || left is null;
                bool both = alone
                    ? TwoHanded(primary) || OneOrTwo(primary)
                    : !new[] { right!, left! }.Any(OneOrTwo) && new[] { right!, left! }.Any(TwoHanded);
                if (both && Column(row, "2handedwclass") is { } twoHanded)
                    weaponClass = twoHanded.ToUpperInvariant();
            }

            // A bow is held in the left hand; a crossbow, like everything else, in the right.
            string hand = weaponClass == "BOW" ? "LH" : "RH";
            parts[hand] = Art(primary);
            Dye(hand, primary);
            if (weapons.Count == 2)
            {
                parts["LH"] = Art(weapons[1]);
                Dye("LH", weapons[1]);
            }
        }
        if (shield is not null)
        {
            parts["SH"] = Art(shield);
            Dye("SH", shield);
        }

        return new FigureLook(token, mode, weaponClass, parts, tints) { Ethereal = ethereal };

        // A part's colour from the item worn there, worked out as the game works it out (ItemTints, worn:
        // a unique's or set piece's chrtransform, a magic or rare item's first coloured suffix) - so an
        // item put on in the editor shows its colour before the game has saved. No item, or one without
        // a colour, draws the part plain.
        void Dye(string part, Item? item)
        {
            if (item is not null && Items.ItemTints.Of(item, BaseRow(item), Items.ItemPresenter.QualityRow(item), worn: true) is { } tint)
                tints[part] = tint;
            else
                tints.Remove(part);
            // Only a weapon, as the game draws it in the world: its hands' parts.
            if (item is { IsEthereal: true } && part is "RH" or "LH")
                ethereal.Add(part);
            else
                ethereal.Remove(part);
        }
    }

    /// <summary>
    /// How solid an ethereal weapon's layers are drawn: 60%, see-through by 40%. In
    /// the world the game draws only an ethereal weapon see-through - armour, helm and shield stay solid -
    /// and the figure follows it; the select screen draws all of it solid.
    /// </summary>
    public const byte EtherealAlpha = 153;

    /// <summary>A colour laid over another at <paramref name="alpha"/> - straight alpha, as the frames are.</summary>
    private static uint Over(uint colour, byte alpha, uint under)
    {
        uint underAlpha = under >> 24;
        if (underAlpha == 0)
            return (colour & 0x00FFFFFF) | ((uint)alpha << 24);
        // out = a + b(1 - a), each channel weighted by its own alpha.
        double a = alpha / 255.0, b = underAlpha / 255.0, outAlpha = a + b * (1 - a);
        uint Channel(int shift) => (uint)Math.Round(((colour >> shift & 0xFF) * a + (under >> shift & 0xFF) * b * (1 - a)) / outAlpha);
        return (uint)Math.Round(outAlpha * 255) << 24 | Channel(16) << 16 | Channel(8) << 8 | Channel(0);
    }

    /// <summary>The parts body armour dresses, and the column of its row that names each one's weight.</summary>
    private static readonly (string Part, string Column)[] Body =
        [("TR", "Torso"), ("LG", "Legs"), ("RA", "rArm"), ("LA", "lArm"), ("S1", "rSPad"), ("S2", "lSPad")];

    /// <summary>
    /// What the character wears that it can use, by slot - only that is drawn: not
    /// an item whose requirement is not met, judged as the item tiles judge it (Requirements.Judge: the
    /// gear helping, the unmet giving nothing), nor another class's item the editor let on. The hands are
    /// the main slots' - what is in hand whichever tab the game shows it under (<see cref="Characters.WeaponSets"/>);
    /// the swap slots' pair is never drawn.
    /// </summary>
    private static Dictionary<ItemLocation, Item> Usable(D2S save)
    {
        string? classCode = GameFiles.CharacterClasses.CodeOf(save.ClassId);
        int strength = Saves.AttributeScale.Display("strength", save.Attributes.Stats.GetValueOrDefault("strength"));
        int dexterity = Saves.AttributeScale.Display("dexterity", save.Attributes.Stats.GetValueOrDefault("dexterity"));
        var met = new HashSet<Item>(
            Characters.Requirements.Judge(save, Characters.WeaponSets.InHand, strength, dexterity).Where(judged => judged.Met).Select(judged => judged.Item),
            ReferenceEqualityComparer.Instance);

        var usable = new Dictionary<ItemLocation, Item>();
        foreach (var item in save.PlayerItemList.Items.Where(item => item.Mode == ItemMode.Equipped))
        {
            var at = item.Location is ItemLocation.SwapRight or ItemLocation.SwapLeft ? ItemLocation.None : item.Location;
            if (at == ItemLocation.None || !met.Contains(item))
                continue;
            if (Items.ItemTypes.ClassOfBase(item.Code) is { Length: > 0 } only
                && !string.Equals(only, classCode, StringComparison.OrdinalIgnoreCase))
                continue;
            usable.TryAdd(at, item);
        }
        return usable;
    }

    /// <summary>
    /// A Barbarian's pose with a weapon in each hand, as the engine picks it (COMPOSIT_GetWeaponClassCode): the
    /// name's last letter is the primary weapon's stroke - S swung (1HS), T thrust (1HT) - and the one before
    /// it the other's, S swung or J jabbed. 1SS, 1JS ("left jab, right swing"), 1JT, 1ST; anything else, 1SS.
    /// Not the other way round, the primary's letter first.
    /// </summary>
    private static string DualClass(Item primary, Item secondary)
    {
        string a = (Column(BaseRow(primary), "wclass") ?? "").ToUpperInvariant();
        string b = (Column(BaseRow(secondary), "wclass") ?? "").ToUpperInvariant();
        return (a, b) switch
        {
            ("1HS", "1HT") => "1JS",
            ("1HT", "1HT") => "1JT",
            ("1HT", "1HS") => "1ST",
            _ => "1SS",
        };
    }

    private static D2SLib.Model.TXT.TXTRow? BaseRow(Item item) => D2SLib.Core.TXT.ItemsTXT.GetByCode(item.Code);

    private static string? Column(D2SLib.Model.TXT.TXTRow? row, string column) =>
        row?.Get(column)?.Value?.Trim() is { Length: > 0 } value ? value : null;

    private static bool IsWeapon(Item item) => Column(BaseRow(item), "wclass") is not null;

    /// <summary>The art an item is drawn with: its base's <c>alternateGfx</c>, else its own code.</summary>
    private static string Art(Item item) =>
        (Column(BaseRow(item), "alternateGfx") ?? Column(BaseRow(item), "alternategfx") ?? item.Code.Trim()).ToUpperInvariant();

    /// <summary>
    /// Where a direction's pictures sit in a DCC file. A COF numbers directions the way the game turns -
    /// 0 facing the viewer - and keeps its drawing order by that number; a DCC stores them in another
    /// order, the 8 main directions first. The tables are OpenDiablo2's (dccDir8/16/32). Taken as one
    /// numbering, the pictures facing the viewer (file 4) were drawn in direction 4's order, which puts
    /// the right hand behind the body: FigureTest's sword hid behind its leg.
    /// </summary>
    private static int FileDirection(int direction, int count) => count switch
    {
        8 => Dir8[direction],
        16 => Dir16[direction],
        32 => Dir32[direction],
        _ => direction,
    };

    private static readonly int[] Dir8 = [4, 0, 5, 1, 6, 2, 7, 3];
    private static readonly int[] Dir16 = [4, 8, 0, 9, 5, 10, 1, 11, 6, 12, 2, 13, 7, 14, 3, 15];
    private static readonly int[] Dir32 =
        [4, 16, 8, 17, 0, 18, 9, 19, 5, 20, 10, 21, 1, 22, 11, 23, 6, 24, 12, 25, 2, 26, 13, 27, 7, 28, 14, 29, 3, 30, 15, 31];

    /// <summary>
    /// The look put together for one direction: every frame of the pose, its layers drawn back to front
    /// in the COF's order, each through its tint. Null where the class has no COF for the pose.
    /// </summary>
    public static Figure? Compose(GameData game, Palette palette, FigureLook look, int direction) =>
        Compose(game, palette, look, direction, null);

    internal static Figure? Compose(GameData game, Palette palette, FigureLook look, int direction,
                                    System.Collections.Concurrent.ConcurrentDictionary<(string, int), IReadOnlyList<DccFrame>>? decoded)
    {
        var cofFile = game.Read(Cof.PathFor(look.Token, look.Mode, look.WeaponClass))
                      ?? game.Read(Cof.PathFor(look.Token, look.Mode, "HTH"));
        if (cofFile is null)
            return null;
        var cof = Cof.Read(cofFile.Value.Data);
        direction = ((direction % cof.Directions) + cof.Directions) % cof.Directions;
        int fileDirection = FileDirection(direction, cof.Directions);

        var layers = new Dictionary<int, (IReadOnlyList<DccFrame> Frames, byte[]? Remap)>();
        var seeThrough = new HashSet<int>(cof.Layers.Select(layer => layer.Component)
                                                    .Where(component => look.Ethereal.Contains(Parts[component])));
        var missing = new List<string>();
        foreach (var layer in cof.Layers)
        {
            string part = Parts[layer.Component];
            string art = look.Parts.GetValueOrDefault(part, "LIT");
            string path = $@"data\global\chars\{look.Token}\{part}\{look.Token}{part}{art}{look.Mode}{layer.WeaponClass}.dcc";
            try
            {
                byte[]? remap = look.Tints.TryGetValue(part, out var tint) ? ColourMaps.For(tint) : null;
                if (decoded is not null && decoded.TryGetValue((path, fileDirection), out var kept))
                {
                    layers[layer.Component] = (kept, remap);
                    continue;
                }
                if (game.Read(path) is not { } dcc)
                {
                    // The bare shield slot has no art of its own; anything else missing is worth saying.
                    if (art != "LIT" || part is not ("SH" or "RH" or "LH"))
                        missing.Add(Path.GetFileName(path));
                    continue;
                }
                int directions = Dcc.Directions(dcc.Data);
                var read = Dcc.Read(dcc.Data, directions == cof.Directions ? fileDirection : FileDirection(direction * directions / cof.Directions, directions));
                decoded?.TryAdd((path, fileDirection), read);
                layers[layer.Component] = (read, remap);
            }
            catch (Exception problem) when (problem is InvalidDataException or NotSupportedException or OverflowException or IOException)
            {
                // One unreadable part leaves a gap, not a figure missing altogether.
                missing.Add($"{Path.GetFileName(path)} ({problem.Message})");
            }
        }

        var drawn = layers.Values.SelectMany(layer => layer.Frames).Where(frame => frame.Width > 0).ToList();
        if (drawn.Count == 0)
            return null;
        int left = drawn.Min(f => f.Left), top = drawn.Min(f => f.Top);
        int width = drawn.Max(f => f.Left + f.Width) - left, height = drawn.Max(f => f.Top + f.Height) - top;

        // The body without what it holds: the right and left hands' weapons and the shield (5, 6, 7).
        var body = layers.Where(layer => layer.Key is not (5 or 6 or 7))
                         .SelectMany(layer => layer.Value.Frames).Where(frame => frame.Width > 0).ToList();
        if (body.Count == 0)
            body = drawn;

        var frames = new List<FigureFrame>(cof.Frames);
        for (int n = 0; n < cof.Frames; n++)
        {
            var bgra = new uint[width * height];
            foreach (int component in cof.Order(direction, n))
            {
                if (!layers.TryGetValue(component, out var layer) || n >= layer.Frames.Count)
                    continue;
                var frame = layer.Frames[n];
                bool ghost = seeThrough.Contains(component);
                for (int y = 0; y < frame.Height; y++)
                    for (int x = 0; x < frame.Width; x++)
                    {
                        byte index = frame.Indices[y * frame.Width + x];
                        if (index == 0)
                            continue;
                        int at = (frame.Top - top + y) * width + frame.Left - left + x;
                        uint colour = palette[layer.Remap is null ? index : layer.Remap[index]];
                        bgra[at] = ghost ? Over(colour, EtherealAlpha, bgra[at]) : colour;
                    }
            }
            frames.Add(new FigureFrame(bgra));
        }

        // The COF's speed is in 256ths of the game's 25 frames a second.
        double fps = cof.Speed > 0 ? cof.Speed * 25.0 / 256 : 8;
        return new Figure(width, height, -left, -top, fps, frames, missing)
        {
            BodyTop = body.Min(f => f.Top),
            BodyBottom = body.Max(f => f.Top + f.Height),
        };
    }
}

/// <summary>
/// The figures the pages show, composed off the UI thread: decoded layers are kept, so a second
/// character in the same armour, or the same one again, decodes nothing. Installed with the tables,
/// as <see cref="SpriteLoader"/> is.
/// </summary>
public sealed class FigureLoader(GameData game, Palette palette)
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string, int), IReadOnlyList<DccFrame>> _decoded = new();

    public static FigureLoader? Current { get; private set; }

    public static FigureLoader Install(GameData game, Palette palette) => Current = new FigureLoader(game, palette);

    /// <summary>The direction the select screen shows: facing the viewer - the game's direction 0.</summary>
    public const int Facing = 0;

    /// <summary>The look composed, facing the viewer; null where the class has no pose for it. Safe on a worker thread.</summary>
    public Figure? Compose(FigureLook look, int direction = Facing) =>
        CharacterFigure.Compose(game, palette, look, direction, _decoded);
}
