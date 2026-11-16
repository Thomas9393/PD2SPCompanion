using System.Collections;

using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// The shared stash's materials tab: 98 counts in the header of <c>pd2_shared.stash</c>, one per
/// kind of material, in place of items.
/// <para>
/// <b>Which material each slot holds is in no table.</b> <c>misc.txt</c> has no column for it and
/// no other excel table lists it; the game keeps the layout itself. So the list below is an
/// engine constant - and it was not copied from
/// anywhere: it was <b>measured</b>, by writing n into slot n and emptying the tab
/// onto a character. Every code came out with a total quantity equal to its slot number, one
/// code per slot. A season that moves a
/// material breaks this list, and the same test re-measures it in two sittings.
/// </para>
/// <para>
/// The tab deals in <b>stack items</b> - <c>r01s</c>, not <c>r01</c> - which is what the game
/// hands out, so taking one out makes a stack. Putting one in takes either form.
/// </para>
/// </summary>
public static class Materials
{
    /// <summary>Slot n's code is <c>Codes[n - 1]</c>. Order is the file's, which is the tab's.</summary>
    public static readonly IReadOnlyList<string> Codes =
    [
        // 1-33: rune stacks, El to Zod
        "r01s", "r02s", "r03s", "r04s", "r05s", "r06s", "r07s", "r08s", "r09s", "r10s", "r11s",
        "r12s", "r13s", "r14s", "r15s", "r16s", "r17s", "r18s", "r19s", "r20s", "r21s", "r22s",
        "r23s", "r24s", "r25s", "r26s", "r27s", "r28s", "r29s", "r30s", "r31s", "r32s", "r33s",
        // 34-40: flawless gems; 41-47: perfect - Topaz, Diamond, Amethyst, Sapphire, Emerald,
        // Ruby, Skull, which is not the order the tab draws them in
        "glys", "glws", "gzvs", "glbs", "glgs", "glrs", "skls",
        "gpys", "gpws", "gpvs", "gpbs", "gpgs", "gprs", "skzs",
        // 48-54: craft infusions, the gem panel's third column
        "crfu", "crfp", "crfc", "crfh", "crfs", "crfb", "crfv",
        // 55-72: keys, organs, uber sigils, clone, Rathma and Lucion materials
        "pk1", "pk2", "pk3", "mbr", "bey", "dhn", "ubaa", "ubab", "ubac",
        "dcbl", "dcso", "dcho", "rtmo", "rtmv", "cm2f", "lucb", "lucc", "lucd",
        // 73-74: the two rejuvenation potions - ordinary items, not stack items
        "rvs", "rvl",
        // 75-84: cube, fragments, shards, Larzuk's box and piece, the four essences
        "imrn", "jewf", "cwss", "wss", "lbox", "lpp", "tes", "ceh", "bet", "fed",
        // 85-98: the bottom panel
        "std", "iwss", "upmp", "irma", "urma", "irra", "rrra",
        "scrb", "fort", "scou", "imma", "upma", "imra", "rera",
    ];

    public const int Count = 98;

    /// <summary>
    /// What Season 14's tab keeps beyond those 98, as slots 99 on - the editor's numbering, not the file's: the
    /// stash's layout says where each one sits (Season 14 writes VERSION 3, every
    /// cell read in game from a numbered file). Kept apart from <see cref="Codes"/>, which the Forge and
    /// the sorting rules read as "a material": a white map or a chipped gem did not become one for them.
    /// </summary>
    public static readonly IReadOnlyList<string> Season14Codes =
    [
        // 99-103: the Ubers tab's new cells - the Uber Tristram map, then the bottom row
        "ubtm", "uba", "dcma", "rtma", "luca",
        // 104-110: the ears, in misc.txt's order; 111-113: Token of Absolution, Lilith's Mirror, Vial of Lightsong
        "ivea", "ivez", "iveb", "ived", "iven", "ivep", "ives", "toa", "llmr", "lsvl",
        // 114-142: the Maps tab, in the file's order - each tier by misc.txt's rows, but tier 1's Westmarch (t26)
        // and Library (t14) the other way; every tier-1 slot with a twin count proven by a hover
        "t11", "t21", "t22", "t24", "t33", "t34", "t36", "t26", "t12", "t25", "t32", "t37", "t38", "t14", "t16",
        "t3a", "t17", "t13", "t23", "t31", "t35", "t39", "t15", "t28", "t27", "t3b", "t29", "t18", "t2a",
        // 143-163: the Gems tab's chipped, flawed and plain rows - Topaz, Diamond, Amethyst, Sapphire, Emerald,
        // Ruby, Skull, as the flawless and perfect rows run in the file
        "gcy", "gcw", "gcv", "gcb", "gcg", "gcr", "skc",
        "gfy", "gfw", "gfv", "gfb", "gfg", "gfr", "skf",
        "gsy", "gsw", "gsv", "gsb", "gsg", "gsr", "sku",
    ];

    /// <summary>Every slot either season's tab may keep: <see cref="Codes"/>, then <see cref="Season14Codes"/>.</summary>
    public static int Every => Count + Season14Codes.Count;

    /// <summary>The code slot n keeps, 1 to <see cref="Every"/>.</summary>
    public static string CodeAt(int slot) => slot <= Count ? Codes[slot - 1] : Season14Codes[slot - Count - 1];

    /// <summary>The field is sixteen bits wide.</summary>
    public const int FieldMaximum = ushort.MaxValue;

    private static readonly Dictionary<string, int> SlotOf =
        Codes.Select((code, index) => (code, slot: index + 1))
             .ToDictionary(pair => pair.code, pair => pair.slot, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, int> AnySlotOf =
        Codes.Concat(Season14Codes).Select((code, index) => (code, slot: index + 1))
             .ToDictionary(pair => pair.code, pair => pair.slot, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The name the tab would give a slot, from the string tables: the plain item's, since Season 14's tables call
    /// the stacks "Nef Rune (old)" and the tab hands out the plain one.
    /// </summary>
    public static string NameOf(int slot) => ItemPresenter.BaseName(Plain(slot));

    /// <summary>The slots that keep stacks with a plain twin: 33 runes, then 14 gems and skulls.</summary>
    public const int StackSlots = 47;

    /// <summary>
    /// A slot's plain item - <c>r01</c> for the stack <c>r01s</c>, <c>gpy</c> for <c>gpys</c> - in the rune, gem and
    /// skull slots; the slot's own code elsewhere. What the tab draws, and what Season 14's tab hands out (a Nef Rune
    /// and a Perfect Topaz taken out in game: <c>r04</c> and <c>gpy</c>, item level 99, quantity 1).
    /// </summary>
    public static string Plain(int slot)
    {
        string code = CodeAt(slot);
        return slot <= StackSlots && code.EndsWith('s') && ItemBases.Of(code[..^1]) is not null ? code[..^1] : code;
    }

    /// <summary>
    /// The slot that keeps an item of this code - by the code itself (<c>wss</c>) or by its stack
    /// form (<c>r30</c> is kept as <c>r30s</c>) - or null when the tab keeps none. As
    /// <see cref="Takes"/> reads a code, for a caller holding the code and not an item.
    /// </summary>
    public static int? SlotFor(string code) =>
        SlotOf.TryGetValue(code, out int slot) || SlotOf.TryGetValue(code + "s", out slot) ? slot : null;

    /// <summary>
    /// The slot an item goes into, and how many it counts for - or null when the tab does not
    /// take it. The stack item itself (<c>r01s</c>) or its single form (<c>r01</c>) both land on
    /// the stack's slot, since that is the one form the tab keeps.
    /// </summary>
    public static (int Slot, int Quantity)? Takes(Item item) => Takes(item, SlotOf);

    /// <summary>
    /// The same over <see cref="Season14Codes"/> too, for a stash that keeps them - asked through the stash
    /// (<c>SharedStash.Takes</c>), which knows which slots its file has. Only a normal item: a magic or unique map
    /// stays an item, as the game's own tab left such maps on the pages.
    /// </summary>
    public static (int Slot, int Quantity)? TakesAny(Item item) =>
        item.Quality is ItemQuality.Normal || item.IsCompact ? Takes(item, AnySlotOf) : null;

    private static (int Slot, int Quantity)? Takes(Item item, Dictionary<string, int> slots)
    {
        string? code = item.Code?.Trim();
        if (code is not { Length: > 0 })
            return null;

        if (slots.TryGetValue(code, out int slot))
            return (slot, Stacks.IsStackable(code) ? Stacks.Shown(item.Quantity) : 1);

        // The single form: one of them in Season 13, where it never stacked; in 14 a rune and a gem stack themselves,
        // and a stack of thirty counted as one would lose twenty-nine.
        if (slots.TryGetValue(code + "s", out slot))
            return (slot, Stacks.IsStackable(code) ? Stacks.Shown(item.Quantity) : 1);

        return null;
    }
}
