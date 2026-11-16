using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PD2SPCompanion.Shell.Services.Settings;

/// <summary>How far one area of the editor goes: not at all, as far as the game could, or past it.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Reach>))]
public enum Reach
{
    /// <summary>Hidden: Legit.</summary>
    Locked,

    /// <summary>Anything the game could legitimately produce, the grind skipped: Casual.</summary>
    Legal,

    /// <summary>Past what the game makes: Free.</summary>
    Any,
}

/// <summary>
/// What the editor lets a player do - the play mode. Mostly which parts of the screen show; a few
/// settings change how an action behaves instead. Legit, Casual and Free are fixed presets; any
/// other combination is the one Custom.
/// <para>
/// A preset hides a part of the screen rather than refusing the action behind it - single-player,
/// nobody to stop, and nearly every action is reachable only through a button. Where a part is
/// hidden, its shortcuts and gestures go with it.
/// </para>
/// </summary>
public sealed record PlayPolicy(
    // ---- the character
    Reach Character,
    bool TypeGold,
    bool Waypoints,
    bool MapSeed,

    // ---- items
    Reach Properties,
    Reach Details,
    // 1 to 99 wherever it can be set, Free included - it decides which affixes can roll.
    bool ItemLevel,
    bool Ethereal,
    // Locked or Legal only: up to the base's maximum, never past it - the game holds an item to its
    // base's maximum however it was written. Any, from an older file, reads as
    // Legal (Normalized).
    Reach Sockets,
    bool Affixes,
    bool Corruption,
    bool Craft,
    bool Duplicate,
    bool TypeMaterials,
    // The runeword builder. Free, as crafting is.
    bool Runewords,

    // ---- vault and grail. The vault moves, one in and one out, in every mode:
    // Copy - storing and withdrawing copies, Free's - went, as Template had before it; a stored
    // policy's Withdraw is ignored. Pack imports - a switch nothing read once the vault was built and no longer
    // filled from packs - went; a stored policy's VaultImports is ignored.
    // The grail's own Forge button went (the Forge page makes the same items);
    // a stored policy's GrailForge is ignored. What the grail counted as found - Grail: finds, finds and buys, or
    // anything - went, with the editor's finds: found is the game's Discovery; a stored policy's is
    // ignored.

    // ---- trading. Any offer may be made, nothing included, in every mode: the minimum payment (one
    // Worldstone Shard, Legit's) went, deliberately; a stored policy's is ignored.
    bool RepeatBuys,

    // ---- equipping: putting on an item whose
    // requirements are not met, with a warning. Last, with a default, so a Custom policy saved
    // before it reads as allowing it.
    bool ForceEquip = true,

    // ---- duplicates: items that
    // share an id marked wherever they are. Legit's; a Custom policy saved before reads as off.
    bool DetectDuplicates = false,

    // ---- the property picker as the tables list it: every stat once, by id and engine name, rather
    // than the trade site's lines per class, tree and skill - for those who know the engine or come from
    // tools such as Udietoo. No preset turns it on, so it is always Custom's.
    bool RawProperties = false,

    // ---- the Forge: an item made from nothing - a unique, a set piece or a
    // runeword by name, any base in any quality - and kept into the inventory or the vault. Free's;
    // last, with a default, so a Custom policy saved before it reads as without.
    bool Forge = false)
{
    /// <summary>The editor as a manager: vault, grail and trading; nothing edited.</summary>
    public static PlayPolicy Legit { get; } = new(
        Reach.Locked, TypeGold: false, Waypoints: false, MapSeed: false,
        Reach.Locked, Reach.Locked, ItemLevel: false, Ethereal: false, Reach.Locked,
        Affixes: false, Corruption: false, Craft: false, Duplicate: false, TypeMaterials: false,
        Runewords: false,
        RepeatBuys: false,
        // Legit checks whether an equip is legit, counting gear bonuses.
        ForceEquip: false,
        // Meant for those who want a legit experience.
        DetectDuplicates: true);

    /// <summary>Anything the game could legitimately produce, without the grind.</summary>
    public static PlayPolicy Casual { get; } = new(
        Reach.Legal, TypeGold: true, Waypoints: true, MapSeed: true,
        Reach.Legal, Reach.Legal, ItemLevel: true, Ethereal: true, Reach.Legal,
        Affixes: true, Corruption: true, Craft: true, Duplicate: true, TypeMaterials: true,
        Runewords: true,
        RepeatBuys: true);

    /// <summary>Past what the game makes: any value, any property.</summary>
    public static PlayPolicy Free { get; } = new(
        Reach.Any, TypeGold: true, Waypoints: true, MapSeed: true,
        Reach.Any, Reach.Any, ItemLevel: true, Ethereal: true, Reach.Legal,
        Affixes: true, Corruption: true, Craft: true, Duplicate: true, TypeMaterials: true,
        Runewords: true,
        RepeatBuys: true, Forge: true);

    /// <summary>
    /// A stored policy as this editor reads it: sockets past the base's maximum are not a setting any
    /// more (the game would not keep them), so Any is Legal.
    /// </summary>
    public PlayPolicy Normalized() => Sockets == Reach.Any ? this with { Sockets = Reach.Legal } : this;

    public const string LegitName = "Legit", CasualName = "Casual", FreeName = "Free", CustomName = "Custom";

    /// <summary>
    /// A custom mode as it is named where a mode is said in a word - on the Settings page, and in the vault
    /// that keeps it: "Custom - SSF rules".
    /// </summary>
    public static string CustomLabel(string name) => $"{CustomName} - {name}";

    /// <summary>The custom mode's own name out of such a label, or null for a label that is not one.</summary>
    public static string? NameInLabel(string? label) =>
        label is not null && label.StartsWith(CustomName + " - ", StringComparison.Ordinal) && label.Length > CustomName.Length + 3
            ? label[(CustomName.Length + 3)..]
            : null;

    /// <summary>The preset this is, by name, or null for a combination no preset has.</summary>
    public static string? PresetNameOf(PlayPolicy policy) =>
        policy == Legit ? LegitName
        : policy == Casual ? CasualName
        : policy == Free ? FreeName
        : null;

    /// <summary>A preset by name, or null for any other name.</summary>
    public static PlayPolicy? Preset(string? name) => name switch
    {
        LegitName => Legit,
        CasualName => Casual,
        FreeName => Free,
        _ => null,
    };
}

/// <summary>
/// A custom play mode kept under a name:
/// what the Custom panel's list holds beside Legit, Casual and Free, and what a vault that keeps a custom mode
/// carries a copy of.
/// </summary>
public sealed class PolicyPreset
{
    public string Name { get; set; } = "";

    /// <summary>When it was kept, so the list can be put back in the order it was built up.</summary>
    public DateTimeOffset Created { get; set; }

    public PlayPolicy Policy { get; set; } = PlayPolicy.Casual;
}
